"""
RLM Python Sandbox - Executes LLM-generated Python code with recursive call support.
"""
from typing import Dict, Any, Optional, Callable
import traceback
import io
import sys


class RlmRepl:
    """
    REPL environment for RLM execution.
    Provides context access and recursive call capability to LLM-generated code.
    """

    def __init__(
        self,
        session_id: str,
        context: Dict[str, str],
        recursive_callback: Callable[[str, Dict[str, str]], str],
        max_output_len: int = 50000
    ):
        self.session_id = session_id
        self.context = context
        self._recursive_callback = recursive_callback
        self._max_output_len = max_output_len
        self._call_count = 0
        self._max_calls = 20  # Safety limit per execution

    def rlm_call(self, prompt: str, subset: Optional[Dict[str, str]] = None) -> str:
        """
        Recursive call to the LLM orchestrator.
        The LLM-generated code calls this to spawn sub-analysis.

        Args:
            prompt: The task/question for the recursive call
            subset: Optional context subset (defaults to full context if None)

        Returns:
            The result from the recursive LLM call
        """
        self._call_count += 1
        if self._call_count > self._max_calls:
            return f"[ERROR: Max recursive calls ({self._max_calls}) exceeded]"

        # Use full context if no subset provided
        if subset is None:
            subset = self.context

        return self._recursive_callback(prompt, subset)

    def get_context(self, key: str) -> str:
        """Get a specific context item by key."""
        return self.context.get(key, f"[Key '{key}' not found in context]")

    def list_context_keys(self) -> list:
        """List all available context keys."""
        return list(self.context.keys())

    def search_context(self, pattern: str) -> Dict[str, str]:
        """Search context for keys/values containing pattern."""
        pattern_lower = pattern.lower()
        return {
            k: v for k, v in self.context.items()
            if pattern_lower in k.lower() or pattern_lower in v.lower()
        }

    def execute(self, code: str) -> Dict[str, Any]:
        """
        Execute LLM-generated Python code in a sandboxed namespace.

        Args:
            code: Python code to execute

        Returns:
            Dict with 'result', 'output', 'error', and 'call_count'
        """
        # Capture stdout
        stdout_capture = io.StringIO()
        old_stdout = sys.stdout

        # Create execution namespace with REPL functions
        namespace = {
            # Context and data
            "context": self.context,

            # REPL functions
            "rlm_call": self.rlm_call,
            "get_context": self.get_context,
            "list_context_keys": self.list_context_keys,
            "search_context": self.search_context,

            # Result variable - LLM sets this
            "result": "",

            # Safe builtins
            "print": print,
            "len": len,
            "str": str,
            "int": int,
            "float": float,
            "bool": bool,
            "list": list,
            "dict": dict,
            "set": set,
            "tuple": tuple,
            "range": range,
            "enumerate": enumerate,
            "zip": zip,
            "map": map,
            "filter": filter,
            "sorted": sorted,
            "reversed": reversed,
            "sum": sum,
            "min": min,
            "max": max,
            "abs": abs,
            "round": round,
            "any": any,
            "all": all,
            "isinstance": isinstance,
            "hasattr": hasattr,
            "getattr": getattr,
        }

        error = None
        try:
            sys.stdout = stdout_capture
            exec(code, namespace)
        except Exception as e:
            error = f"{type(e).__name__}: {str(e)}\n{traceback.format_exc()}"
        finally:
            sys.stdout = old_stdout

        output = stdout_capture.getvalue()
        if len(output) > self._max_output_len:
            output = output[:self._max_output_len] + "\n[OUTPUT TRUNCATED]"

        # Ensure result is always a string
        result_value = namespace.get("result", "")
        if not isinstance(result_value, str):
            result_value = str(result_value)

        return {
            "result": result_value,
            "output": output,
            "error": error,
            "call_count": self._call_count
        }
