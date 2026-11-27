using Audorix.KnockTwice.Api.SupportQueue;

namespace Audorix.KnockTwice.UnitTests;

public sealed class SupportQueueSettingsTests
{
    [Fact]
    public void Database_is_the_default_and_needs_no_redis_configuration()
    {
        var result = new SupportQueueSettingsValidator().Validate(null, new SupportQueueSettings());

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Redis_requires_a_valid_connection_and_stream_prefix()
    {
        var missing = new SupportQueueSettingsValidator().Validate(null, new SupportQueueSettings
        {
            Provider = "Redis",
        });

        Assert.False(missing.Succeeded);
        Assert.Contains(missing.Failures ?? [], failure => failure.Contains("ConnectionString is required", StringComparison.Ordinal));

        var valid = new SupportQueueSettingsValidator().Validate(null, new SupportQueueSettings
        {
            Provider = "Redis",
            Redis = new RedisQueueSettings
            {
                ConnectionString = "localhost:6379",
                StreamPrefix = "chatx-support-test",
            },
        });

        Assert.True(valid.Succeeded);
    }
}
