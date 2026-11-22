using Audorix.ChatX.Domain;
using Audorix.ChatX.Domain.Installations;

namespace Audorix.ChatX.UnitTests;

public sealed class InstallationTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 22, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Initialize_normalizes_language_and_sets_both_timestamps()
    {
        var installation = Installation.Initialize(Guid.NewGuid(), " ES ", Now);

        Assert.Equal("es", installation.DefaultLanguage);
        Assert.Equal(Now, installation.InitializedAtUtc);
        Assert.Equal(Now, installation.UpdatedAtUtc);
    }

    [Theory]
    [InlineData("")]
    [InlineData("english")]
    [InlineData("en-US")]
    [InlineData("e1")]
    public void Initialize_rejects_invalid_language_codes(string language)
    {
        var ex = Assert.Throws<DomainRuleException>(() => Installation.Initialize(Guid.NewGuid(), language, Now));
        Assert.Equal("invalid_language_code", ex.Code);
    }

    [Fact]
    public void Initialize_rejects_empty_identifier_and_non_utc_time()
    {
        Assert.Equal("installation_id_required",
            Assert.Throws<DomainRuleException>(() => Installation.Initialize(Guid.Empty, "en", Now)).Code);
        Assert.Equal("timestamp_not_utc",
            Assert.Throws<DomainRuleException>(() => Installation.Initialize(Guid.NewGuid(), "en", Now.ToOffset(TimeSpan.FromHours(2)))).Code);
    }

    [Fact]
    public void ChangeDefaultLanguage_refuses_to_move_update_time_backwards()
    {
        var installation = Installation.Initialize(Guid.NewGuid(), "en", Now);

        var ex = Assert.Throws<DomainRuleException>(() => installation.ChangeDefaultLanguage("es", Now.AddMinutes(-1)));

        Assert.Equal("installation_clock_regression", ex.Code);
        Assert.Equal("en", installation.DefaultLanguage);
    }
}
