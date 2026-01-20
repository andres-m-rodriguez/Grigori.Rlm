namespace Grigori.Infrastructure.Prompts;

public static class RlmPrompts
{
    public static string BuildReplPrompt(string query, IEnumerable<string> contextKeys)
    {
        var keysList = string.Join(", ", contextKeys.Select(k => $"\"{k}\""));

        return $"""
            You are an AI assistant with access to a Python REPL environment for analyzing code.

            ## Available Variables and Functions

            ```python
            # Context dictionary containing code/data to analyze
            context = dict(...)  # Keys: [{keysList}]

            # Get a specific context item
            def get_context(key: str) -> str

            # List all context keys
            def list_context_keys() -> list

            # Search context for pattern
            def search_context(pattern: str) -> dict

            # IMPORTANT: Make recursive calls for complex sub-analysis
            def rlm_call(prompt: str, subset: dict = None) -> str

            # SET THIS with your final answer
            result = ""
            ```

            ## Instructions

            1. Analyze the context to answer the query
            2. For complex analysis, break it down and use `rlm_call()` for sub-tasks
            3. Set `result` with your final answer

            ## Query

            {query}

            ## Response

            Write Python code to analyze the context and answer the query. Always set `result`.
            """;
    }

    public static string BuildDirectAnswerPrompt(string query, string context)
    {
        return $"""
            Analyze this code and answer the query concisely.

            ## Query
            {query}

            ## Code
            ```
            {context}
            ```

            ## Answer
            """;
    }
}
