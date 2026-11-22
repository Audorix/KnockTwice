using System.Diagnostics;
using Audorix.ChatX.IntegrationTests.Support;

namespace Audorix.ChatX.IntegrationTests;

/// <summary>
/// Runs the real API executable as an operator would, verifying exit codes and console output.
/// </summary>
public sealed class CommandLineTests
{
    private const string Secret = "Cli-Secret-Pa55";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("MySql", "Database:Provider 'MySql' is not supported. Supported values: Sqlite, PostgreSql.")]
    [InlineData("", "Database:Provider is required. Supported values: Sqlite, PostgreSql.")]
    public async Task Invalid_provider_exits_with_configuration_error_and_no_secret_in_output(string provider, string expected)
    {
        foreach (var args in new[] { Array.Empty<string>(), ["migrate"] })
        {
            var (exitCode, output) = await RunApiAsync(args, new()
            {
                ["Database__Provider"] = provider,
                ["Database__ConnectionString"] = "Host=db;Database=chatx;Password=" + Secret,
            });

            Assert.Equal(2, exitCode);
            Assert.Contains(expected, output, StringComparison.Ordinal);
            Assert.DoesNotContain(Secret, output, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("Sqlite")]
    [InlineData("PostgreSql")]
    public async Task Migrate_command_is_repeatable(string provider)
    {
        await using var database = await TestDatabase.CreateAsync(provider);
        var environment = new Dictionary<string, string?>
        {
            ["Database__Provider"] = provider,
            ["Database__ConnectionString"] = database.ConnectionString,
        };

        var first = await RunApiAsync(["migrate"], environment);
        var second = await RunApiAsync(["migrate"], environment);

        Assert.True(first.ExitCode == 0, first.Output);
        Assert.Contains("installation initialized", first.Output, StringComparison.Ordinal);
        Assert.True(second.ExitCode == 0, second.Output);
        Assert.Contains("0 migration(s) applied; installation unchanged", second.Output, StringComparison.Ordinal);
    }

    private static async Task<(int ExitCode, string Output)> RunApiAsync(string[] args, Dictionary<string, string?> environment)
    {
        var apiAssembly = typeof(Program).Assembly.Location;
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = Path.GetDirectoryName(apiAssembly)!,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add(apiAssembly);
        foreach (var arg in args)
        {
            start.ArgumentList.Add(arg);
        }

        start.Environment["ASPNETCORE_ENVIRONMENT"] = "Production";
        start.Environment["Logging__Console__FormatterName"] = "simple";
        foreach (var (key, value) in environment)
        {
            start.Environment[key] = value;
        }

        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync(Ct);
        var stderr = process.StandardError.ReadToEndAsync(Ct);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(60));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw;
        }

        return (process.ExitCode, await stdout + await stderr);
    }
}
