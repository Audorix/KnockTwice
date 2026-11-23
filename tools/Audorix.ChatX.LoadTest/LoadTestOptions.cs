using System.Globalization;

namespace Audorix.ChatX.LoadTest;

/// <summary>
/// What to run and what counts as a pass. The defaults are the Phase 22 targets for one server:
/// 2,000 connected people, 100 messages a second, p95 send under 300 ms and p95 history page under 200 ms, no errors.
/// </summary>
internal sealed record LoadTestOptions
{
    public Uri Url { get; init; } = new("http://localhost:5080");
    public int Users { get; init; } = 2000;
    public int GroupSize { get; init; } = 50;
    public int? OrganizationSize { get; init; }
    public double MessagesPerSecond { get; init; } = 100;
    public double HistoryPerSecond { get; init; } = 10;
    public TimeSpan Duration { get; init; } = TimeSpan.FromSeconds(120);
    public TimeSpan Warmup { get; init; } = TimeSpan.FromSeconds(10);
    public int SetupConcurrency { get; init; } = 16;
    public double SendP95Ms { get; init; } = 300;
    public double HistoryP95Ms { get; init; } = 200;
    public double DeliveryP95Ms { get; init; } = 1000;
    public string Password { get; init; } = "load-test-password-1";
    public string? Output { get; init; }

    public const string Usage = """
        Audorix.ChatX load test (Phase 22)

          dotnet run -c Release --project tools/Audorix.ChatX.LoadTest -- [options]

        The installation must be a test installation in development mode: Identity:AutoConfirmEmail=true,
        Captcha:Provider=None and RateLimiting:AuthPermitLimit high enough for the setup (e.g. 100000).
        The tool creates its own users, organization and groups; never point it at a production server.

          --url <url>               server address (default http://localhost:5080)
          --users <n>               connected people (default 2000)
          --group-size <n>          people per group conversation (default 50)
          --organization-size <n>   people per organization (default: everyone in one). Presence
                                    goes to everyone sharing an organization, so this sets its cost
          --rate <n>                messages per second (default 100)
          --history-rate <n>        history pages per second (default 10)
          --duration <seconds>      measured run (default 120)
          --warmup <seconds>        unmeasured run first (default 10)
          --setup-concurrency <n>   parallel setup requests (default 16)
          --send-p95 <ms>           target p95 for sending (default 300)
          --history-p95 <ms>        target p95 for a history page (default 200)
          --delivery-p95 <ms>       target p95 from send to arrival at each recipient (default 1000)
          --out <file>              also write the results as JSON

        Exit code: 0 all targets met, 2 a target missed, 1 the run could not be completed.
        """;

    public static LoadTestOptions Parse(string[] args)
    {
        var options = new LoadTestOptions();
        for (var i = 0; i < args.Length; i++)
        {
            string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"{args[i]} needs a value.");
            int Int() => int.Parse(Next(), CultureInfo.InvariantCulture);
            double Number() => double.Parse(Next(), CultureInfo.InvariantCulture);
            options = args[i] switch
            {
                "--url" => options with { Url = new Uri(Next()) },
                "--users" => options with { Users = Int() },
                "--group-size" => options with { GroupSize = Int() },
                "--organization-size" => options with { OrganizationSize = Int() },
                "--rate" => options with { MessagesPerSecond = Number() },
                "--history-rate" => options with { HistoryPerSecond = Number() },
                "--duration" => options with { Duration = TimeSpan.FromSeconds(Number()) },
                "--warmup" => options with { Warmup = TimeSpan.FromSeconds(Number()) },
                "--setup-concurrency" => options with { SetupConcurrency = Int() },
                "--send-p95" => options with { SendP95Ms = Number() },
                "--history-p95" => options with { HistoryP95Ms = Number() },
                "--delivery-p95" => options with { DeliveryP95Ms = Number() },
                "--out" => options with { Output = Next() },
                _ => throw new ArgumentException($"Unknown option {args[i]}."),
            };
        }

        if (options.Users < 2 || options.GroupSize < 2 || options.GroupSize > options.Users)
        {
            throw new ArgumentException("--users must be at least 2 and --group-size between 2 and --users.");
        }

        if (options.OrganizationSize is { } size && (size < options.GroupSize || size > options.Users))
        {
            throw new ArgumentException("--organization-size must be between --group-size and --users.");
        }

        return options;
    }
}
