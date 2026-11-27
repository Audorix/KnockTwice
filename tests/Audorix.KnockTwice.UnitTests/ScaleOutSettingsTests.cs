using Audorix.KnockTwice.Api.ScaleOut;

namespace Audorix.KnockTwice.UnitTests;

/// <summary>Phase 21: scale-out configuration.</summary>
public sealed class ScaleOutSettingsTests
{
    [Fact]
    public void Off_by_default()
    {
        var settings = new ScaleOutSettings();
        Assert.False(settings.Enabled);
        Assert.Empty(settings.Validate());
    }

    [Theory]
    [InlineData("redis:6379", true)]
    [InlineData("redis-1:6379,redis-2:6379,password=secret,ssl=true,abortConnect=false", true)]
    [InlineData("redis:6379,notAnOption=1", false)]
    public void Redis_connection_strings_are_checked(string redis, bool valid) =>
        Assert.Equal(valid, new ScaleOutSettings { Redis = redis, Prefix = "test-staging" }.Validate().Count == 0);

    [Fact]
    public void Distributed_mode_requires_an_environment_specific_prefix() =>
        Assert.Contains(new ScaleOutSettings { Redis = "redis:6379" }.Validate(), error => error.Contains("ScaleOut:Prefix", StringComparison.Ordinal));

    [Theory]
    [InlineData("chatx", true)]
    [InlineData("tenant_1-prod", true)]
    [InlineData("", false)]
    [InlineData("has space", false)]
    [InlineData("a:b", false)]
    public void Prefixes_are_plain(string prefix, bool valid) =>
        Assert.Equal(valid, new ScaleOutSettings { Redis = "redis:6379", Prefix = prefix }.Validate().Count == 0);
}
