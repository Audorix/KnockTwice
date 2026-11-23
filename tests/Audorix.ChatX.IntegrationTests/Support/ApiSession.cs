using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Audorix.ChatX.Contracts.V1.Auth;

namespace Audorix.ChatX.IntegrationTests.Support;

/// <summary>
/// A browser-like client: keeps cookies and sends the antiforgery token on state-changing
/// requests, refreshing it after sign-in state changes exactly as the web app does.
/// </summary>
public sealed class ApiSession(HttpClient client) : IDisposable
{
    private string? _csrf;

    public HttpClient Client { get; } = client;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async Task RefreshCsrfAsync()
    {
        var token = await Client.GetFromJsonAsync<CsrfTokenResponse>("/api/v1/auth/csrf", Ct);
        _csrf = token!.Token;
    }

    public async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? body = null, bool withCsrf = true)
    {
        if (withCsrf && _csrf is null)
        {
            await RefreshCsrfAsync();
        }

        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        if (withCsrf)
        {
            request.Headers.Add("X-CSRF-TOKEN", _csrf);
        }

        return await Client.SendAsync(request, Ct);
    }

    public Task<HttpResponseMessage> PostAsync(string path, object? body = null, bool withCsrf = true) => SendAsync(HttpMethod.Post, path, body, withCsrf);

    /// <summary>Uploads one file as <c>multipart/form-data</c> in the field <c>file</c>, like a browser form.</summary>
    public async Task<HttpResponseMessage> UploadAsync(string path, string fileName, byte[] content, string? contentType = "application/octet-stream")
    {
        if (_csrf is null)
        {
            await RefreshCsrfAsync();
        }

        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(content);
        if (contentType is not null)
        {
            file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        }

        form.Add(file, "file", fileName);
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = form };
        request.Headers.Add("X-CSRF-TOKEN", _csrf);
        return await Client.SendAsync(request, Ct);
    }

    public async Task<HttpResponseMessage> LoginAsync(string email, string password, bool rememberMe = false)
    {
        await RefreshCsrfAsync();
        var response = await PostAsync("/api/v1/auth/login", new { email, password, rememberMe });
        if (response.IsSuccessStatusCode)
        {
            // The antiforgery token is bound to the user; get one for the signed-in identity.
            await RefreshCsrfAsync();
        }

        return response;
    }

    public static async Task<string?> ErrorCodeAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync(Ct);
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        using var doc = JsonDocument.Parse(text);
        return doc.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    public static async Task<JsonElement> JsonAsync(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        return doc.RootElement.Clone();
    }

    public static void AssertStatus(HttpStatusCode expected, HttpResponseMessage response, string? body = null) =>
        Assert.True(response.StatusCode == expected, $"Expected {expected}, got {response.StatusCode}. {body}");

    public void Dispose() => Client.Dispose();
}
