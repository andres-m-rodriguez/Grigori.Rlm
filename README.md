# Grigori.Rlm

Recursive Language Model pattern using Claude.ai (subscription) as root LLM, with Ollama handling sub-calls via MCP.

## Overview

**Problem:** RLM requires programmatic LLM access, but many users only have a Claude.ai subscription (no API key).

**Solution:** Use Claude.ai as the "smart" root LLM (covered by subscription), and offload sub-LLM calls to local Ollama through MCP.

```
User (Claude.ai subscriber)
         |
    Claude.ai chat (root LLM)
         |
    Calls Grigori MCP tool
         |
    Grigori chunks context + calls Ollama (free, local)
         |
    Aggregated results return to Claude.ai
         |
    Claude synthesizes final answer
```

## Prerequisites

- .NET 10 SDK
- Ollama running locally (`http://localhost:11434`)

## Run

```bash
# HTTP mode (default)
dotnet run --project src/Grigori.Rlm.Mcp

# stdio mode (for Claude Desktop)
dotnet run --project src/Grigori.Rlm.Mcp -- --mcp
```

## Configuration

Edit `src/Grigori.Rlm.Mcp/appsettings.json`:

```json
{
  "Ollama": {
    "BaseUrl": "http://localhost:11434",
    "Model": "llama3.2",
    "TimeoutSeconds": 120,
    "MaxConcurrentRequests": 4
  }
}
```

## Project Structure

```
src/
├── Grigori.Infrastructure/
│   ├── LlmClients/         # ILlmClient, OllamaClient
│   └── Services/           # ChunkingService, DeepExplorationService
└── Grigori.Rlm.Mcp/
    └── Features/Explore/   # MCP tool endpoints
```

## MCP Tools

- `explore_codebase` - Deep exploration using local LLM for chunk-by-chunk analysis

## License

MIT
