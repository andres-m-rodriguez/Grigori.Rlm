using System.ComponentModel;
using System.Text.Json;
using Grigori.Infrastructure.Orchestration;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace Grigori.Rlm.Mcp.Features.Rlm.Endpoints;

[McpServerToolType]
public class RlmEndpoints
{
    private readonly RlmOrchestrator _orchestrator;
    private readonly ILogger<RlmEndpoints> _logger;

    public RlmEndpoints(RlmOrchestrator orchestrator, ILogger<RlmEndpoints> logger)
    {
        _orchestrator = orchestrator;
        _logger = logger;
    }

    [McpServerTool(Name = "rlm_analyze")]
    [Description("Deep recursive analysis of code using RLM pattern. The LLM writes Python code to analyze context and can recursively call itself for complex sub-tasks.")]
    public async Task<string> RlmAnalyzeAsync(
        [Description("Natural language query describing what you want to understand")] string query,
        [Description("Directory path containing code to analyze")] string directory,
        [Description("File pattern to match (default: **/*.cs)")] string pattern = "**/*.cs",
        [Description("Maximum recursion depth (default: 5)")] int maxDepth = 5,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(query))
            return "Error: Query is required";

        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
            return $"Error: Directory not found: {directory}";

        _logger.LogInformation("RLM analysis: {Query} in {Directory} with pattern {Pattern}", query, directory, pattern);

        try
        {
            // Load files into context
            var context = await LoadContextAsync(directory, pattern, cancellationToken);
            if (context.Count == 0)
                return $"Error: No files found matching pattern '{pattern}' in {directory}";

            _logger.LogInformation("Loaded {Count} files into context", context.Count);

            var request = new RlmRequest(query, context, maxDepth);
            var result = await _orchestrator.ExecuteAsync(request, cancellationToken);

            return JsonSerializer.Serialize(new
            {
                success = result.Error == null,
                answer = result.Answer,
                stats = new
                {
                    maxDepthReached = result.Depth,
                    totalRecursiveCalls = result.TotalRecursiveCalls,
                    traceEntries = result.Trace.Count
                },
                error = result.Error,
                trace = result.Trace.Select(t => new
                {
                    depth = t.Depth,
                    prompt = t.Prompt.Length > 100 ? t.Prompt[..100] + "..." : t.Prompt,
                    callCount = t.CallCount
                })
            }, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RLM analysis failed");
            return $"Error: {ex.Message}";
        }
    }

    private static async Task<Dictionary<string, string>> LoadContextAsync(string directory, string pattern, CancellationToken ct)
    {
        var context = new Dictionary<string, string>();
        var searchPattern = pattern.Replace("**/", "");
        var searchOption = pattern.StartsWith("**/") ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

        var files = Directory.GetFiles(directory, searchPattern, searchOption)
            .Take(50); // Limit files

        foreach (var file in files)
        {
            var relativePath = Path.GetRelativePath(directory, file);
            var content = await File.ReadAllTextAsync(file, ct);

            // Limit file size
            if (content.Length > 50000)
                content = content[..50000] + "\n[TRUNCATED]";

            context[relativePath] = content;
        }

        return context;
    }
}
