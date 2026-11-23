using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;

namespace Audorix.ChatX.LoadTest;

/// <summary>
/// One simulated person: their own cookies and antiforgery token, sent over a connection pool shared
/// by all people (so the client machine does not open a pool per person), plus a hub connection.
/// </summary>
internal sealed class ApiUser(HttpClient http, string email, string displayName)
{
    private readonly Dictionary<string, string> _cookies = new(StringComparer.Ordinal);
    private readonly Lock _gate = new();
    private string? _csrf;

    public string Email { get; } = email;
    public string DisplayName { get; } = displayName;
    public Guid Id { get; private set; }
    public HubConnection? Hub { get; private set; }

    public async Task SignUpAndInAsync(string password, CancellationToken ct)
    {
        await RefreshCsrfAsync(ct);
        using var register = await SendAsync(HttpMethod.Post, "/api/v1/auth/register", new { email = Email, password, displayName = DisplayName, captchaToken = "load-test" }, ct);
        await EnsureAsync(register, "register", HttpStatusCode.Accepted, HttpStatusCode.OK, HttpStatusCode.Created);
        await RefreshCsrfAsync(ct);
        using var login = await SendAsync(HttpMethod.Post, "/api/v1/auth/login", new { email = Email, password, rememberMe = false }, ct);
        await EnsureAsync(login, "sign in", HttpStatusCode.OK);
        await RefreshCsrfAsync(ct); // the token is bound to the signed-in identity
        using var me = await GetJsonAsync("/api/v1/users/me", ct);
        Id = me.RootElement.GetProperty("id").GetGuid();
    }

    public async Task ConnectAsync(Uri server, Action<JsonElement> onMessage, CancellationToken ct)
    {
        var hub = new HubConnectionBuilder()
            .WithUrl(new Uri(server, "/hubs/chat"), options =>
            {
                options.Transports = HttpTransportType.WebSockets;
                options.SkipNegotiation = true;
                options.Headers["Cookie"] = CookieHeader();
            })
            .Build();
        hub.On<JsonElement>("MessageCreated", onMessage);
        await hub.StartAsync(ct);
        Hub = hub;
    }

    public async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        if (_csrf is not null)
        {
            request.Headers.Add("X-CSRF-TOKEN", _csrf);
        }

        if (CookieHeader() is { Length: > 0 } cookies)
        {
            request.Headers.Add("Cookie", cookies);
        }

        var response = await http.SendAsync(request, ct);
        Remember(response);
        return response;
    }

    public async Task<JsonDocument> GetJsonAsync(string path, CancellationToken ct)
    {
        using var response = await SendAsync(HttpMethod.Get, path, null, ct);
        await EnsureAsync(response, $"GET {path}", HttpStatusCode.OK);
        return await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
    }

    public async Task<JsonDocument> PostJsonAsync(string path, object? body, CancellationToken ct, params HttpStatusCode[] expected)
    {
        using var response = await SendAsync(HttpMethod.Post, path, body, ct);
        await EnsureAsync(response, $"POST {path}", expected.Length == 0 ? [HttpStatusCode.OK, HttpStatusCode.Created] : expected);
        return await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
    }

    private async Task RefreshCsrfAsync(CancellationToken ct)
    {
        using var token = await GetJsonAsync("/api/v1/auth/csrf", ct);
        _csrf = token.RootElement.GetProperty("token").GetString();
    }

    private string CookieHeader()
    {
        lock (_gate)
        {
            return string.Join("; ", _cookies.Select(c => $"{c.Key}={c.Value}"));
        }
    }

    private void Remember(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var values))
        {
            return;
        }

        lock (_gate)
        {
            foreach (var pair in values.Select(v => v.Split(';', 2)[0]))
            {
                var at = pair.IndexOf('=', StringComparison.Ordinal);
                if (at <= 0)
                {
                    continue;
                }

                var (name, value) = (pair[..at], pair[(at + 1)..]);
                if (value.Length == 0)
                {
                    _cookies.Remove(name);
                }
                else
                {
                    _cookies[name] = value;
                }
            }
        }
    }

    private static async Task EnsureAsync(HttpResponseMessage response, string what, params HttpStatusCode[] expected)
    {
        if (!expected.Contains(response.StatusCode))
        {
            var text = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException($"{what} returned {(int)response.StatusCode}: {text[..Math.Min(text.Length, 300)]}");
        }
    }
}
