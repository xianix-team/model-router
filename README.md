# LlmModelProxy

A production-grade **ASP.NET Core 10** service that acts as an **Anthropic API façade**.  
Tools that speak the Anthropic protocol — like the **Claude CLI** — point at this proxy instead of `api.anthropic.com`. The proxy transparently translates every request to a backend of your choice: **OpenAI**, **Azure OpenAI**, **Ollama** (local), or a direct **Anthropic passthrough**.

```
Claude CLI  ──POST /v1/messages──▶  LlmModelProxy  ──▶  OpenAI / Azure / Ollama / Anthropic
    ▲                                                                    │
    └────────────────── Anthropic-format SSE response ◀─────────────────┘
```

Default listen address: `http://0.0.0.0:8766`

---

## Why this exists

The Claude CLI hardcodes the Anthropic wire format. This proxy lets you substitute any OpenAI-compatible backend without touching the client, while also injecting an **autonomous-agent mandate** into every system prompt so the model executes tasks end-to-end without asking for permission mid-task.

Key behaviours injected automatically:

- 7-point autonomous-agent mandate injected into every system prompt (no confirmation requests)
- Synthetic `__task_complete__` tool injected when `tool_choice = required`, giving the model a clean exit path when done
- Per-request structured audit log written to disk
- Provider routing by model-id prefix (`claude-`* → OpenAI, `llama*` → Ollama, etc.)

---

## Prerequisites


