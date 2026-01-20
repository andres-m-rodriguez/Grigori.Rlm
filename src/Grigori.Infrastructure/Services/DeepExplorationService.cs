using Grigori.Infrastructure.LlmClients;

namespace Grigori.Infrastructure.Services;

public class DeepExplorationService
{
    private readonly ILlmClient _llm;
    private readonly ChunkingService _chunker;
    private readonly SemaphoreSlim _semaphore;

    public DeepExplorationService(ILlmClient llm, ChunkingService chunker, ExploreOptions? defaultOptions = null)
    {
        _llm = llm;
        _chunker = chunker;
        _semaphore = new SemaphoreSlim(defaultOptions?.MaxConcurrentRequests ?? 4);
    }

    public async Task<ExplorationResult> ExploreAsync(
        string query,
        IEnumerable<string> filePaths,
        ExploreOptions? options = null,
        CancellationToken ct = default)
    {
        options ??= new ExploreOptions();
        var allChunks = new List<TextChunk>();

        // Load and chunk file contents
        foreach (var filePath in filePaths.Take(options.MaxFiles))
        {
            if (!File.Exists(filePath)) continue;

            var content = await File.ReadAllTextAsync(filePath, ct);
            var chunks = _chunker.Chunk(content, options.ChunkOptions);
            allChunks.AddRange(chunks.Select(c => c with { FilePath = filePath }));
        }

        if (allChunks.Count == 0)
        {
            return new ExplorationResult
            {
                Answer = "No content found to analyze.",
                SourceChunks = [],
                ChunksProcessed = 0,
                ChunksRelevant = 0
            };
        }

        // Process chunks in parallel with concurrency limit
        const string chunkPlaceholder = "{chunk}";
        var summaryPrompt = $"""
            Analyze this code chunk in context of the query: "{query}"

            If relevant, explain what this code does and how it relates to the query.
            If not relevant, respond with "NOT_RELEVANT".

            Code:
            ```
            {chunkPlaceholder}
            ```
            """;

        var tasks = allChunks.Take(options.MaxChunks).Select(async chunk =>
        {
            await _semaphore.WaitAsync(ct);
            try
            {
                var prompt = summaryPrompt.Replace("{chunk}", chunk.Content);
                var summary = await _llm.CompleteAsync(prompt, ct);
                return new ChunkSummary(chunk, summary);
            }
            finally
            {
                _semaphore.Release();
            }
        });

        var summaries = await Task.WhenAll(tasks);

        // Filter relevant summaries
        var relevant = summaries
            .Where(s => !s.Summary.Contains("NOT_RELEVANT", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (relevant.Count == 0)
        {
            return new ExplorationResult
            {
                Answer = "No relevant code sections found for the query.",
                SourceChunks = [],
                ChunksProcessed = summaries.Length,
                ChunksRelevant = 0
            };
        }

        // Final aggregation call
        var aggregationPrompt = $"""
            Based on these code analysis summaries, answer the original query: "{query}"

            Summaries:
            {string.Join("\n\n", relevant.Select(r => $"[{r.Chunk.FilePath}:{r.Chunk.StartLine}-{r.Chunk.EndLine}]\n{r.Summary}"))}

            Provide a comprehensive answer that synthesizes all relevant information.
            """;

        var finalAnswer = await _llm.CompleteAsync(aggregationPrompt, ct);

        return new ExplorationResult
        {
            Answer = finalAnswer,
            SourceChunks = relevant.Select(r => r.Chunk).ToList(),
            ChunksProcessed = summaries.Length,
            ChunksRelevant = relevant.Count
        };
    }
}

public record ChunkSummary(TextChunk Chunk, string Summary);

public record ExplorationResult
{
    public required string Answer { get; init; }
    public required List<TextChunk> SourceChunks { get; init; }
    public int ChunksProcessed { get; init; }
    public int ChunksRelevant { get; init; }
}

public record ExploreOptions
{
    public int MaxFiles { get; init; } = 10;
    public int MaxChunks { get; init; } = 50;
    public int MaxConcurrentRequests { get; init; } = 4;
    public string? Scope { get; init; }
    public ChunkOptions ChunkOptions { get; init; } = new();
}
