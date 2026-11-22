using System.Collections.Concurrent;
using Bunkum.Core.Storage;

namespace Refresh.Core.Storage;

public class DownloadingDataStore : IDataStore
{
    private readonly IDataStore _localDataStore;
    private readonly IDataStore _remoteDataStore;
    private readonly ConcurrentDictionary<string, int> _hits = [];

    private readonly int _hitsBeforeDownload;
    
    private const string WriteError = $"{nameof(DownloadingDataStore)} is a read-only source, and cannot be written to.";

    public DownloadingDataStore(IDataStore localDataStore, IDataStore remoteDataStore, int hitsBeforeDownload = 3)
    {
        if (hitsBeforeDownload < 1)
            throw new ArgumentOutOfRangeException(nameof(hitsBeforeDownload), "The cache threshold must be at least one.");

        this._localDataStore = localDataStore;
        this._remoteDataStore = remoteDataStore;
        this._hitsBeforeDownload = hitsBeforeDownload;
    }

    private bool Hit(string key)
    {
        int value = this._hits.AddOrUpdate(key, 1, (_, current) => current + 1);
        return value >= this._hitsBeforeDownload;
    }

    
    public bool ExistsInStore(string key)
    {
        if (this._hits.ContainsKey(key))
            return true;

        return this._remoteDataStore.ExistsInStore(key);
    }

    public bool WriteToStore(string key, byte[] data)
    {
        throw new InvalidOperationException(WriteError);
    }

    public byte[] GetDataFromStore(string key)
    {
        byte[] data = this._remoteDataStore.GetDataFromStore(key);
        if (this.Hit(key))
        {
            this._hits.Remove(key, out _);
            if (!this._localDataStore.WriteToStore(key, data))
                throw new IOException($"Could not cache remote asset '{key}' in the primary data store.");
        }

        return data;
    }

    public bool RemoveFromStore(string key)
    {
        throw new InvalidOperationException(WriteError);
    }

    public string[] GetKeysFromStore()
    {
        return this._remoteDataStore.GetKeysFromStore();
    }

    public bool WriteToStoreFromStream(string key, Stream data)
    {
        throw new InvalidOperationException(WriteError);
    }

    public Stream GetStreamFromStore(string key)
    {
        Stream data = this._remoteDataStore.GetStreamFromStore(key);
        if (this.Hit(key))
        {
            this._hits.Remove(key, out _);
            if (!this._localDataStore.WriteToStoreFromStream(key, data))
                throw new IOException($"Could not cache remote asset '{key}' in the primary data store.");
            data.Seek(0, SeekOrigin.Begin);
        }

        return data;
    }

    public Stream OpenWriteStream(string key)
    {
        throw new InvalidOperationException(WriteError);
    }
}