| Tool                                              | Version                            |
| ------------------------------------------------- | ---------------------------------- |
| [.NET SDK](https://dotnet.microsoft.com/download) | 10.x (`dotnet --version`)          |
| Docker + Compose                                  | Any recent version *(Docker only)* |


---

## Running locally

### 1 — Build

```bash
cd llm-model-proxy
dotnet restore
dotnet build
```

### 2 — Add your API key

`appsettings.example.json` is the full configuration template. Copy it to `appsettings.Local.json` — **this file is gitignored and will never be committed** — then fill in your real API key:

```bash
cp src/LlmModelProxy.Api/appsettings.example.json \
   src/LlmModelProxy.Api/appsettings.Local.json
```

Open `appsettings.Local.json` and replace the placeholder:

```json
{
  "Proxy": {
    "Providers": {
      "OpenAI": {
        "ApiKey": "sk-proj-YOUR_REAL_KEY_HERE"
      }
    }
  }
}
```

You only need the fields you want to override — everything else stays as-is from `appsettings.json`.  
The minimal version that gets you started is just the `ApiKey` field above.

> **Security**: never commit `appsettings.Local.json`. If a key was ever committed or appeared in logs, rotate it immediately.

### 3 — Run the proxy

From the **solution root** you must pass `--project` (the root folder has a `.sln` file, not a runnable project):

```bash
dotnet run --project src/LlmModelProxy.Api/LlmModelProxy.Api.csproj
```

Or `cd` into the API project first:

```bash
cd src/LlmModelProxy.Api
dotnet run
```

You should see the startup banner and `Listening on http://0.0.0.0:8766`.

Health check:

```bash
curl http://127.0.0.1:8766/health
```

### 4 — Point Claude CLI at the proxy

In a second terminal:

```bash
export ANTHROPIC_BASE_URL=http://127.0.0.1:8766
claude
```

---

## Running with Docker

### OpenAI backend (default)

```bash
# Copy the example env file and fill in your OpenAI key
cp .env.example .env
# Edit .env — set OPENAI_API_KEY=sk-proj-...

docker compose up --build
```

Docker Compose reads `.env` automatically and injects the values into the container. The proxy is available on **host port 8766**.

Audit logs are written to the `proxy_logs` Docker volume at `/logs/proxy-audit.log` inside the container.

### Local Ollama backend (no API key needed)

Spins up the proxy + an Ollama sidecar in one command:

```bash
cp .env.example .env   # OPENAI_API_KEY can stay blank for Ollama

docker compose --profile ollama up --build
```

Pull a model into Ollama (first time only):

```bash
docker exec llm-ollama ollama pull llama3:8b
```

Set `OLLAMA_MODEL=llama3:8b` in `.env` to change the default model.

---

## Configuration reference

Settings are layered in this order (later sources win):

1. `appsettings.json` — base defaults, safe to commit, no secrets
2. `appsettings.Local.json` — your local overrides and API keys, **gitignored**
3. Environment variables / `.env` — used by Docker

`appsettings.example.json` is the full documented template. Copy it to `appsettings.Local.json` to get started (see [step 2](#2--add-your-api-key) above).

### Pipeline


| Key                              | Default    | Description                                                                  |
| -------------------------------- | ---------- | ---------------------------------------------------------------------------- |
| `Proxy:Pipeline:ToolChoice`      | `required` | Forces the model to always call a tool. Keep `required` for autonomous mode. |
| `Proxy:Pipeline:InjectAgentHint` | `true`     | Prepends the 7-rule agent mandate to every system prompt.                    |
| `Proxy:Pipeline:SystemAppend`    | `""`       | Extra text appended after the agent mandate.                                 |


### OpenAI provider


| Key                                             | Default                  | Description                                                                                             |
| ----------------------------------------------- | ------------------------ | ------------------------------------------------------------------------------------------------------- |
| `Proxy:Providers:OpenAI:ApiKey`                 | *(required)*             | Your OpenAI API key. Set in `appsettings.Local.json` locally, or `OPENAI_API_KEY` in `.env` for Docker. |
| `Proxy:Providers:OpenAI:BaseUrl`                | `https://api.openai.com` | Change to point at OpenRouter, a local vLLM instance, etc.                                              |
| `Proxy:Providers:OpenAI:DefaultModel`           | `gpt-4o`                 | Model ID forwarded to the backend.                                                                      |
| `Proxy:Providers:OpenAI:MaxCompletionTokensCap` | `16384`                  | Hard cap on `max_completion_tokens`.                                                                    |
| `Proxy:Providers:OpenAI:SkipCompletionTokenCap` | `false`                  | Set `true` to pass the original token count unchanged.                                                  |


### Azure OpenAI provider *(optional)*


| Key                                          | Description                                 |
| -------------------------------------------- | ------------------------------------------- |
| `Proxy:Providers:AzureOpenAI:Endpoint`       | e.g. `https://my-resource.openai.azure.com` |
| `Proxy:Providers:AzureOpenAI:ApiKey`         | Azure resource API key                      |
| `Proxy:Providers:AzureOpenAI:DeploymentName` | Deployment name                             |
| `Proxy:Providers:AzureOpenAI:ApiVersion`     | Default `2024-02-01`                        |


### Ollama provider *(optional)*


| Key                                    | Default                        | Description                                    |
| -------------------------------------- | ------------------------------ | ---------------------------------------------- |
| `Proxy:Providers:Ollama:BaseUrl`       | `http://localhost:11434`       | Ollama server address                          |
| `Proxy:Providers:Ollama:DefaultModel`  | `llama3:8b`                    | Default model tag                              |
| `Proxy:Providers:Ollama:ModelPrefixes` | `llama,mistral,phi,gemma,qwen` | Comma-separated prefixes this provider accepts |


### Anthropic passthrough *(optional)*


| Key                                | Description                                    |
| ---------------------------------- | ---------------------------------------------- |
| `Proxy:Providers:Anthropic:ApiKey` | Real Anthropic API key for passthrough routing |


### Provider routing rules

Defined under `Proxy:ProviderRouting:Rules` in `appsettings.json` — first matching rule wins, `"*"` is a catch-all:

```json
"Rules": [
  { "ModelPrefix": "claude-", "Provider": "OpenAI" },
  { "ModelPrefix": "llama",   "Provider": "Ollama"  },
  { "ModelPrefix": "*",       "Provider": "OpenAI"  }
]
```

### Audit logging


| Key                      | Default           | Description                     |
| ------------------------ | ----------------- | ------------------------------- |
| `Proxy:Logging:Enabled`  | `true`            | Write per-request audit entries |
| `Proxy:Logging:FilePath` | `proxy-audit.log` | File path                       |


---

## Project structure

```
llm-model-proxy/
├── src/
│   ├── LlmModelProxy.Api/              # Web host — entry point
│   │   ├── Controllers/
│   │   │   ├── MessagesController.cs   # POST /v1/messages (streaming + buffered)
│   │   │   └── ModelsController.cs     # GET  /v1/models
│   │   ├── Middleware/
│   │   │   └── RequestLoggingMiddleware.cs
│   │   ├── appsettings.json            # Base config — safe to commit, no secrets
│   │   ├── appsettings.example.json    # ← Full template — copy to appsettings.Local.json
│   │   ├── appsettings.Local.json      # ← YOUR secrets go here (gitignored)
│   │   └── Program.cs
│   │
│   ├── LlmModelProxy.Core/             # No external NuGet deps
│   │   ├── Models/Anthropic/           # Request, Response, SSE events, Tool, Delta
│   │   ├── Interfaces/                 # ILlmProvider, IProviderRouter, IPipelineBehavior
│   │   ├── Pipeline/
│   │   │   ├── ProxyPipeline.cs
│   │   │   └── Behaviors/
│   │   │       ├── SystemPromptInjectionBehavior.cs   # Order=10
│   │   │       └── SyntheticToolInjectionBehavior.cs  # Order=20
│   │   ├── Routing/ProviderRouter.cs
│   │   └── Configuration/ProxyOptions.cs
│   │
│   ├── LlmModelProxy.Providers/        # One adapter per backend
│   │   ├── OpenAI/
│   │   │   ├── OpenAiProvider.cs
│   │   │   ├── OpenAiRequestTranslator.cs
│   │   │   ├── OpenAiResponseTranslator.cs
│   │   │   ├── OpenAiStreamTranslator.cs   # SSE state machine
│   │   │   └── Models/OpenAiModels.cs
│   │   ├── AzureOpenAI/
│   │   ├── Ollama/
│   │   ├── Anthropic/                  # Direct passthrough to real Anthropic
│   │   └── ProviderServiceExtensions.cs
│   │
│   └── LlmModelProxy.Infrastructure/   # Serilog audit logger + Polly resilience helpers
│
├── tests/
│   ├── LlmModelProxy.Unit.Tests/       # xUnit + FluentAssertions + NSubstitute
│   └── LlmModelProxy.Integration.Tests/
│
├── .env.example                        # Template — copy to .env for Docker
├── Dockerfile                          # Multi-stage: sdk:10.0 → aspnet:10.0
├── docker-compose.yml                  # Default (OpenAI) + --profile ollama
└── .gitignore
```

---

## Adding a new provider

1. Create `src/LlmModelProxy.Providers/MyProvider/MyProviderOptions.cs`
2. Implement `ILlmProvider` (`Name`, `SupportsModel`, `CompleteAsync`, `StreamAsync`)
3. Register the HTTP client and provider in `ProviderServiceExtensions.cs`
4. Add routing rules in `appsettings.json` under `Proxy:ProviderRouting:Rules`

---

## Security

- `appsettings.Local.json` and `.env` are gitignored — keep secrets in those files only.
- If a key was ever committed or appeared in logs, rotate it immediately.
- The Docker image runs as a non-root user (`appuser`).
- `--dangerously-skip-permissions` bypasses Claude CLI's own safety prompts — only use it in trusted local environments.

