namespace Refresh.Core.Configuration;

/// <summary>Explicit environment settings for the optional S3 primary asset store.</summary>
public sealed class ObjectStorageSettings
{
    public bool Enabled { get; private init; }
    public string Endpoint { get; private init; } = "";
    public string BucketName { get; private init; } = "";
    public string AccessKeyId { get; private init; } = "";
    public string SecretAccessKey { get; private init; } = "";
    public string Region { get; private init; } = "us-east-1";
    public bool ForcePathStyle { get; private init; } = true;

    public static ObjectStorageSettings FromEnvironment(Func<string, string?>? read = null)
    {
        read ??= Environment.GetEnvironmentVariable;
        bool ReadBool(string key, bool fallback)
        {
            string? value = read(key);
            if (value == null) return fallback;
            if (bool.TryParse(value, out bool result)) return result;
            throw new InvalidOperationException($"{key} must be true or false.");
        }
        string Required(string key)
        {
            string? value = read(key);
            return !string.IsNullOrWhiteSpace(value) ? value :
                throw new InvalidOperationException($"S3 storage requires {key}.");
        }

        if (!ReadBool("S3__Enabled", false)) return new ObjectStorageSettings();
        string endpoint = Required("S3__Endpoint");
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out Uri? uri) ||
            uri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            throw new InvalidOperationException("S3__Endpoint must be an HTTPS service URL without credentials, query, or fragment.");

        return new ObjectStorageSettings
        {
            Enabled = true,
            Endpoint = endpoint,
            BucketName = Required("S3__BucketName"),
            AccessKeyId = Required("S3__AccessKeyId"),
            SecretAccessKey = Required("S3__SecretAccessKey"),
            Region = Required("S3__Region"),
            ForcePathStyle = ReadBool("S3__ForcePathStyle", true),
        };
    }
}
