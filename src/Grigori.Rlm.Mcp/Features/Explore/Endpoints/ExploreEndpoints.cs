using System.ComponentModel;
using System.Text.Json;
using Grigori.Infrastructure.Services;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace Grigori.Rlm.Mcp.Features.Explore.Endpoints;

[McpServerToolType]
public class ExploreEndpoints
{
    private readonly DeepExplorationService _explorationService;
    private readonly ILogger<ExploreEndpoints> _logger;

    public ExploreEndpoints(DeepExplorationService explorationService, ILogger<ExploreEndpoints> logger)
    {
        _explorationService = explorationService;
        _logger = logger;
    }

    [McpServerTool(Name = "explore_codebase")]
    [Description("Deep exploration of codebase using local LLM (Ollama) for chunk-by-chunk analysis. Use this for complex queries that require understanding code across multiple files.")]
    public async Task<string> ExploreCodebaseAsync(
        [Description("Natural language query describing what you want to understand about the codebase")] string query,
        [Description("Comma-separated list of file paths to analyze")] string filePaths,
        [Description("Maximum number of files to analyze (default: 10)")] int maxFiles = 10,
        [Description("Maximum chunks to process per query (default: 50)")] int maxChunks = 50,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(query))
        {
            return "Error: Query is required";
        }

        if (string.IsNullOrEmpty(filePaths))
        {
            return "Error: At least one file path is required";
        }

        var paths = filePaths.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        _logger.LogInformation("Exploring codebase for: {Query} (files: {FileCount}, maxChunks: {MaxChunks})",
            query, paths.Length, maxChunks);

        try
        {
            var result = await _explorationService.ExploreAsync(
                query,
                paths,
                new ExploreOptions
                {
                    MaxFiles = maxFiles,
                    MaxChunks = maxChunks
                },
                cancellationToken);

            return JsonSerializer.Serialize(new
            {
                success = true,
                answer = result.Answer,
                stats = new
                {
                    chunksProcessed = result.ChunksProcessed,
                    chunksRelevant = result.ChunksRelevant
                },
                sources = result.SourceChunks.Select(c => new
                {
                    file = c.FilePath,
                    lines = $"{c.StartLine}-{c.EndLine}"
                }).ToList()
            }, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error exploring codebase");
            return $"Error: {ex.Message}";
        }
    }
}
