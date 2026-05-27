# Build & Run

## Prerequisites
- .NET 10 SDK (`dotnet --version` → 10.x)
- Docker + Docker Compose (for containerised run)

## Quick start (local)

```bash
cd llm-model-proxy

# Restore NuGet packages
dotnet restore

# Build solution
dotnet build

# Run tests
dotnet test

# Run the proxy (listens on :8766)
cd src/LlmModelProxy.Api
Proxy__Providers__OpenAI__ApiKey="sk-..." dotnet run
```

Then in a second terminal:
```bash
export ANTHROPIC_BASE_URL=http://127.0.0.1:8766
claude --dangerously-skip-permissions
```

## Docker (OpenAI backend)

```bash
cd llm-model-proxy

# Copy and edit env
cp .env.example .env   # set OPENAI_API_KEY

docker compose up --build
```

## Docker (local Ollama)

```bash
docker compose --profile ollama up --build
```

## Configuration

All settings are driven by environment variables or `appsettings.json`.

| Env var | Default | Purpose |
|---|---|---|
| `Proxy__Providers__OpenAI__ApiKey` | *(required)* | OpenAI API key |
| `Proxy__Providers__OpenAI__DefaultModel` | `gpt-4o` | Model forwarded to OpenAI |
| `Proxy__Pipeline__ToolChoice` | `required` | Forces tool use (keep `required` for autonomous mode) |
| `Proxy__Pipeline__InjectAgentHint` | `true` | Injects agent mandate into system prompt |
| `Proxy__Logging__FilePath` | `proxy-audit.log` | Per-request audit log path |

## Adding a new provider

1. Create `src/LlmModelProxy.Providers/MyProvider/MyProviderOptions.cs`
2. Implement `ILlmProvider` in `MyProviderProvider.cs`
3. Register in `ProviderServiceExtensions.cs`
4. Add routing rules in `appsettings.json` under `Proxy:ProviderRouting:Rules`
