using System.Net;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Refresh.Core.Configuration;
using Refresh.Core.Storage;

namespace RefreshTests.GameServer.Tests.Storage;

public class ObjectStorageTests
{
    private static Dictionary<string, string> ValidSettings() => new()
    {
        ["S3__Enabled"] = "true",
        ["S3__Endpoint"] = "https://storage.example.com",
        ["S3__BucketName"] = "assets",
        ["S3__AccessKeyId"] = "test-id",
        ["S3__SecretAccessKey"] = "test-secret",
        ["S3__Region"] = "us-east-1",
    };

    [Test]
    public void DisabledByDefault() =>
        Assert.That(ObjectStorageSettings.FromEnvironment(_ => null).Enabled, Is.False);

    [Test]
    public void LoadsExplicitSettings()
    {
        Dictionary<string, string> values = ValidSettings();
        values["S3__ForcePathStyle"] = "false";
        ObjectStorageSettings settings = ObjectStorageSettings.FromEnvironment(key => values.GetValueOrDefault(key));
        Assert.Multiple(() =>
        {
            Assert.That(settings.Enabled, Is.True);
            Assert.That(settings.Endpoint, Is.EqualTo(values["S3__Endpoint"]));
            Assert.That(settings.BucketName, Is.EqualTo("assets"));
            Assert.That(settings.ForcePathStyle, Is.False);
        });
    }

    [TestCase("S3__BucketName")]
    [TestCase("S3__AccessKeyId")]
    [TestCase("S3__SecretAccessKey")]
    [TestCase("S3__Region")]
    public void RejectsMissingSettingsWithoutLeakingCredentials(string missing)
    {
        Dictionary<string, string> values = ValidSettings();
        values.Remove(missing);
        InvalidOperationException? error = Assert.Throws<InvalidOperationException>(() =>
            ObjectStorageSettings.FromEnvironment(key => values.GetValueOrDefault(key)));
        Assert.That(error!.Message, Does.Not.Contain("test-secret"));
    }

    [TestCase("http://storage.example.com")]
    [TestCase("https://user:secret@storage.example.com")]
    [TestCase("https://storage.example.com?token=secret")]
    public void RejectsUnsafeEndpoints(string endpoint)
    {
        Dictionary<string, string> values = ValidSettings();
        values["S3__Endpoint"] = endpoint;
        Assert.Throws<InvalidOperationException>(() =>
            ObjectStorageSettings.FromEnvironment(key => values.GetValueOrDefault(key)));
    }

    [Test]
    public void UploadStreamCommitsOnDisposeOnlyOnce()
    {
        using FakeS3 client = new();
        ObjectStorageDataStore store = new(client, "assets");
        Stream stream = store.OpenWriteStream("png/test");
        stream.Write([1, 2, 3]);
        Assert.That(client.Writes, Is.Zero);
        stream.Dispose();
        stream.Dispose();
        Assert.Multiple(() =>
        {
            Assert.That(client.Writes, Is.EqualTo(1));
            Assert.That(client.Uploaded, Is.EqualTo(new byte[] { 1, 2, 3 }));
        });
    }

    [Test]
    public void ReadsAreSeekableAndDoNotRequireSourceLength()
    {
        using FakeS3 client = new();
        ObjectStorageDataStore store = new(client, "assets");
        using Stream stream = store.GetStreamFromStore("test");
        Assert.Multiple(() =>
        {
            Assert.That(stream.CanSeek, Is.True);
            Assert.That(stream.Length, Is.EqualTo(3));
            Assert.That(stream.ReadByte(), Is.EqualTo(1));
        });
    }

    [Test]
    public void ListsAllPagesAndAcceptsDeleteNoContent()
    {
        using FakeS3 client = new();
        ObjectStorageDataStore store = new(client, "assets");
        Assert.That(store.GetKeysFromStore(), Is.EqualTo(new[] { "first", "second" }));
        Assert.That(store.RemoveFromStore("first"), Is.True);
    }

    [Test]
    public void MissingObjectsReturnFalseButForbiddenThrows()
    {
        using FakeS3 client = new();
        ObjectStorageDataStore store = new(client, "assets");
        Assert.That(store.ExistsInStore("missing"), Is.False);
        client.MetadataStatus = HttpStatusCode.Forbidden;
        Assert.Throws<AmazonS3Exception>(() => store.ExistsInStore("forbidden"));
    }

    private sealed class FakeS3() : AmazonS3Client(new AnonymousAWSCredentials(), new AmazonS3Config
    {
        ServiceURL = "https://storage.example.com",
        AuthenticationRegion = "us-east-1",
    })
    {
        public int Writes { get; private set; }
        public byte[] Uploaded { get; private set; } = [];
        public HttpStatusCode MetadataStatus { get; set; } = HttpStatusCode.NotFound;

        public override Task<PutObjectResponse> PutObjectAsync(PutObjectRequest request, CancellationToken cancellationToken = default)
        {
            using MemoryStream copy = new();
            request.InputStream.CopyTo(copy);
            this.Uploaded = copy.ToArray();
            this.Writes++;
            return Task.FromResult(new PutObjectResponse { HttpStatusCode = HttpStatusCode.OK });
        }

        public override Task<GetObjectResponse> GetObjectAsync(string bucketName, string key, CancellationToken cancellationToken = default) =>
            Task.FromResult(new GetObjectResponse { ResponseStream = new MemoryStream([1, 2, 3]) });

        public override Task<GetObjectMetadataResponse> GetObjectMetadataAsync(string bucketName, string key, CancellationToken cancellationToken = default) =>
            Task.FromException<GetObjectMetadataResponse>(new AmazonS3Exception("test") { StatusCode = this.MetadataStatus });

        public override Task<DeleteObjectResponse> DeleteObjectAsync(string bucketName, string key, CancellationToken cancellationToken = default) =>
            Task.FromResult(new DeleteObjectResponse { HttpStatusCode = HttpStatusCode.NoContent });

        public override Task<ListObjectsV2Response> ListObjectsV2Async(ListObjectsV2Request request, CancellationToken cancellationToken = default)
        {
            bool first = request.ContinuationToken == null;
            return Task.FromResult(new ListObjectsV2Response
            {
                IsTruncated = first,
                NextContinuationToken = first ? "next" : null,
                S3Objects = [new S3Object { Key = first ? "first" : "second" }],
            });
        }
    }
}
