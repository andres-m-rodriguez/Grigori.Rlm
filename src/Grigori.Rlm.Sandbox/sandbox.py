"""
RLM Python Sandbox - Executes LLM-generated Python code with recursive call support.
"""
from typing import Dict, Any, Optional, Callable, List, Tuple
import traceback
import io
import sys
import re
import ast


class CodeValidator(ast.NodeVisitor):
    """
    Validates Python code before execution by walking the AST.
    Blocks dangerous patterns that could escape the sandbox.
    """

    BLOCKED_NAMES = frozenset({
        '__import__', 'eval', 'exec', 'compile', 'open',
        'input', 'breakpoint', 'globals', 'locals', 'vars',
        'dir', 'type', 'object', 'getattr', 'setattr', 'delattr',
        'hasattr', 'memoryview', 'bytearray', 'bytes',
    })

    BLOCKED_ATTRS = frozenset({
        '__class__', '__bases__', '__mro__', '__subclasses__',
        '__globals__', '__code__', '__builtins__', '__import__',
        '__dict__', '__module__', '__self__', '__func__',
        '__closure__', '__annotations__', '__kwdefaults__',
        '__defaults__', '__qualname__', '__reduce__', '__reduce_ex__',
    })

    def __init__(self):
        self.errors: List[str] = []

    def visit_Import(self, node: ast.Import) -> None:
        names = ', '.join(alias.name for alias in node.names)
        self.errors.append(f"Import statements not allowed: import {names}")

    def visit_ImportFrom(self, node: ast.ImportFrom) -> None:
        self.errors.append(f"Import statements not allowed: from {node.module} import ...")

    def visit_Name(self, node: ast.Name) -> None:
        if node.id in self.BLOCKED_NAMES:
            self.errors.append(f"Blocked function: {node.id}")
        elif node.id.startswith('__') and node.id.endswith('__'):
            self.errors.append(f"Dunder names not allowed: {node.id}")
        self.generic_visit(node)

    def visit_Attribute(self, node: ast.Attribute) -> None:
        if node.attr in self.BLOCKED_ATTRS:
            self.errors.append(f"Blocked attribute: {node.attr}")
        elif node.attr.startswith('__') and node.attr.endswith('__'):
            self.errors.append(f"Dunder attributes not allowed: {node.attr}")
        elif node.attr.startswith('_'):
            self.errors.append(f"Private attributes not allowed: {node.attr}")
        self.generic_visit(node)

    def visit_Call(self, node: ast.Call) -> None:
        # Check for string-based getattr calls like getattr(x, '__class__')
        if isinstance(node.func, ast.Name) and node.func.id in ('getattr', 'setattr', 'delattr', 'hasattr'):
            if len(node.args) >= 2 and isinstance(node.args[1], ast.Constant):
                attr_name = node.args[1].value
                if isinstance(attr_name, str) and (attr_name in self.BLOCKED_ATTRS or attr_name.startswith('_')):
                    self.errors.append(f"Blocked dynamic attribute access: {attr_name}")
        self.generic_visit(node)


def validate_code(code: str) -> Tuple[bool, List[str]]:
    """
    Parse and validate code for security issues.

    Args:
        code: Python source code to validate

    Returns:
        Tuple of (is_valid, list of error messages)
    """
    try:
        tree = ast.parse(code)
    except SyntaxError as e:
        return False, [f"Syntax error at line {e.lineno}: {e.msg}"]

    validator = CodeValidator()
    validator.visit(tree)

    return len(validator.errors) == 0, validator.errors


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

    def search_context(self, pattern: str, *args, **kwargs) -> Dict[str, str]:
        """Search context for keys/values containing pattern. Extra args ignored for compatibility."""
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
        # Validate code before execution
        is_valid, validation_errors = validate_code(code)
        if not is_valid:
            return {
                "result": "",
                "output": "",
                "error": f"Code validation failed:\n" + "\n".join(f"  - {e}" for e in validation_errors),
                "call_count": self._call_count
            }

        # Capture stdout
        stdout_capture = io.StringIO()
        old_stdout = sys.stdout

        # Create execution namespace with REPL functions
        # NOTE: __builtins__ is explicitly set to None to prevent access to dangerous functions
        namespace = {
            # Block access to builtins entirely
            "__builtins__": {},

            # Context and data
            "context": self.context,

            # REPL functions
            "rlm_call": self.rlm_call,
            "get_context": self.get_context,
            "list_context_keys": self.list_context_keys,
            "search_context": self.search_context,

            # Result variable - LLM sets this
            "result": "",

            # Safe builtins only - no getattr/hasattr/type/object
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
            "repr": repr,
            "chr": chr,
            "ord": ord,
            "hex": hex,
            "bin": bin,
            "oct": oct,
            "pow": pow,
            "divmod": divmod,
            "slice": slice,
            "iter": iter,
            "next": next,
            "callable": callable,
            "format": format,
            "id": id,
            "hash": hash,

            # Safe modules
            "re": re,

            # Exceptions (needed for try/except)
            "Exception": Exception,
            "ValueError": ValueError,
            "TypeError": TypeError,
            "KeyError": KeyError,
            "IndexError": IndexError,
            "AttributeError": AttributeError,
            "StopIteration": StopIteration,
            "RuntimeError": RuntimeError,

            # Constants
            "True": True,
            "False": False,
            "None": None,
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
