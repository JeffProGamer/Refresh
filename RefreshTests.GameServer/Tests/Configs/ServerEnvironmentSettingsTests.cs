using Refresh.Core.Configuration;

namespace RefreshTests.GameServer.Tests.Configs;

public class ServerEnvironmentSettingsTests
{
    [Test]
    public void AppliesServerIdentityAndDerivesGameConfigUrlFromPublicUrl()
    {
        Dictionary<string, string> values = new()
        {
            ["Server__Name"] = "LBP Reborn",
            ["Server__PublicUrl"] = "https://lbpreborn.onrender.com",
            ["Server__PodNewsTitle"] = "News",
            ["Server__PodNewsText"] = "Welcome to the server",
        };
        GameServerConfig config = new();

        ServerEnvironmentSettings.Apply(config, key => values.GetValueOrDefault(key));

        Assert.Multiple(() =>
        {
            Assert.That(config.InstanceName, Is.EqualTo("LBP Reborn"));
            Assert.That(config.WebExternalUrl, Is.EqualTo("https://lbpreborn.onrender.com"));
            Assert.That(config.GameConfigStorageUrl, Is.EqualTo("https://lbpreborn.onrender.com/lbp"));
            Assert.That(config.PodNewsTitle, Is.EqualTo("News"));
            Assert.That(config.PodNewsText, Is.EqualTo("Welcome to the server"));
        });
    }

    [Test]
    public void RejectsNonHttpsPublicUrls()
    {
        GameServerConfig config = new();

        Assert.Throws<InvalidOperationException>(() =>
            ServerEnvironmentSettings.Apply(config, key =>
                key == "Server__PublicUrl" ? "http://lbpreborn.onrender.com" : null));
    }
}
