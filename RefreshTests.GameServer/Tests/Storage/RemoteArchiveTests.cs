using System.Security.Cryptography;
using System.Text;
using Bunkum.Core.Storage;
using Refresh.Core.Configuration;
using Refresh.Core.Storage;

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
    public void ReadsByHashAndCachesFirstSuccessfulFetch()
    {
        byte[] content = Encoding.UTF8.GetBytes("archived-level");
        string hash = Convert.ToHexString(SHA1.HashData(content)).ToLowerInvariant();
        RecordingHandler handler = new(content);
        using HttpClient client = new(handler);
        RemoteArchiveDataStore remote = new("https://archive.example.com/assets/", client);
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
                request.Path == $"/assets/{hash}.bin"), Is.True);
        });
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
}
