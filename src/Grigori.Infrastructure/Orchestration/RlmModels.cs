namespace Grigori.Infrastructure.Orchestration;

public record RlmRequest(string Query, Dictionary<string, string> Context, int MaxDepth = 5);

public record RlmResult(string Answer, int Depth, int TotalRecursiveCalls, List<RlmTraceEntry> Trace, string? Error = null);

public record RlmTraceEntry(int Depth, string Prompt, string Code, string Result, int CallCount, DateTimeOffset Timestamp);

public record RlmSession(string Id, int MaxDepth, Dictionary<string, string> Context)
{
    public int CurrentDepth { get; set; }
    public int TotalCalls { get; set; }
    public List<RlmTraceEntry> Trace { get; } = [];
}
