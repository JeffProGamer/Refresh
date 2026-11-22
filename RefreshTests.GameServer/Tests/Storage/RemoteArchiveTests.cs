using System.Net;
using System.Security.Cryptography;
using System.Text;
using Bunkum.Core.Storage;
using NotEnoughLogs;
using Refresh.Core.Configuration;
using Refresh.Core.Services;
using Refresh.Core.Storage;
using Refresh.Database.Models.Authentication;
using Refresh.Database.Models.Levels;
using Refresh.Database.Models.Statistics;
using Refresh.Database.Query;
using RefreshTests.GameServer.Logging;

namespace RefreshTests.GameServer.Tests.Storage;

public class RemoteArchiveTests
{
    [Test]
    public void SettingsAreDisabledByDefaultAndRequireAnHttpsBaseUrlWhenEnabled()
    {
        Assert.That(RemoteArchiveSettings.FromEnvironment(_ => null).Enabled, Is.False);

        Dictionary<string, string> values = new()
        {
            ["RemoteArchive__Enabled"] = "true",
            ["RemoteArchive__BaseUrl"] = "https://archive.example.com/assets",
        };
        RemoteArchiveSettings settings = RemoteArchiveSettings.FromEnvironment(key => values.GetValueOrDefault(key));
        Assert.Multiple(() =>
        {
            Assert.That(settings.Enabled, Is.True);
            Assert.That(settings.BaseUrl, Is.EqualTo("https://archive.example.com/assets/"));
        });

        values["RemoteArchive__BaseUrl"] = "http://archive.example.com";
        Assert.Throws<InvalidOperationException>(() =>
            RemoteArchiveSettings.FromEnvironment(key => values.GetValueOrDefault(key)));
    }

    [Test]
    public void SearchApiUrlIsValidatedAndNormalized()
    {
        Dictionary<string, string> values = new()
        {
            ["RemoteArchive__Enabled"] = "true",
            ["RemoteArchive__BaseUrl"] = "https://archive.example.com/assets",
            ["RemoteArchive__SearchApiUrl"] = "https://archive.example.com/api/v3",
        };

        RemoteArchiveSettings settings = RemoteArchiveSettings.FromEnvironment(key => values.GetValueOrDefault(key));
        Assert.That(settings.SearchApiUrl, Is.EqualTo("https://archive.example.com/api/v3/"));

        values["RemoteArchive__SearchApiUrl"] = "http://archive.example.com/api/v3";
        Assert.Throws<InvalidOperationException>(() =>
            RemoteArchiveSettings.FromEnvironment(key => values.GetValueOrDefault(key)));
    }

    [Test]
    public void SearchRequestsRemoteMetadataAndMapsTheReturnedLevel()
    {
        const string rootHash = "0000000000000000000000000000000000000000";
        const string iconHash = "1111111111111111111111111111111111111111";
        const string responseJson = """
            {
              "success": true,
              "listInfo": { "nextPageIndex": 2, "totalItems": 1 },
              "data": [{
                "rootLevelHash": "0000000000000000000000000000000000000000",
                "iconHash": "1111111111111111111111111111111111111111",
                "title": "Archived level",
                "description": "Imported from the archive",
                "gameVersion": 2,
                "levelType": 0,
                "publisher": { "username": "Original creator" },
                "hearts": 8
              }]
            }
            """;

        RecordingJsonHandler handler = new(responseJson);
        using HttpClient client = new(handler);
        using Logger logger = new([new NUnitSink()]);
        RemoteArchiveSearchService service = new(logger, "https://archive.example.com/api/v3/", client);

        RemoteArchiveSearchPage? page = service.Search(
            "archived level", 0, 20, new LevelFilterSettings(TokenGame.LittleBigPlanet3));
        GameLevel? level = page?.Data?.Single().ToGameLevel();
        Uri requestUri = handler.RequestUri ?? throw new InvalidOperationException("The search request was not sent.");
        GameLevel importedLevel = level ?? throw new InvalidOperationException("The returned archive level was invalid.");
        GameLevelStatistics statistics = importedLevel.Statistics ??
            throw new InvalidOperationException("The archive level statistics were not mapped.");

        Assert.Multiple(() =>
        {
            Assert.That(requestUri.AbsolutePath, Is.EqualTo("/api/v3/levels/search"));
            Assert.That(requestUri.Query, Does.Contain("query=archived%20level"));
            Assert.That(requestUri.Query, Does.Contain("skip=1"));
            Assert.That(requestUri.Query, Does.Contain("game=lbp1"));
            Assert.That(requestUri.Query, Does.Contain("game=lbp2"));
            Assert.That(requestUri.Query, Does.Contain("game=lbp3"));
            Assert.That(importedLevel.RootResource, Is.EqualTo(rootHash));
            Assert.That(importedLevel.IconHash, Is.EqualTo(iconHash));
            Assert.That(importedLevel.Title, Is.EqualTo("Archived level"));
            Assert.That(importedLevel.GameVersion, Is.EqualTo(TokenGame.LittleBigPlanet3));
            Assert.That(importedLevel.IsReUpload, Is.False);
            Assert.That(importedLevel.OriginalPublisher, Is.EqualTo("Original creator"));
            Assert.That(statistics.FavouriteCount, Is.EqualTo(8));
        });
    }

