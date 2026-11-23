using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text.Json;
using Audorix.ChatX.LoadTest;

// Phase 22 load test: creates people, one organization and group conversations through the public
// API, connects everyone to the hub, sends messages and reads history at fixed rates, then compares
// the measurements with the targets. See docs/guides/load-testing.md.

LoadTestOptions options;
try
{
    options = args.Contains("--help") ? throw new ArgumentException(string.Empty) : LoadTestOptions.Parse(args);
}
catch (Exception ex) when (ex is ArgumentException or FormatException)
{
    if (ex.Message.Length > 0)
    {
        Console.Error.WriteLine(ex.Message);
    }

    Console.WriteLine(LoadTestOptions.Usage);
    return 1;
}

using var cancel = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cancel.Cancel();
};

try
{
    return await new LoadRun(options).RunAsync(cancel.Token);
}
catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException or OperationCanceledException)
{
    Console.Error.WriteLine($"The load test stopped: {ex.Message}");
    return 1;
}

internal sealed class LoadRun(LoadTestOptions options)
{
    private static readonly JsonSerializerOptions JsonOutput = new()
    {
        WriteIndented = true,
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals,
    };

    private readonly string _run = DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
    private readonly Series _send = new("send");
    private readonly Series _history = new("history");
    private readonly Series _delivery = new("delivery");
    private readonly ConcurrentDictionary<string, Pending> _pending = new(StringComparer.Ordinal);
    private int _hubClosed;
    private int _inFlight;
    private long _sentMeasured;

    private sealed class Pending(long started, int recipients, bool measured)
    {
        public long Started { get; } = started;
        public int Recipients { get; } = recipients;
        public bool Measured { get; } = measured;
        public int Received;
    }

    private sealed record Group(Guid Id, ApiUser[] Members);

    public async Task<int> RunAsync(CancellationToken ct)
    {
        using var handler = new SocketsHttpHandler
        {
            UseCookies = false,
            MaxConnectionsPerServer = 512,
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
        };
        using var http = new HttpClient(handler) { BaseAddress = options.Url, Timeout = TimeSpan.FromSeconds(60) };

        await CheckServerAsync(http, ct);
        var users = await TimedAsync("Creating people", () => CreateUsersAsync(http, ct));
        var groups = await TimedAsync("Creating the organization and groups", () => CreateGroupsAsync(users, ct));
        await TimedAsync("Connecting to the hub", () => ConnectAsync(users, ct));

        Console.WriteLine($"Running: {options.MessagesPerSecond} messages/s and {options.HistoryPerSecond} history pages/s, " +
            $"{options.Warmup.TotalSeconds:0} s warm-up then {options.Duration.TotalSeconds:0} s measured.");
        var connectedAtStart = users.Count(u => u.Hub?.State == Microsoft.AspNetCore.SignalR.Client.HubConnectionState.Connected);
        await DriveAsync(groups, ct);

        // Late deliveries still count; then anything missing is an error.
        await Task.Delay(TimeSpan.FromSeconds(5), ct);
        var missing = _pending.Values.Where(p => p.Measured).Sum(p => Math.Max(0, p.Recipients - p.Received));
        var connectedAtEnd = users.Count(u => u.Hub?.State == Microsoft.AspNetCore.SignalR.Client.HubConnectionState.Connected);
        var hubClosed = Volatile.Read(ref _hubClosed); // closing the connections below is not a failure

        foreach (var user in users)
        {
            if (user.Hub is { } hub)
            {
                await hub.DisposeAsync();
            }
        }

        return Report(connectedAtStart, connectedAtEnd, missing, hubClosed);
    }

    private async Task CheckServerAsync(HttpClient http, CancellationToken ct)
    {
        using var response = await http.GetAsync(new Uri("/api/v1/auth/config", UriKind.Relative), ct);
        if (response.StatusCode != HttpStatusCode.OK)
        {
            throw new InvalidOperationException($"{options.Url} did not answer /api/v1/auth/config ({(int)response.StatusCode}). Is the server running?");
        }

        using var config = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        var root = config.RootElement;
        var autoConfirm = root.GetProperty("autoConfirmEmail").GetBoolean();
        var captcha = root.GetProperty("captcha").GetProperty("provider").GetString();
        if (!autoConfirm || !string.Equals(captcha, "none", StringComparison.OrdinalIgnoreCase) || !root.GetProperty("registrationEnabled").GetBoolean())
        {
            throw new InvalidOperationException(
                "the server must be a test installation with Identity:AutoConfirmEmail=true, Captcha:Provider=None and registration on " +
                "(and RateLimiting:AuthPermitLimit raised for the setup).");
        }

        Console.WriteLine($"Server {options.Url}: development mode, ready. Run id {_run}.");
    }

