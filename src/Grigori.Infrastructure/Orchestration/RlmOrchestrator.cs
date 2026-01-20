using Grigori.Infrastructure.LlmClients;
using Grigori.Infrastructure.Prompts;
using Grigori.Infrastructure.Sandbox;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;

namespace Grigori.Infrastructure.Orchestration;

public class RlmOrchestrator
{
    private readonly ILlmClient _llm;
    private readonly ISandboxService _sandbox;
    private readonly SandboxOptions _options;
    private readonly ILogger<RlmOrchestrator> _logger;
    private readonly ConcurrentDictionary<string, RlmSession> _sessions = new();

    public string CallbackBaseUrl { get; set; } = "http://localhost:5000";

    public RlmOrchestrator(
        ILlmClient llm,
        ISandboxService sandbox,
        IOptions<SandboxOptions> options,
        ILogger<RlmOrchestrator> logger)
    {
        _llm = llm;
        _sandbox = sandbox;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<RlmResult> ExecuteAsync(RlmRequest request, CancellationToken ct = default)
    {
        var session = new RlmSession(
            Guid.NewGuid().ToString(),
            request.MaxDepth,
            request.Context
        );
        _sessions[session.Id] = session;

        try
        {
            var result = await ExecuteAtDepthAsync(session, request.Query, request.Context, 0, ct);
            return new RlmResult(
                result,
                session.CurrentDepth,
                session.TotalCalls,
                session.Trace.ToList()
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RLM execution failed for session {SessionId}", session.Id);
            return new RlmResult(
                string.Empty,
                session.CurrentDepth,
                session.TotalCalls,
                session.Trace.ToList(),
                ex.Message
            );
        }
        finally
        {
            _sessions.TryRemove(session.Id, out _);
        }
    }

    public async Task<RecursiveCallResponse> HandleRecursiveCallAsync(RecursiveCallRequest request, CancellationToken ct = default)
    {
        if (!_sessions.TryGetValue(request.SessionId, out var session))
            return new RecursiveCallResponse("[ERROR: Session not found]");

        var result = await ExecuteAtDepthAsync(session, request.Prompt, request.Context, request.Depth, ct);
        return new RecursiveCallResponse(result);
    }

    private async Task<string> ExecuteAtDepthAsync(
        RlmSession session,
        string query,
        Dictionary<string, string> context,
        int depth,
        CancellationToken ct)
    {
        session.CurrentDepth = Math.Max(session.CurrentDepth, depth);

        if (depth >= session.MaxDepth)
        {
            _logger.LogWarning("Max depth {MaxDepth} reached for session {SessionId}", session.MaxDepth, session.Id);
            return await GetDirectAnswerAsync(query, context, ct);
        }

        _logger.LogInformation("Executing RLM at depth {Depth} for session {SessionId}", depth, session.Id);

        // 1. Ask LLM for Python code
        var prompt = RlmPrompts.BuildReplPrompt(query, context.Keys);
        var llmResponse = await _llm.CompleteAsync(prompt, ct);

        // 2. Extract Python code
        var code = CodeExtractor.ExtractPython(llmResponse);
        if (string.IsNullOrWhiteSpace(code))
        {
            _logger.LogWarning("No code extracted from LLM response, falling back to direct answer");
            return await GetDirectAnswerAsync(query, context, ct);
        }

        // 3. Execute in sandbox
        var sandboxRequest = new SandboxExecuteRequest(
            session.Id,
            code,
            context,
            CallbackBaseUrl,
            depth,
            session.MaxDepth
        );

        var sandboxResponse = await _sandbox.ExecuteAsync(sandboxRequest, ct);
        session.TotalCalls += sandboxResponse.CallCount;

        // 4. Record trace
        session.Trace.Add(new RlmTraceEntry(
            depth,
            query,
            code,
            sandboxResponse.Result,
            sandboxResponse.CallCount,
            DateTimeOffset.UtcNow
        ));

        if (!string.IsNullOrEmpty(sandboxResponse.Error))
        {
            _logger.LogWarning("Sandbox error at depth {Depth}: {Error}", depth, sandboxResponse.Error);
            return $"[Execution error: {sandboxResponse.Error}]";
        }

        return sandboxResponse.Result;
    }

    private async Task<string> GetDirectAnswerAsync(Dictionary<string, string> context, CancellationToken ct)
    {
        var combined = string.Join("\n\n---\n\n", context.Select(kv => $"// {kv.Key}\n{kv.Value}"));
        return combined.Length > 10000 ? combined[..10000] + "\n[TRUNCATED]" : combined;
    }

    private async Task<string> GetDirectAnswerAsync(string query, Dictionary<string, string> context, CancellationToken ct)
    {
        var combined = string.Join("\n\n", context.Select(kv => $"// {kv.Key}\n{kv.Value}"));
        if (combined.Length > 8000)
            combined = combined[..8000] + "\n[TRUNCATED]";

        var prompt = RlmPrompts.BuildDirectAnswerPrompt(query, combined);
        return await _llm.CompleteAsync(prompt, ct);
    }
}
