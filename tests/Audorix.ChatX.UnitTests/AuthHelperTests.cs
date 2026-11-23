using Audorix.ChatX.Api.Identity;
using Audorix.ChatX.Application.Identity;
using Audorix.ChatX.Infrastructure.Persistence;

namespace Audorix.ChatX.UnitTests;

public sealed class AuthHelperTests
{
    [Theory]
    [InlineData("/account", "/account")]
    [InlineData("/account?tab=sessions", "/account?tab=sessions")]
    [InlineData(null, "/")]
    [InlineData("", "/")]
    [InlineData("https://evil.example/", "/")]
    [InlineData("//evil.example", "/")]
    [InlineData("/\\evil.example", "/")]
    [InlineData("/ok\r\nSet-Cookie: x", "/")]
    [InlineData("account", "/")]
    public void Return_urls_are_restricted_to_local_paths(string? input, string expected) =>
        Assert.Equal(expected, LocalReturnUrl.Normalize(input));

    [Theory]
    [InlineData("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/140.0 Safari/537.36 Edg/140.0", "Edge on Windows")]
    [InlineData("Mozilla/5.0 (X11; Linux x86_64; rv:140.0) Gecko/20100101 Firefox/140.0", "Firefox on Linux")]
    [InlineData("Mozilla/5.0 (iPhone; CPU iPhone OS 18_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/18.0 Mobile/15E148 Safari/604.1", "Safari on iOS")]
    [InlineData(null, "Unknown browser")]
    public void Client_description_is_coarse(string? userAgent, string expected) =>
        Assert.Equal(expected, ClientDescription.FromUserAgent(userAgent));

    [Theory]
    [InlineData("AspNetUsers", "asp_net_users")]
    [InlineData("NormalizedEmail", "normalized_email")]
    [InlineData("PK_AspNetUserRoles", "pk_asp_net_user_roles")]
    [InlineData("default_language", "default_language")]
    [InlineData("UserNameIndex", "user_name_index")]
    public void Database_names_are_snake_case(string input, string expected) =>
        Assert.Equal(expected, ChatXDbContext.ToSnakeCase(input));

    [Fact]
    public void Emailed_tokens_round_trip_through_url_safe_encoding()
    {
        const string token = "CfDJ8+abc/def==";
        var encoded = AccountService.EncodeToken(token);

        Assert.DoesNotContain('+', encoded);
        Assert.DoesNotContain('/', encoded);
        Assert.Equal(token, AccountService.DecodeToken(encoded));
        Assert.Null(AccountService.DecodeToken("not base64 !!"));
    }

    [Fact]
    public void Public_url_settings_build_escaped_links_and_reject_unsafe_values()
    {
        var settings = new PublicUrlSettings { PublicBaseUrl = "https://chat.example.org/app" };

        Assert.Empty(settings.Validate());
        Assert.Equal("https://chat.example.org/app/reset-password?user=1&token=a%2Bb", settings.Link("reset-password", ("user", "1"), ("token", "a+b")).AbsoluteUri);
        Assert.NotEmpty(new PublicUrlSettings { PublicBaseUrl = "" }.Validate());
        Assert.NotEmpty(new PublicUrlSettings { PublicBaseUrl = "javascript:alert(1)" }.Validate());
        Assert.NotEmpty(new PublicUrlSettings { PublicBaseUrl = "https://chat.example.org/?x=1" }.Validate());
    }
}
