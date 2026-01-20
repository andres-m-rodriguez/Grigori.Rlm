namespace Grigori.Infrastructure.Sandbox;

public record SandboxExecuteRequest(string SessionId, string Code, Dictionary<string, string> Context, string CallbackUrl, int Depth = 0, int MaxDepth = 5);

public record SandboxExecuteResponse(string SessionId, string Result, string Output, string? Error, int CallCount);

public record RecursiveCallRequest(string SessionId, string Prompt, Dictionary<string, string> Context, int Depth = 0);

public record RecursiveCallResponse(string Result);

public class SandboxOptions
{
    public const string SectionName = "Sandbox";
    public string BaseUrl { get; set; } = "http://localhost:8100";
    public int TimeoutSeconds { get; set; } = 120;
    public int MaxDepth { get; set; } = 5;
}
