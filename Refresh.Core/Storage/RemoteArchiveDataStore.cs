using System.Diagnostics;
using System.Security.Cryptography;
using Bunkum.Core.Storage;
using JetBrains.Annotations;
using Refresh.Common.Verification;
using Refresh.Core.Metrics;

namespace Refresh.Core.Storage;

public sealed class RemoteArchiveDataStore : IDataStore
{
    private const string WriteError = $"{nameof(RemoteArchiveDataStore)} is a read-only source, and cannot be written to.";
    private static readonly HttpClient DefaultClient = new(new HttpClientHandler { AllowAutoRedirect = false });

    private readonly Uri _baseUri;
    private readonly HttpClient _client;
    private readonly ThreadLocal<Stopwatch> _sw = new(() => new Stopwatch());

    public RemoteArchiveDataStore(string baseUrl, HttpClient? client = null)
    {
        this._baseUri = new Uri(baseUrl, UriKind.Absolute);
        this._client = client ?? DefaultClient;
    }

    [Pure]
    private Uri? GetUri(ReadOnlySpan<char> hash)
    {
        if (!CommonPatterns.Sha1Regex().IsMatch(hash))
            return null;

        return new Uri(this._baseUri, $"{hash}.bin");
    }

    public bool ExistsInStore(string key)
    {
        Uri? uri = this.GetUri(key);
        if (uri == null) throw new FormatException("The key was invalid.");

        DataStoreMetrics.RecordRemoteCheck();
        using HttpRequestMessage request = new(HttpMethod.Head, uri);
        using HttpResponseMessage response = this._client.SendAsync(request).GetAwaiter().GetResult();
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return false;
        response.EnsureSuccessStatusCode();
        return true;
    }

    public bool WriteToStore(string key, byte[] data) => throw new InvalidOperationException(WriteError);

    public byte[] GetDataFromStore(string key)
    {
        Uri? uri = this.GetUri(key);
        if (uri == null) throw new FormatException("The key was invalid.");

        this._sw.Value!.Restart();
        using HttpResponseMessage response = this._client.GetAsync(uri).GetAwaiter().GetResult();
        response.EnsureSuccessStatusCode();
        byte[] data = response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
        this._sw.Value!.Stop();

        string actualHash = Convert.ToHexString(SHA1.HashData(data)).ToLowerInvariant();
        if (!string.Equals(actualHash, key, StringComparison.Ordinal))
            throw new InvalidDataException($"The remote archive returned data that did not match asset hash '{key}'.");

        DataStoreMetrics.RecordRemote(this._sw.Value);
        return data;
    }

    public bool RemoveFromStore(string key) => throw new InvalidOperationException(WriteError);
    public string[] GetKeysFromStore() => [];
    public bool WriteToStoreFromStream(string key, Stream data) => throw new InvalidOperationException(WriteError);
    public Stream GetStreamFromStore(string key) => new MemoryStream(this.GetDataFromStore(key), writable: false);
    public Stream OpenWriteStream(string key) => throw new InvalidOperationException(WriteError);
}
