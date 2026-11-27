using Audorix.KnockTwice.Api.Setup;

namespace Audorix.KnockTwice.Api.Hosting;

/// <summary>
/// Chooses the host for this process. Until <c>Setup:Completed</c> is true (written by the setup
/// wizard into the settings file) the API runs the setup host; after a successful setup it
/// restarts in-process into the normal host, which reads the new settings file. Operator
/// commands (<c>migrate</c>, <c>grant-role</c>) always use the normal host and its validation.
/// </summary>
internal static class ChatXProgram
{
    public static async Task<int> RunAsync(string[] args)
    {
        while (true)
        {
            var builder = WebApplication.CreateBuilder(args);
            builder.AddChatXSettingsFile();
            builder.AddDatabaseConnectionSecretFile();

            if (ChatXSettingsFile.IsSetupRequired(builder.Configuration) && !ChatXHost.IsCommand(args))
            {
                builder.AddChatXSetupMode();
                var setupApp = builder.Build();
                setupApp.UseChatXSetupMode();
                var (restart, exitCode) = await SetupMode.RunAsync(setupApp).ConfigureAwait(false);
                if (restart)
                {
                    continue;
                }

                return exitCode;
            }

            builder.AddChatXApi();
            var app = builder.Build();
            app.UseChatXApi();
            return await ChatXHost.RunAsync(app, args).ConfigureAwait(false);
        }
    }
}