    private async Task<ApiUser[]> CreateUsersAsync(HttpClient http, CancellationToken ct)
    {
        var users = Enumerable.Range(0, options.Users)
            .Select(i => new ApiUser(http, $"lt{_run}-{i}@loadtest.example.org", $"Load {i}"))
            .ToArray();
        var done = 0;
        await Parallel.ForEachAsync(users, new ParallelOptions { MaxDegreeOfParallelism = options.SetupConcurrency, CancellationToken = ct }, async (user, token) =>
        {
            await user.SignUpAndInAsync(options.Password, token);
            Progress(Interlocked.Increment(ref done), users.Length);
        });
        return users;
    }

    private async Task<Group[]> CreateGroupsAsync(ApiUser[] users, CancellationToken ct)
    {
        // Organizations of --organization-size people (one by default); groups never cross them.
        var organizations = Split(users, options.OrganizationSize ?? users.Length, options.GroupSize);
        var plan = new List<(Guid OrganizationId, ApiUser[] Members)>();
        foreach (var (members, n) in organizations.Select((m, n) => (m, n)))
        {
            using var organization = await members[0].PostJsonAsync("/api/v1/organizations", new { name = $"Load test {_run} {n}" }, ct);
            var organizationId = organization.RootElement.GetProperty("id").GetGuid();
            plan.AddRange(Split(members, options.GroupSize, 2).Select(g => (organizationId, g)));
            _organizationOwners[organizationId] = members[0];
            foreach (var member in members.Skip(1))
            {
                _joining.Add((organizationId, member));
            }
        }

        var joined = 0;
        await Parallel.ForEachAsync(_joining, new ParallelOptions { MaxDegreeOfParallelism = options.SetupConcurrency, CancellationToken = ct }, async (item, token) =>
        {
            var (organizationId, user) = item;
            Guid invitationId;
            using (var invitation = await _organizationOwners[organizationId].PostJsonAsync(
                $"/api/v1/organizations/{organizationId}/invitations", new { email = user.Email, role = "member" }, token))
            {
                invitationId = invitation.RootElement.GetProperty("id").GetGuid();
            }

            using var _ = await user.PostJsonAsync($"/api/v1/organizations/invitations/{invitationId}/accept", null, token, HttpStatusCode.OK);
            Progress(Interlocked.Increment(ref joined), _joining.Count);
        });
        Console.WriteLine($"  {organizations.Count} organization(s), {plan.Count} groups");

        var groups = new Group[plan.Count];
        await Parallel.ForEachAsync(Enumerable.Range(0, plan.Count), new ParallelOptions { MaxDegreeOfParallelism = options.SetupConcurrency, CancellationToken = ct }, async (i, token) =>
        {
            var (organizationId, members) = plan[i];
            using var created = await members[0].PostJsonAsync("/api/v1/conversations/groups",
                new { title = $"Load group {i}", userIds = members.Skip(1).Select(m => m.Id).ToArray(), organizationId }, token, HttpStatusCode.Created);
            groups[i] = new Group(created.RootElement.GetProperty("id").GetGuid(), members);
        });
        return groups;
    }

    private readonly Dictionary<Guid, ApiUser> _organizationOwners = [];
    private readonly List<(Guid OrganizationId, ApiUser User)> _joining = [];

    /// <summary>Chunks of <paramref name="size"/>; a last chunk smaller than <paramref name="minimum"/> joins the one before.</summary>
    private static List<ApiUser[]> Split(ApiUser[] users, int size, int minimum)
    {
        var chunks = users.Chunk(size).ToList();
        if (chunks.Count > 1 && chunks[^1].Length < minimum)
        {
            chunks[^2] = [.. chunks[^2], .. chunks[^1]];
            chunks.RemoveAt(chunks.Count - 1);
        }

        return chunks;
    }

