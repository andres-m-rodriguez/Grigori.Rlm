namespace Grigori.Infrastructure.LlmClients;

public interface ILlmClient
{
    Task<string> CompleteAsync(string prompt, CancellationToken ct = default);

    Task<string> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken ct = default);

    IAsyncEnumerable<string> StreamAsync(string prompt, CancellationToken ct = default);
}
