using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace Grigori.Infrastructure.Sandbox;

public class PythonSandboxService : ISandboxService
{
    private readonly HttpClient _http;
    private readonly SandboxOptions _options;

    private const int MaxCodeLength = 100_000;
    private const int MaxContextSize = 10_000_000; // 10MB total context
    private const int MaxContextKeyLength = 500;
    private const int MaxSessionIdLength = 100;

    public PythonSandboxService(HttpClient http, IOptions<SandboxOptions> options)
    {
        _http = http;
        _options = options.Value;
        _http.BaseAddress = new Uri(_options.BaseUrl);
        _http.Timeout = TimeSpan.FromSeconds(_options.TimeoutSeconds);
    }

    public async Task<SandboxExecuteResponse> ExecuteAsync(SandboxExecuteRequest request, CancellationToken ct = default)
    {
        ValidateRequest(request);

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

    private void ValidateRequest(SandboxExecuteRequest request)
    {
        // Validate session ID
        if (string.IsNullOrWhiteSpace(request.SessionId))
            throw new ArgumentException("Session ID is required", nameof(request));

        if (request.SessionId.Length > MaxSessionIdLength)
            throw new ArgumentException($"Session ID exceeds maximum length of {MaxSessionIdLength}", nameof(request));

        if (!Regex.IsMatch(request.SessionId, @"^[a-zA-Z0-9_\-]+$"))
            throw new ArgumentException("Session ID contains invalid characters", nameof(request));

        // Validate code
        if (string.IsNullOrWhiteSpace(request.Code))
            throw new ArgumentException("Code is required", nameof(request));

        if (request.Code.Length > MaxCodeLength)
            throw new ArgumentException($"Code exceeds maximum length of {MaxCodeLength} characters", nameof(request));

        // Validate context
        if (request.Context != null)
        {
            long totalSize = 0;
            foreach (var kvp in request.Context)
            {
                if (kvp.Key.Length > MaxContextKeyLength)
                    throw new ArgumentException($"Context key exceeds maximum length of {MaxContextKeyLength}", nameof(request));

                // Check for path traversal attempts in keys
                if (kvp.Key.Contains("..") || kvp.Key.Contains("~"))
                    throw new ArgumentException("Context key contains invalid path characters", nameof(request));

                totalSize += kvp.Key.Length + (kvp.Value?.Length ?? 0);
            }

            if (totalSize > MaxContextSize)
                throw new ArgumentException($"Total context size exceeds maximum of {MaxContextSize / 1_000_000}MB", nameof(request));
        }

        // Validate depth
        if (request.Depth < 0)
            throw new ArgumentException("Depth cannot be negative", nameof(request));

        if (request.MaxDepth < 1 || request.MaxDepth > 10)
            throw new ArgumentException("MaxDepth must be between 1 and 10", nameof(request));

        if (request.Depth > request.MaxDepth)
            throw new ArgumentException("Depth cannot exceed MaxDepth", nameof(request));

        // Validate callback URL if provided
        if (!string.IsNullOrEmpty(request.CallbackUrl))
        {
            if (!Uri.TryCreate(request.CallbackUrl, UriKind.Absolute, out var callbackUri))
                throw new ArgumentException("Callback URL is not a valid URI", nameof(request));

            // Only allow HTTP/HTTPS and localhost callbacks
            if (callbackUri.Scheme != "http" && callbackUri.Scheme != "https")
                throw new ArgumentException("Callback URL must use HTTP or HTTPS", nameof(request));

            if (!callbackUri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) &&
                !callbackUri.Host.Equals("127.0.0.1", StringComparison.Ordinal))
                throw new ArgumentException("Callback URL must be localhost", nameof(request));
        }
    }

    private record SandboxJsonResponse(string session_id, string result, string output, string? error, int call_count);
}
