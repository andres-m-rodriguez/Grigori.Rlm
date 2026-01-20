using Grigori.Infrastructure;
using Grigori.Infrastructure.Orchestration;
using Grigori.Infrastructure.Sandbox;
using Grigori.Rlm.Mcp.Features.Explore.Endpoints;
using Grigori.Rlm.Mcp.Features.Rlm.Endpoints;
using ModelContextProtocol.Server;

var builder = WebApplication.CreateBuilder(args);

// Check run mode
var mcpMode = args.Contains("--mcp");
var mcpHttpMode = args.Contains("--mcp-http");
if (!mcpMode) mcpHttpMode = true;

// Add Grigori Infrastructure
builder.Services.AddGrigoriInfrastructure(builder.Configuration);

// Register MCP tool endpoints
builder.Services.AddScoped<ExploreEndpoints>();
builder.Services.AddScoped<RlmEndpoints>();

if (mcpMode)
{
    builder.Services.AddMcpServer().WithStdioServerTransport().WithToolsFromAssembly();
}
else
{
    builder.Services.AddMcpServer().WithHttpTransport().WithToolsFromAssembly();
}

var app = builder.Build();

// Get base URL for callback configuration
var baseUrl = builder.Configuration["Urls"] ?? "http://localhost:5000";

// Configure orchestrator callback URL
using (var scope = app.Services.CreateScope())
{
    var orchestrator = scope.ServiceProvider.GetRequiredService<RlmOrchestrator>();
    orchestrator.CallbackBaseUrl = baseUrl;
}

if (mcpHttpMode)
{
    app.MapMcp("/mcp");
}

// Health endpoint
app.MapGet("/health", () => Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow }));

// RLM recursive callback endpoint (called by Python sandbox)
app.MapPost("/rlm/recurse", async (RecursiveCallRequest request, RlmOrchestrator orchestrator, CancellationToken ct) =>
{
    var response = await orchestrator.HandleRecursiveCallAsync(request, ct);
    return Results.Ok(response);
});

await app.StartAsync();
var urls = app.Urls.ToList();
baseUrl = urls.FirstOrDefault() ?? "http://localhost:5000";

// Update orchestrator with actual URL
using (var scope = app.Services.CreateScope())
{
    var orchestrator = scope.ServiceProvider.GetRequiredService<RlmOrchestrator>();
    orchestrator.CallbackBaseUrl = baseUrl;
}

if (mcpMode)
{
    Console.Error.WriteLine("Grigori RLM MCP Server started (stdio transport)");
    Console.Error.WriteLine($"Callback URL: {baseUrl}");
    await Task.Delay(Timeout.Infinite);
}
else
{
    Console.WriteLine("Grigori RLM MCP Server started (HTTP transport)");
    Console.WriteLine($"  MCP:      {baseUrl}/mcp");
    Console.WriteLine($"  Callback: {baseUrl}/rlm/recurse");
    Console.WriteLine($"  Health:   {baseUrl}/health");
    await app.WaitForShutdownAsync();
}
