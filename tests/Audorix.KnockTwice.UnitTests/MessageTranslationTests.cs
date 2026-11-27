using Audorix.KnockTwice.Application.Common;
using Audorix.KnockTwice.Application.Translation;
using Audorix.KnockTwice.Domain.Authorization;

namespace Audorix.KnockTwice.UnitTests;

public sealed class MessageTranslationTests
{
    [Fact]
    public async Task Translate_forwards_text_and_validated_target_language_to_provider()
    {
        var provider = new RecordingTranslationProvider();
        var service = new MessageTranslationService(provider);

        var result = await service.TranslateAsync("Hola", " en ", CancellationToken.None);

        Assert.Equal("translated", result);
        Assert.Equal("Hola", provider.Text);
        Assert.Equal("en", provider.TargetLanguage);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Translate_rejects_empty_message(string? text)
    {
        var service = new MessageTranslationService(new RecordingTranslationProvider());

        var exception = await Assert.ThrowsAsync<RequestValidationException>(() =>
            service.TranslateAsync(text, "en", CancellationToken.None));

        Assert.Equal("translation_text_required", exception.Code);
    }

    [Fact]
    public async Task Translate_rejects_overlong_message_before_provider_call()
    {
        var provider = new RecordingTranslationProvider();
        var service = new MessageTranslationService(provider);

        var exception = await Assert.ThrowsAsync<RequestValidationException>(() =>
            service.TranslateAsync(new string('x', 4001), "en", CancellationToken.None));

        Assert.Equal("translation_text_too_long", exception.Code);
        Assert.Null(provider.Text);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("English")]
    [InlineData("en_US")]
    [InlineData("en--US")]
    public async Task Translate_rejects_invalid_language_tags(string? language)
    {
        var provider = new RecordingTranslationProvider();
        var service = new MessageTranslationService(provider);

        var exception = await Assert.ThrowsAsync<RequestValidationException>(() =>
            service.TranslateAsync("Hello", language, CancellationToken.None));

        Assert.Equal("language_invalid", exception.Code);
        Assert.Null(provider.Text);
    }

    [Fact]
    public void Translation_permission_is_available_for_group_roles()
    {
        Assert.Contains(PermissionCatalog.Available, permission => permission.Key == Permissions.MessagesTranslate);
        Assert.Contains(Permissions.All, permission => permission == Permissions.MessagesTranslate);
    }

    private sealed class RecordingTranslationProvider : IMessageTranslationProvider
    {
        public string? Text { get; private set; }
        public string? TargetLanguage { get; private set; }

        public Task<string> TranslateAsync(string text, string targetLanguage, CancellationToken cancellationToken)
        {
            Text = text;
            TargetLanguage = targetLanguage;
            return Task.FromResult("translated");
        }
    }
}
