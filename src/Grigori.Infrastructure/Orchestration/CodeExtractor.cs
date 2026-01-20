using System.Text.RegularExpressions;

namespace Grigori.Infrastructure.Orchestration;

public static partial class CodeExtractor
{
    [GeneratedRegex(@"```python\s*(.*?)```", RegexOptions.Singleline)]
    private static partial Regex PythonCodeBlockRegex();

    [GeneratedRegex(@"```\s*(.*?)```", RegexOptions.Singleline)]
    private static partial Regex GenericCodeBlockRegex();

    public static string ExtractPython(string llmResponse)
    {
        // Try python-specific code block first
        var pythonMatch = PythonCodeBlockRegex().Match(llmResponse);
        if (pythonMatch.Success)
            return pythonMatch.Groups[1].Value.Trim();

        // Fall back to generic code block
        var genericMatch = GenericCodeBlockRegex().Match(llmResponse);
        if (genericMatch.Success)
            return genericMatch.Groups[1].Value.Trim();

        // No code block found - assume entire response is code (or return empty)
        var trimmed = llmResponse.Trim();
        if (trimmed.Contains("result =") || trimmed.Contains("rlm_call("))
            return trimmed;

        return string.Empty;
    }
}
