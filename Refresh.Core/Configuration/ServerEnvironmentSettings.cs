namespace Refresh.Core.Configuration;

public static class ServerEnvironmentSettings
{
    public static void Apply(GameServerConfig gameServer, Func<string, string?>? read = null)
    {
        read ??= Environment.GetEnvironmentVariable;

        Set("Server__Name", value => gameServer.InstanceName = value);
        Set("Server__Description", value => gameServer.InstanceDescription = value);
        Set("Server__LicenseText", value => gameServer.LicenseText = value);
        Set("Server__WelcomeNotificationTitle", value => gameServer.WelcomeNotificationTitle = value);
        Set("Server__WelcomeNotificationText", value => gameServer.WelcomeNotificationText = value);
        Set("Server__PodNewsTitle", value => gameServer.PodNewsTitle = value);
        Set("Server__PodNewsText", value => gameServer.PodNewsText = value);
        Set("Server__GameConfigStorageUrl", value => gameServer.GameConfigStorageUrl = ValidateUrl("Server__GameConfigStorageUrl", value, allowPath: true));

        string? publicUrl = read("Server__PublicUrl");
        if (!string.IsNullOrWhiteSpace(publicUrl))
        {
            publicUrl = ValidateUrl("Server__PublicUrl", publicUrl, allowPath: false);
            gameServer.WebExternalUrl = publicUrl;

            if (string.IsNullOrWhiteSpace(read("Server__GameConfigStorageUrl")))
                gameServer.GameConfigStorageUrl = $"{publicUrl}/lbp";
        }

        void Set(string key, Action<string> assign)
        {
            string? value = read(key);
            if (value != null) assign(value);
        }
    }

    private static string ValidateUrl(string key, string value, bool allowPath)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment) ||
            (!allowPath && uri.AbsolutePath != "/"))
        {
            throw new InvalidOperationException($"{key} must be an HTTPS URL without credentials, query, or fragment.");
        }

        return value.TrimEnd('/');
    }
}
