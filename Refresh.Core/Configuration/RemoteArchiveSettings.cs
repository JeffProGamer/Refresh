namespace Refresh.Core.Configuration;

public sealed class RemoteArchiveSettings
{
    public bool Enabled { get; private init; }
    public string BaseUrl { get; private init; } = "";

    public static RemoteArchiveSettings FromEnvironment(Func<string, string?>? read = null)
    {
        read ??= Environment.GetEnvironmentVariable;
        string? enabledValue = read("RemoteArchive__Enabled");
        bool enabled = enabledValue == null
            ? false
            : bool.TryParse(enabledValue, out bool parsed)
                ? parsed
                : throw new InvalidOperationException("RemoteArchive__Enabled must be true or false.");

        if (!enabled) return new RemoteArchiveSettings();

        string? baseUrl = read("RemoteArchive__BaseUrl");
        if (string.IsNullOrWhiteSpace(baseUrl) ||
            !Uri.TryCreate(baseUrl, UriKind.Absolute, out Uri? uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment))
        {
            throw new InvalidOperationException(
                "RemoteArchive__BaseUrl must be an HTTPS URL without credentials, query, or fragment.");
        }

        return new RemoteArchiveSettings
        {
            Enabled = true,
            BaseUrl = baseUrl.EndsWith('/') ? baseUrl : baseUrl + '/',
        };
    }
}
