"""
RLM Sandbox FastAPI Service

This service executes LLM-generated Python code and handles recursive callbacks
to the C# orchestrator.
"""
from fastapi import FastAPI, HTTPException
from pydantic import BaseModel
from typing import Dict, Optional
import httpx
import logging

from sandbox import RlmRepl

logging.basicConfig(level=logging.INFO)
logger = logging.getLogger(__name__)

app = FastAPI(
    title="Grigori RLM Sandbox",
    description="Python execution sandbox for Recursive Language Model pattern",
    version="1.0.0"
)

# Store for active sessions (in production, use Redis or similar)
sessions: Dict[str, dict] = {}


class ExecuteRequest(BaseModel):
    """Request to execute Python code in the sandbox."""
    session_id: str
    code: str
    context: Dict[str, str]
    callback_url: str  # URL to call back for recursive calls
    depth: int = 0
    max_depth: int = 5


class ExecuteResponse(BaseModel):
    """Response from code execution."""
    session_id: str
    result: str
    output: str
    error: Optional[str]
    call_count: int


class RecursiveCallRequest(BaseModel):
    """Request for a recursive RLM call (from C# orchestrator)."""
    session_id: str
    prompt: str
    context: Dict[str, str]


class RecursiveCallResponse(BaseModel):
    """Response from recursive call."""
    result: str


class HealthResponse(BaseModel):
    """Health check response."""
    status: str
    service: str


def make_recursive_callback(callback_url: str, session_id: str, depth: int, max_depth: int):
    """
    Create a callback function that calls the C# orchestrator for recursive calls.
    """
    def callback(prompt: str, subset: Dict[str, str]) -> str:
        if depth >= max_depth:
            return f"[Max recursion depth ({max_depth}) reached]"

        try:
            with httpx.Client(timeout=120.0) as client:
                response = client.post(
                    f"{callback_url}/rlm/recurse",
                    json={
                        "session_id": session_id,
                        "prompt": prompt,
                        "context": subset,
                        "depth": depth + 1
                    }
                )
                response.raise_for_status()
                return response.json().get("result", "")
        except httpx.TimeoutException:
            return "[ERROR: Recursive call timed out]"
        except Exception as e:
            logger.error(f"Recursive callback failed: {e}")
            return f"[ERROR: Recursive call failed - {str(e)}]"

    return callback


@app.post("/execute", response_model=ExecuteResponse)
async def execute_code(request: ExecuteRequest):
    """
    Execute Python code in the RLM sandbox.

    The code has access to:
    - context: Dict[str, str] - The loaded context (files, data)
    - rlm_call(prompt, subset) - Make recursive calls
    - get_context(key) - Get specific context item
    - list_context_keys() - List available keys
    - search_context(pattern) - Search context
    - result - Set this variable with the final answer
    """
    logger.info(f"Executing code for session {request.session_id} at depth {request.depth}")

    # Create recursive callback
    callback = make_recursive_callback(
        request.callback_url,
        request.session_id,
        request.depth,
        request.max_depth
    )

    # Create REPL instance
    repl = RlmRepl(
        session_id=request.session_id,
        context=request.context,
        recursive_callback=callback
    )

    # Execute the code
    result = repl.execute(request.code)

    logger.info(f"Execution complete: {result['call_count']} recursive calls, error={result['error'] is not None}")

    return ExecuteResponse(
        session_id=request.session_id,
        result=result["result"],
        output=result["output"],
        error=result["error"],
        call_count=result["call_count"]
    )


@app.get("/health", response_model=HealthResponse)
async def health_check():
    """Health check endpoint."""
    return HealthResponse(status="healthy", service="grigori-rlm-sandbox")


if __name__ == "__main__":
    import uvicorn
    uvicorn.run(app, host="0.0.0.0", port=8100)
