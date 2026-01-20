using Grigori.Infrastructure;
using Grigori.Rlm.Mcp.Features.Explore.Endpoints;
using ModelContextProtocol.Server;

var builder = WebApplication.CreateBuilder(args);

// Check run mode
var mcpMode = args.Contains("--mcp");           // stdio MCP mode (for local use)
var mcpHttpMode = args.Contains("--mcp-http");  // HTTP MCP mode (for remote AI clients)

// Default to HTTP mode if no flags provided
if (!mcpMode)
    mcpHttpMode = true;

// Add Grigori Infrastructure services (Ollama client, chunking, exploration)
builder.Services.AddGrigoriInfrastructure(builder.Configuration);

// Register MCP tool endpoints
builder.Services.AddScoped<ExploreEndpoints>();

if (mcpMode)
{
    // Register MCP server with stdio transport (local use)
    builder.Services
        .AddMcpServer()
        .WithStdioServerTransport()
        .WithToolsFromAssembly();
}
else
{
    // Register MCP server with HTTP transport
    builder.Services
        .AddMcpServer()
        .WithHttpTransport()
        .WithToolsFromAssembly();
}

var app = builder.Build();

if (mcpHttpMode)
{
    // Map MCP endpoints for Streamable HTTP transport
    app.MapMcp("/mcp");
}

// Health endpoint
app.MapGet("/health", () => Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow }));

await app.StartAsync();
var urls = app.Urls.ToList();
var baseUrl = urls.FirstOrDefault() ?? "http://localhost:5000";

if (mcpMode)
{
    Console.Error.WriteLine("Grigori RLM MCP Server started (stdio transport)");
    await Task.Delay(Timeout.Infinite);
}
else
{
    Console.WriteLine("Grigori RLM MCP Server started (HTTP transport)");
    Console.WriteLine($"  MCP:     {baseUrl}/mcp");
    Console.WriteLine($"  Health:  {baseUrl}/health");
    await app.WaitForShutdownAsync();
}
