using System.ComponentModel;
using System.Text.Json;
using Grigori.Infrastructure.LlmClients;
using Grigori.Infrastructure.Sandbox;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace Grigori.Rlm.Mcp.Features.ClaudeExecute.Endpoints;

/// <summary>
/// MCP tool that lets Claude Code generate Python code for execution,
/// with optional local LLM summarization of results.
///
/// This inverts the typical RLM pattern:
/// - Claude (powerful) generates the analysis code
/// - Local LLM (fast/cheap) summarizes large outputs
/// </summary>
[McpServerToolType]
public class ClaudeExecuteEndpoints
{
    private readonly ISandboxService _sandbox;
    private readonly ILlmClient _llm;
    private readonly ILogger<ClaudeExecuteEndpoints> _logger;

    public ClaudeExecuteEndpoints(
        ISandboxService sandbox,
        ILlmClient llm,
        ILogger<ClaudeExecuteEndpoints> logger)
    {
        _sandbox = sandbox;
        _llm = llm;
        _logger = logger;
    }

    [McpServerTool(Name = "execute_python")]
    [Description(@"Execute Python code in a sandboxed environment. YOU (Claude) write the code.

Available in the sandbox:
- context: Dict[str, str] - loaded files (if directory provided)
- get_context(key) - get specific file content
- list_context_keys() - list available files
- search_context(pattern) - search files for pattern
- print() - output to stdout
- result - SET THIS with your final answer

Example:
```python
# Find all classes in the codebase
classes = []
for filename, content in context.items():
    for line in content.split('\n'):
        if 'class ' in line and ':' in line:
            classes.append(f'{filename}: {line.strip()}')
result = '\n'.join(classes)
```")]
    public async Task<string> ExecutePythonAsync(
        [Description("Python code to execute. Set 'result' variable with your answer.")] string code,
        [Description("Directory to load as context (optional). Files become available in 'context' dict.")] string? directory = null,
        [Description("File pattern when loading directory (default: **/*.cs)")] string pattern = "**/*.cs",
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Claude execute: running Python code");

        try
        {
            // Load context if directory provided
            var context = new Dictionary<string, string>();
            if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory))
            {
                context = await LoadContextAsync(directory, pattern, cancellationToken);
                _logger.LogInformation("Loaded {Count} files into context", context.Count);
            }

            // Execute in sandbox
            var request = new SandboxExecuteRequest(
                SessionId: Guid.NewGuid().ToString(),
                Code: code,
                Context: context,
                CallbackUrl: "", // No callbacks needed - Claude orchestrates
                Depth: 0,
                MaxDepth: 1
            );

            var response = await _sandbox.ExecuteAsync(request, cancellationToken);

            return JsonSerializer.Serialize(new
            {
                success = string.IsNullOrEmpty(response.Error),
                result = response.Result,
                output = response.Output,
                error = response.Error
            }, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Execute failed");
            return JsonSerializer.Serialize(new { success = false, error = ex.Message });
        }
    }

    [McpServerTool(Name = "execute_and_summarize")]
    [Description(@"Execute Python code and have a local LLM summarize the output.

Use this when:
- Your code produces large output that needs condensing
- You want the local LLM to extract specific information from results
- You want to chain: your code -> local LLM analysis

YOU write the code, the local LLM just processes your output.")]
    public async Task<string> ExecuteAndSummarizeAsync(
        [Description("Python code to execute. Set 'result' variable with data for summarization.")] string code,
        [Description("Prompt for local LLM to process/summarize the result. E.g., 'List the top 5 most important findings'")] string summarizePrompt,
        [Description("Directory to load as context (optional)")] string? directory = null,
        [Description("File pattern when loading directory (default: **/*.cs)")] string pattern = "**/*.cs",
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Claude execute + summarize: running Python then local LLM");

        try
        {
            // Load context if directory provided
            var context = new Dictionary<string, string>();
            if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory))
            {
                context = await LoadContextAsync(directory, pattern, cancellationToken);
                _logger.LogInformation("Loaded {Count} files into context", context.Count);
            }

            // Execute in sandbox
            var request = new SandboxExecuteRequest(
                SessionId: Guid.NewGuid().ToString(),
                Code: code,
                Context: context,
                CallbackUrl: "",
                Depth: 0,
                MaxDepth: 1
            );

            var response = await _sandbox.ExecuteAsync(request, cancellationToken);

            if (!string.IsNullOrEmpty(response.Error))
            {
                return JsonSerializer.Serialize(new
                {
                    success = false,
                    phase = "execution",
                    error = response.Error
                });
            }

            // Combine result and output for summarization
            var dataToSummarize = response.Result;
            if (!string.IsNullOrEmpty(response.Output))
            {
                dataToSummarize = $"Result:\n{response.Result}\n\nOutput:\n{response.Output}";
            }

            // Truncate if too large for LLM
            if (dataToSummarize.Length > 15000)
            {
                dataToSummarize = dataToSummarize[..15000] + "\n[TRUNCATED]";
            }

            // Have local LLM summarize
            var llmPrompt = $"""
                {summarizePrompt}

                Data to analyze:
                ```
                {dataToSummarize}
                ```

                Provide a concise response:
                """;

            var summary = await _llm.CompleteAsync(llmPrompt, cancellationToken);

            return JsonSerializer.Serialize(new
            {
                success = true,
                summary = summary.Trim(),
                rawResultLength = response.Result.Length,
                rawOutputLength = response.Output?.Length ?? 0
            }, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Execute and summarize failed");
            return JsonSerializer.Serialize(new { success = false, error = ex.Message });
        }
    }

    [McpServerTool(Name = "local_llm_analyze")]
    [Description(@"Send data directly to the local LLM for analysis without code execution.

Use this when you already have data and just want the local LLM to:
- Summarize it
- Extract specific information
- Answer questions about it
- Classify or categorize it")]
    public async Task<string> LocalLlmAnalyzeAsync(
        [Description("The data/text to analyze")] string data,
        [Description("What you want the local LLM to do with the data")] string prompt,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Local LLM analyze: {PromptPreview}",
            prompt.Length > 50 ? prompt[..50] + "..." : prompt);

        try
        {
            // Truncate data if too large
            var truncatedData = data;
            if (data.Length > 15000)
            {
                truncatedData = data[..15000] + "\n[TRUNCATED]";
            }

            var llmPrompt = $"""
                {prompt}

                Data:
                ```
                {truncatedData}
                ```

                Response:
                """;

            var response = await _llm.CompleteAsync(llmPrompt, cancellationToken);

            return JsonSerializer.Serialize(new
            {
                success = true,
                response = response.Trim(),
                dataLength = data.Length,
                wasTruncated = data.Length > 15000
            }, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Local LLM analyze failed");
            return JsonSerializer.Serialize(new { success = false, error = ex.Message });
        }
    }

    private static async Task<Dictionary<string, string>> LoadContextAsync(
        string directory, string pattern, CancellationToken ct)
    {
        var context = new Dictionary<string, string>();
        var searchPattern = pattern.Replace("**/", "");
        var searchOption = pattern.StartsWith("**/")
            ? SearchOption.AllDirectories
            : SearchOption.TopDirectoryOnly;

        var files = Directory.GetFiles(directory, searchPattern, searchOption)
            .Take(50); // Limit files

        foreach (var file in files)
        {
            var relativePath = Path.GetRelativePath(directory, file);
            var content = await File.ReadAllTextAsync(file, ct);

            // Limit file size
            if (content.Length > 50000)
                content = content[..50000] + "\n[TRUNCATED]";

            context[relativePath] = content;
        }

        return context;
    }
}
