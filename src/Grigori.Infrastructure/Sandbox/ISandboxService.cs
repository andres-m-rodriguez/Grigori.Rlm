namespace Grigori.Infrastructure.Sandbox;

public interface ISandboxService
{
    Task<SandboxExecuteResponse> ExecuteAsync(SandboxExecuteRequest request, CancellationToken ct = default);
}
