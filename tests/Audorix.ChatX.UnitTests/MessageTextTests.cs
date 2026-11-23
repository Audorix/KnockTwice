using Audorix.ChatX.Application.Common;

namespace Audorix.ChatX.UnitTests;

/// <summary>Phase 19: Markdown-style text shown as plain text in email, push and reply previews.</summary>
public sealed class MessageTextTests
{
    [Theory]
    [InlineData("**Bold** and *italic* and _also_", "Bold and italic and also")]
    [InlineData("~~gone~~ `x = 1`", "gone x = 1")]
    [InlineData("> quoted\nreply", "quoted\nreply")]
    [InlineData("See [the docs](https://example.org/docs)", "See the docs (https://example.org/docs)")]
    [InlineData("[https://example.org](https://example.org)", "https://example.org")]
    [InlineData("```js\nconst a = **1**;\n```", "const a = **1**;")]
    [InlineData("- one\n- two", "- one\n- two")]
    [InlineData("2 * 3 * 4 and snake_case_name", "2 * 3 * 4 and snake_case_name")]
    [InlineData("[click](javascript:alert(1))", "[click](javascript:alert(1))")]
    [InlineData("", "")]
    public void Plain_text_drops_markers_only(string text, string plain) => Assert.Equal(plain, MessageText.ToPlain(text));

    [Fact]
    public void Excerpts_are_one_line_and_bounded()
    {
        Assert.Equal("Hello world", MessageText.Excerpt("**Hello**\n\n  world"));
        Assert.Null(MessageText.Excerpt("   "));
        var excerpt = MessageText.Excerpt(new string('a', 200));
        Assert.Equal(141, excerpt!.Length);
        Assert.EndsWith("…", excerpt, StringComparison.Ordinal);
    }
}
