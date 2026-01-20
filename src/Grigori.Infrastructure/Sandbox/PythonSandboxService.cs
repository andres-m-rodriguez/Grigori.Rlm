using System.Net.Http.Json;
using Microsoft.Extensions.Options;

namespace Grigori.Infrastructure.Sandbox;

public class PythonSandboxService : ISandboxService
{
    private readonly HttpClient _http;

    public PythonSandboxService(HttpClient http, IOptions<SandboxOptions> options)
    {
        _http = http;
        _http.BaseAddress = new Uri(options.Value.BaseUrl);
        _http.Timeout = TimeSpan.FromSeconds(options.Value.TimeoutSeconds);
    }

    public async Task<SandboxExecuteResponse> ExecuteAsync(SandboxExecuteRequest request, CancellationToken ct = default)
    {
        var payload = new
        {
            session_id = request.SessionId,
            code = request.Code,
            context = request.Context,
            callback_url = request.CallbackUrl,
            depth = request.Depth,
            max_depth = request.MaxDepth
        };

        var response = await _http.PostAsJsonAsync("/execute", payload, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<SandboxJsonResponse>(ct)
            ?? throw new InvalidOperationException("Empty response from sandbox");

        return new SandboxExecuteResponse(
            result.session_id,
            result.result,
            result.output,
            result.error,
            result.call_count
        );
    }

    private record SandboxJsonResponse(string session_id, string result, string output, string? error, int call_count);
}