    private async Task ConnectAsync(ApiUser[] users, CancellationToken ct)
    {
        var connected = 0;
        await Parallel.ForEachAsync(users, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(options.SetupConcurrency, 32), CancellationToken = ct }, async (user, token) =>
        {
            await user.ConnectAsync(options.Url, message => OnMessage(user, message), token);
            user.Hub!.Closed += _ =>
            {
                Interlocked.Increment(ref _hubClosed);
                return Task.CompletedTask;
            };
            Progress(Interlocked.Increment(ref connected), users.Length);
        });
    }

    private void OnMessage(ApiUser receiver, JsonElement message)
    {
        if (!message.TryGetProperty("entry", out var entry)
            || !entry.TryGetProperty("clientMessageId", out var id) || id.GetString() is not { } clientMessageId
            || !_pending.TryGetValue(clientMessageId, out var pending))
        {
            return;
        }

        // The sender's own copy is not a delivery.
        if (entry.TryGetProperty("actor", out var actor) && actor.ValueKind == JsonValueKind.Object && actor.GetProperty("id").GetGuid() == receiver.Id)
        {
            return;
        }

        Interlocked.Increment(ref pending.Received);
        if (pending.Measured)
        {
            _delivery.Add(pending.Started);
        }
    }

    private async Task DriveAsync(Group[] groups, CancellationToken ct)
    {
        var total = options.Warmup + options.Duration;
        var started = Stopwatch.GetTimestamp();
        long messages = 0, histories = 0;
        var tasks = new ConcurrentBag<Task>();
        var lastReport = TimeSpan.Zero;
        while (Stopwatch.GetElapsedTime(started) is var elapsed && elapsed < total)
        {
            ct.ThrowIfCancellationRequested();
            var measuring = elapsed >= options.Warmup;
            _send.Recording = _history.Recording = _delivery.Recording = measuring;

            for (var due = (long)(elapsed.TotalSeconds * options.MessagesPerSecond); messages < due; messages++)
            {
                tasks.Add(SendOneAsync(groups[Random.Shared.Next(groups.Length)], measuring, ct));
            }

            for (var due = (long)(elapsed.TotalSeconds * options.HistoryPerSecond); histories < due; histories++)
            {
                tasks.Add(ReadHistoryAsync(groups[Random.Shared.Next(groups.Length)], ct));
            }

            if (elapsed - lastReport >= TimeSpan.FromSeconds(10))
            {
                lastReport = elapsed;
                Console.WriteLine($"  {elapsed.TotalSeconds,5:0} s: {messages} messages, {histories} history pages, {Volatile.Read(ref _inFlight)} requests open");
            }

            await Task.Delay(5, ct);
        }

        _send.Recording = _history.Recording = false;
        await Task.WhenAll(tasks);
        _delivery.Recording = true; // late deliveries of measured messages still count
    }

    private async Task SendOneAsync(Group group, bool measured, CancellationToken ct)
    {
        if (Interlocked.Increment(ref _inFlight) > 5000)
        {
            // The server is not keeping up; do not let the client pile up unbounded work.
            Interlocked.Decrement(ref _inFlight);
            _send.Fail("client backlog over 5000 requests");
            return;
        }

        var sender = group.Members[Random.Shared.Next(group.Members.Length)];
        var clientMessageId = $"lt-{Guid.NewGuid():N}";
        var start = Stopwatch.GetTimestamp();
        _pending[clientMessageId] = new Pending(start, group.Members.Length - 1, measured);
        try
        {
            using var response = await sender.SendAsync(HttpMethod.Post, $"/api/v1/conversations/{group.Id}/messages",
                new { clientMessageId, body = $"Load test message {clientMessageId[3..11]}" }, ct);
            if (response.StatusCode == HttpStatusCode.Created)
            {
                _send.Add(start);
                if (measured)
                {
                    Interlocked.Increment(ref _sentMeasured);
                }
            }
            else
            {
                _pending.TryRemove(clientMessageId, out _);
                _send.Fail($"HTTP {(int)response.StatusCode}");
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _pending.TryRemove(clientMessageId, out _);
            _send.Fail(ex.GetType().Name);
        }
        finally
        {
            Interlocked.Decrement(ref _inFlight);
        }
    }

    private async Task ReadHistoryAsync(Group group, CancellationToken ct)
    {
        var reader = group.Members[Random.Shared.Next(group.Members.Length)];
        var start = Stopwatch.GetTimestamp();
        Interlocked.Increment(ref _inFlight);
        try
        {
            using var response = await reader.SendAsync(HttpMethod.Get, $"/api/v1/conversations/{group.Id}/history?limit=50", null, ct);
            await response.Content.LoadIntoBufferAsync(ct);
            if (response.StatusCode == HttpStatusCode.OK)
            {
                _history.Add(start);
            }
            else
            {
                _history.Fail($"HTTP {(int)response.StatusCode}");
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _history.Fail(ex.GetType().Name);
        }
        finally
        {
            Interlocked.Decrement(ref _inFlight);
        }
    }

    private int Report(int connectedAtStart, int connectedAtEnd, int missingDeliveries, int hubClosed)
    {
        var send = _send.Result();
        var history = _history.Result();
        var delivery = _delivery.Result();
        var achieved = _sentMeasured / options.Duration.TotalSeconds;
        static string Ms(double v) => double.IsNaN(v) ? "-" : v.ToString("0", CultureInfo.InvariantCulture);

        Console.WriteLine();
        Console.WriteLine($"{"",-10}{"count",9}{"errors",8}{"p50 ms",9}{"p95 ms",9}{"p99 ms",9}{"max ms",9}");
        foreach (var s in new[] { send, history, delivery })
        {
            Console.WriteLine($"{s.Name,-10}{s.Count,9}{s.Errors,8}{Ms(s.P50),9}{Ms(s.P95),9}{Ms(s.P99),9}{Ms(s.Max),9}");
            foreach (var (reason, count) in s.ErrorReasons)
            {
                Console.WriteLine($"{"",12}{count} x {reason}");
            }
        }

        var errors = send.Errors + history.Errors + missingDeliveries + hubClosed;
        var targets = new List<TargetResult>
        {
            new("connected people", $"{options.Users}", $"{connectedAtStart} at start, {connectedAtEnd} at end", connectedAtStart == options.Users && connectedAtEnd == options.Users),
            new("message rate", $"{options.MessagesPerSecond:0.#}/s", $"{achieved:0.#}/s", achieved >= options.MessagesPerSecond * 0.95),
            new("send p95", $"< {options.SendP95Ms:0} ms", $"{Ms(send.P95)} ms", send.P95 < options.SendP95Ms),
            new("history p95", $"< {options.HistoryP95Ms:0} ms", $"{Ms(history.P95)} ms", history.P95 < options.HistoryP95Ms),
            new("delivery p95", $"< {options.DeliveryP95Ms:0} ms", $"{Ms(delivery.P95)} ms", delivery.P95 < options.DeliveryP95Ms),
            new("errors", "0", $"{errors} (missing deliveries {missingDeliveries}, hub disconnects {hubClosed})", errors == 0),
        };

        Console.WriteLine();
        foreach (var t in targets)
        {
            Console.WriteLine($"{(t.Met ? "PASS" : "MISS")}  {t.Name,-17} target {t.Target,-10} measured {t.Measured}");
        }

        if (options.Output is { } path)
        {
            var result = new
            {
                run = _run,
                server = options.Url,
                options = new { options.Users, options.GroupSize, options.MessagesPerSecond, options.HistoryPerSecond, durationSeconds = options.Duration.TotalSeconds },
                series = new[] { send, history, delivery },
                targets,
            };
            File.WriteAllText(path, JsonSerializer.Serialize(result, JsonOutput));
            Console.WriteLine($"Results written to {path}");
        }

        return targets.TrueForAll(t => t.Met) ? 0 : 2;
    }

    private static async Task<T> TimedAsync<T>(string what, Func<Task<T>> step)
    {
        Console.WriteLine($"{what}...");
        var start = Stopwatch.GetTimestamp();
        var result = await step();
        Console.WriteLine($"  done in {Stopwatch.GetElapsedTime(start).TotalSeconds:0.0} s");
        return result;
    }

    private static async Task TimedAsync(string what, Func<Task> step) => await TimedAsync(what, async () =>
    {
        await step();
        return true;
    });

    private static void Progress(int done, int total)
    {
        if (done % 200 == 0 || done == total)
        {
            Console.WriteLine($"  {done}/{total}");
        }
    }
}
