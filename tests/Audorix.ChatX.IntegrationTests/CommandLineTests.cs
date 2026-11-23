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

    [Fact]
    public async Task Without_completed_setup_the_host_starts_in_setup_mode_and_logs_a_setup_code()
    {
        var directory = Path.Combine(Path.GetTempPath(), "chatx-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var (_, output) = await RunApiAsync(["--urls", "http://127.0.0.1:0"], new()
            {
                ["Setup__Completed"] = "false",
                ["Setup__SettingsPath"] = Path.Combine(directory, "settings.json"),
                ["Database__Provider"] = "",
                ["Database__ConnectionString"] = "",
            }, stopWhenOutputContains: "The code is also saved to");

            // The log line, a banner on standard output, and a file next to the settings file.
            Assert.Matches(@"setup code:\s+([A-Z2-9]{4}-[A-Z2-9]{4}-[A-Z2-9]{4})", output);
            var value = System.Text.RegularExpressions.Regex.Match(output, @"setup code:\s+([A-Z2-9-]{14})").Groups[1].Value;
            Assert.Contains($"setup code {value}", output, StringComparison.Ordinal);
            Assert.Equal(value, (await File.ReadAllTextAsync(Path.Combine(directory, "setup-code.txt"), Ct)).Trim());
            Assert.DoesNotContain("configuration is invalid", output, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private static async Task<(int ExitCode, string Output)> RunApiAsync(
        string[] args, Dictionary<string, string?> environment, string? stopWhenOutputContains = null)
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
        start.Environment["Setup__Completed"] = "true";
        foreach (var (key, value) in environment)
        {
            start.Environment[key] = value;
        }

        using var process = Process.Start(start)!;
        if (stopWhenOutputContains is not null)
        {
            // Long-running host: read until the expected line appears, then stop the process.
            var collected = new System.Text.StringBuilder();
            using var wait = CancellationTokenSource.CreateLinkedTokenSource(Ct);
            wait.CancelAfter(TimeSpan.FromSeconds(60));
            try
            {
                while (await process.StandardOutput.ReadLineAsync(wait.Token) is { } line)
                {
                    collected.AppendLine(line);
                    if (line.Contains(stopWhenOutputContains, StringComparison.Ordinal))
                    {
                        break;
                    }
                }
            }
            finally
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None);
            }

            return (-1, collected.ToString());
        }

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