    [Test]
    public void ReadsByHashAndCachesFirstSuccessfulFetch()
    {
        byte[] content = Encoding.UTF8.GetBytes("archived-level");
        string hash = Convert.ToHexString(SHA1.HashData(content)).ToLowerInvariant();
        RecordingHandler handler = new(content);
        using HttpClient client = new(handler);
        RemoteArchiveDataStore remote = new("https://archive.example.com/dry/", client);
        InMemoryDataStore primary = new();
        DownloadingDataStore store = new(primary, remote, hitsBeforeDownload: 1);

        Assert.That(store.ExistsInStore(hash), Is.True);
        Assert.That(store.GetDataFromStore(hash), Is.EqualTo(content));

        Assert.Multiple(() =>
        {
            Assert.That(primary.ExistsInStore(hash), Is.True);
            Assert.That(primary.GetDataFromStore(hash), Is.EqualTo(content));
            Assert.That(handler.Requests.Select(request => request.Method), Is.EqualTo(new[] { "HEAD", "GET" }));
            Assert.That(handler.Requests.All(request =>
                request.Path == $"/dry/{hash}.bin"), Is.True);
        });
    }

    [Test]
    public void BonsaiAssetsAreRequestedWithoutTheBinExtension()
    {
        byte[] content = Encoding.UTF8.GetBytes("archived-level");
        string hash = Convert.ToHexString(SHA1.HashData(content)).ToLowerInvariant();
        RecordingHandler handler = new(content);
        using HttpClient client = new(handler);
        RemoteArchiveDataStore remote = new("https://lbp.lbpbonsai.com/api/v3/assets/", client);

        Assert.That(remote.ExistsInStore(hash), Is.True);
        Assert.That(remote.GetDataFromStore(hash), Is.EqualTo(content));

        Assert.That(handler.Requests.Select(request => request.Path),
            Is.EqualTo(new[] { $"/api/v3/assets/{hash}", $"/api/v3/assets/{hash}" }));
    }

    [Test]
    public void ExistingArchiveSourcesKeepTheThreeReadCacheThresholdByDefault()
    {
        byte[] content = Encoding.UTF8.GetBytes("archived-level");
        string hash = Convert.ToHexString(SHA1.HashData(content)).ToLowerInvariant();
        RecordingHandler handler = new(content);
        using HttpClient client = new(handler);
        RemoteArchiveDataStore remote = new("https://archive.example.com/", client);
        InMemoryDataStore primary = new();
        DownloadingDataStore store = new(primary, remote);

        store.GetDataFromStore(hash);
        store.GetDataFromStore(hash);
        Assert.That(primary.ExistsInStore(hash), Is.False);

        store.GetDataFromStore(hash);
        Assert.That(primary.GetDataFromStore(hash), Is.EqualTo(content));
    }

    [Test]
    public void RejectsInvalidHashesAndMismatchedRemoteContent()
    {
        RecordingHandler handler = new(Encoding.UTF8.GetBytes("different"));
        using HttpClient client = new(handler);
        RemoteArchiveDataStore store = new("https://archive.example.com/", client);

        Assert.Throws<FormatException>(() => store.ExistsInStore("../invalid"));
        Assert.Throws<InvalidDataException>(() => store.GetDataFromStore(new string('a', 40)));
        Assert.That(handler.Requests, Has.Count.EqualTo(1));
    }

    private sealed class RecordingHandler(byte[] content) : HttpMessageHandler
    {
        public List<(string Method, string Path)> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            this.Requests.Add((request.Method.Method, request.RequestUri!.AbsolutePath));
            HttpResponseMessage response = request.Method == HttpMethod.Head
                ? new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                : new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(content),
                };
            return Task.FromResult(response);
        }
    }

    private sealed class RecordingJsonHandler(string json) : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            this.RequestUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            });
        }
    }
}
