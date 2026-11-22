using System.Net;
using Amazon.S3;
using Amazon.S3.Model;
using Bunkum.Core.Storage;
using Refresh.Core.Configuration;

namespace Refresh.Core.Storage;

/// <summary>S3 primary storage. Reads are buffered to support image decoders requiring seekable streams.</summary>
public sealed class ObjectStorageDataStore(IAmazonS3 client, string bucketName) : IDataStore
{
    public static IDataStore Create(ObjectStorageSettings settings)
    {
        AmazonS3Config config = new()
        {
            ServiceURL = settings.Endpoint,
            AuthenticationRegion = settings.Region,
            ForcePathStyle = settings.ForcePathStyle,
        };
        return new ObjectStorageDataStore(
            new AmazonS3Client(settings.AccessKeyId, settings.SecretAccessKey, config), settings.BucketName);
    }

    public bool ExistsInStore(string key)
    {
        try
        {
            client.GetObjectMetadataAsync(bucketName, key).GetAwaiter().GetResult();
            return true;
        }
        catch (AmazonS3Exception exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    public bool WriteToStore(string key, byte[] data)
    {
        using MemoryStream stream = new(data, writable: false);
        return this.WriteToStoreFromStream(key, stream);
    }

    public bool WriteToStoreFromStream(string key, Stream data)
    {
        PutObjectResponse response = client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = bucketName,
            Key = key,
            InputStream = data,
            AutoCloseStream = false,
            AutoResetStreamPosition = false,
        }).GetAwaiter().GetResult();
        return response.HttpStatusCode == HttpStatusCode.OK;
    }

    public byte[] GetDataFromStore(string key)
    {
        using GetObjectResponse response = client.GetObjectAsync(bucketName, key).GetAwaiter().GetResult();
        using MemoryStream buffer = new();
        response.ResponseStream.CopyTo(buffer);
        return buffer.ToArray();
    }

    public Stream GetStreamFromStore(string key) => new MemoryStream(this.GetDataFromStore(key), writable: false);

    public bool RemoveFromStore(string key)
    {
        DeleteObjectResponse response = client.DeleteObjectAsync(bucketName, key).GetAwaiter().GetResult();
        return response.HttpStatusCode is HttpStatusCode.OK or HttpStatusCode.NoContent;
    }

    public string[] GetKeysFromStore()
    {
        List<string> keys = [];
        ListObjectsV2Request request = new() { BucketName = bucketName };
        ListObjectsV2Response response;
        do
        {
            response = client.ListObjectsV2Async(request).GetAwaiter().GetResult();
            keys.AddRange(response.S3Objects.Select(obj => obj.Key));
            request.ContinuationToken = response.NextContinuationToken;
        } while (response.IsTruncated);
        return keys.ToArray();
    }

    // Derived images are uploaded when the encoder disposes the output stream.
    public Stream OpenWriteStream(string key) => new UploadStream(stream =>
    {
        if (!this.WriteToStoreFromStream(key, stream))
            throw new IOException("S3 did not acknowledge the asset upload.");
    });

    private sealed class UploadStream(Action<Stream> upload) : MemoryStream
    {
        private bool _disposed;

        protected override void Dispose(bool disposing)
        {
            if (disposing && !this._disposed)
            {
                this._disposed = true;
                try
                {
                    this.Position = 0;
                    upload(this);
                }
                finally
                {
                    base.Dispose(disposing);
                }
                return;
            }
            base.Dispose(disposing);
        }
    }
}
