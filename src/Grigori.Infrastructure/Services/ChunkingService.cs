namespace Grigori.Infrastructure.Services;

public class ChunkingService
{
    public IEnumerable<TextChunk> Chunk(string content, ChunkOptions? options = null)
    {
        options ??= new ChunkOptions();
        var lines = content.Split('\n');
        var chunks = new List<TextChunk>();

        for (int i = 0; i < lines.Length; i += options.ChunkSize - options.Overlap)
        {
            var chunkLines = lines.Skip(i).Take(options.ChunkSize).ToArray();
            chunks.Add(new TextChunk
            {
                Content = string.Join('\n', chunkLines),
                StartLine = i,
                EndLine = Math.Min(i + chunkLines.Length, lines.Length)
            });

            if (i + options.ChunkSize >= lines.Length) break;
        }

        return chunks;
    }
}

public record TextChunk
{
    public required string Content { get; init; }
    public int StartLine { get; init; }
    public int EndLine { get; init; }
    public string? FilePath { get; init; }
}

public record ChunkOptions
{
    public int ChunkSize { get; init; } = 100;
    public int Overlap { get; init; } = 10;
}
