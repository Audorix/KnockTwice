using Audorix.ChatX.Api.Hosting;

var builder = WebApplication.CreateBuilder(args);
builder.AddChatXApi();

var app = builder.Build();
app.UseChatXApi();

return await ChatXHost.RunAsync(app, args).ConfigureAwait(false);

/// <summary>Entry point; public so integration tests can use <c>WebApplicationFactory&lt;Program&gt;</c>.</summary>
public partial class Program;
