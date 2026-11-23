using Audorix.ChatX.Application.Abstractions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Audorix.ChatX.IntegrationTests.Support;

/// <summary>
/// In-process API host with explicit configuration. Defaults to the Production environment so
/// tests observe production error and cookie behaviour (clients use https://localhost). Email
/// and captcha ports are replaced by in-memory fakes; everything else is the real stack.
/// Adds one test-only route, <c>/__test/throw</c>, after the real pipeline.
/// </summary>
public sealed class ChatXApiFactory(IReadOnlyDictionary<string, string?> settings, string environment = "Production")
    : WebApplicationFactory<Program>
{
    public const string ThrowPath = "/__test/throw";
    public const string ThrownMessage = "Sensitive internal detail that must not reach clients";

    public static readonly IReadOnlyDictionary<string, string?> Defaults = new Dictionary<string, string?>
    {
        ["App:PublicBaseUrl"] = "https://chat.test",
        ["Captcha:Provider"] = "None",
        ["RateLimiting:AuthPermitLimit"] = "10000",
        // Tests configure everything explicitly: normal mode, and never a developer's settings file.
        ["Setup:Completed"] = "true",
        ["Setup:SettingsPath"] = Path.Combine(Path.GetTempPath(), "chatx-tests", "no-settings-file.json"),
    };

    /// <summary>Local attachment storage of this host: a fresh temporary directory, removed on dispose (unless the test set <c>Storage:Local:Path</c>).</summary>
    public string StorageRoot { get; } = Path.Combine(Path.GetTempPath(), "chatx-tests", "storage-" + Guid.NewGuid().ToString("N"));

    public FakeEmailSender Emails { get; } = new();

    public FakeCaptchaVerifier Captcha { get; } = new();

    /// <summary>Additional test service registrations (for example a conversation-access fixture).</summary>
    public Action<IServiceCollection>? ExtraServices { get; init; }

    public HttpClient CreateHttpsClient() =>
        CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(environment);
        // A null value suppresses the default without setting anything, so lower sources
        // (such as a settings file) can supply the key.
        foreach (var (key, value) in Defaults.Where(d => !settings.ContainsKey(d.Key)).Concat(settings).Where(s => s.Value is not null))
        {
            builder.UseSetting(key, value);
        }

        // Never write attachments into the API project's App_Data.
        if (!settings.ContainsKey("Storage:Local:Path"))
        {
            builder.UseSetting("Storage:Local:Path", StorageRoot);
        }

        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<IStartupFilter, ThrowingRouteStartupFilter>();
            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender>(Emails);
            services.RemoveAll<ICaptchaVerifier>();
            services.AddSingleton<ICaptchaVerifier>(Captcha);
            ExtraServices?.Invoke(services);
        });
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        try
        {
            if (Directory.Exists(StorageRoot))
            {
                Directory.Delete(StorageRoot, recursive: true);
            }
        }
        catch (IOException)
        {
        }
    }

    private sealed class ThrowingRouteStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            next(app);
            app.Use((context, nextMiddleware) =>
                context.Request.Path == ThrowPath
                    ? throw new InvalidOperationException(ThrownMessage)
                    : nextMiddleware(context));
        };
    }
}
