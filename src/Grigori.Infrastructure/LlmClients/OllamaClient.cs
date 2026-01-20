using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Grigori.Infrastructure.LlmClients;

public class OllamaClient : ILlmClient
{
    private readonly HttpClient _http;
    private readonly string _model;

    public OllamaClient(HttpClient http, IOptions<OllamaOptions> options)
    {
        _http = http;
        _model = options.Value.Model;
        _http.BaseAddress = new Uri(options.Value.BaseUrl);
        _http.Timeout = TimeSpan.FromSeconds(options.Value.TimeoutSeconds);
    }

    public async Task<string> CompleteAsync(string prompt, CancellationToken ct = default)
    {
        var request = new { model = _model, prompt, stream = false };
        var response = await _http.PostAsJsonAsync("/api/generate", request, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<OllamaResponse>(ct);
        return result?.Response ?? string.Empty;
    }

    public async Task<string> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken ct = default)
    {
        var request = new { model = _model, system = systemPrompt, prompt = userPrompt, stream = false };
        var response = await _http.PostAsJsonAsync("/api/generate", request, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<OllamaResponse>(ct);
        return result?.Response ?? string.Empty;
    }

    public async IAsyncEnumerable<string> StreamAsync(
        string prompt,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var request = new { model = _model, prompt, stream = true };
        var response = await _http.PostAsJsonAsync("/api/generate", request, ct);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        while (!reader.EndOfStream && !ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct);
            if (string.IsNullOrEmpty(line)) continue;

            var chunk = JsonSerializer.Deserialize<OllamaResponse>(line);
            if (chunk?.Response is not null)
            {
                yield return chunk.Response;
            }
        }
    }

    private record OllamaResponse(string? Response, bool Done);
}
