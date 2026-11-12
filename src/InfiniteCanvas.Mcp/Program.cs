using InfiniteCanvas.Mcp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// Serveur MCP GreenBoard (transport stdio) : l'enseignant demande une illustration à l'IA,
// l'IA écrit un SVG, le serveur le rend et le pose sur le tableau GreenBoard ouvert (canal nommé local).
var builder = Host.CreateApplicationBuilder(args);

// stdout est réservé au protocole MCP : tous les journaux vont sur stderr
builder.Logging.ClearProviders();
builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);

builder.Services.AddSingleton<BoardClient>();
builder.Services
    .AddMcpServer(options =>
    {
        options.ServerInfo = new() { Name = "greenboard", Title = "GreenBoard — tableau blanc", Version = "1.0.0" };
        options.ServerInstructions = DesignGuide.Text;
    })
    .WithStdioServerTransport()
    .WithTools<BoardTools>()
    .WithPrompts<TeachingPrompts>();

await builder.Build().RunAsync();
