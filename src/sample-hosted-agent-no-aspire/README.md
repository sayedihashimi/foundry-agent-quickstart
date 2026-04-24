# Seattle Hotel Booking Agent — Hosted Agent (No Aspire)

A .NET 10 console app that runs a hotel booking agent using the Azure AI AgentServer framework and the OpenAI Responses protocol. This is the standalone version — no Aspire, no AppHost, no ServiceDefaults. The agent can search for hotels, check availability, and book rooms in Seattle using natural language.

## Architecture

- **Azure.AI.AgentServer.AgentFramework** — Responses protocol server and agent hosting
- **Microsoft.Agents.AI** — `ChatClientAgent` abstraction
- **Microsoft.Extensions.AI** — `IChatClient`, tool/function calling
- **Azure.AI.OpenAI** — Azure OpenAI SDK with `DefaultAzureCredential`
- **Azure AI Foundry** — GPT-4o-mini model inference

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [Azure CLI](https://learn.microsoft.com/cli/azure/install-azure-cli)
- An Azure subscription with an AI Services resource and GPT-4o-mini deployment

## How This Differs from the Aspire Version

This app implements the same Foundry Hosted Agent as `sample-hosted-agent`, but without any Aspire dependencies.

| Aspect | This (No Aspire) | Aspire Version (`sample-hosted-agent`) |
|---|---|---|
| **Projects** | Single `.csproj` | 3 projects (Agent + AppHost + ServiceDefaults) |
| **Aspire packages** | None | ServiceDefaults, service discovery |
| **Run command** | `dotnet run` | `dotnet run --project AppHost` |
| **Complexity** | Minimal | More infrastructure |
| **Protocol** | Responses protocol | Responses protocol |
| **Port** | 8088 | 8088 |

## Run Locally

```powershell
$env:AZURE_OPENAI_ENDPOINT = "https://YOUR-RESOURCE.cognitiveservices.azure.com"
$env:AZURE_OPENAI_DEPLOYMENT_NAME = "chat"

dotnet run --project SeattleHotelAgent.Hosted.NoAspire
```

The agent starts on `http://[::]:8088`.

If your subscription tenant differs from your default login tenant:

```powershell
$env:AZURE_AI_TENANT_ID = "YOUR-TENANT-ID"
```

## Test

```powershell
# Health check
Invoke-RestMethod -Uri "http://localhost:8088/readiness"

# Chat via Responses protocol
$body = @{ input = "Find me a hotel in Ballard for 2 guests, under $200/night" } | ConvertTo-Json
$resp = Invoke-WebRequest -Uri "http://localhost:8088/responses" -Method Post -Body $body -ContentType "application/json" -TimeoutSec 120
($resp.Content | ConvertFrom-Json).output | ForEach-Object {
    if ($_.type -eq "message" -and $_.content) {
        $_.content | ForEach-Object { if ($_.text) { Write-Host $_.text } }
    }
}
```

## Endpoints

| Method | Path | Description |
|--------|------|-------------|
| `POST` | `/responses` | Chat via OpenAI Responses protocol |
| `GET` | `/readiness` | Health check |

## Agent Tools

The agent has access to these C# functions:

- **SearchHotels** — Filter by neighborhood, star rating, price, guest count
- **GetHotelDetails** — Get full details including room types and amenities
- **CheckAvailability** — Check room availability for specific dates
- **BookRoom** — Book a room and get a confirmation number

## Hotels

8 fake Seattle hotels across different neighborhoods:

| Hotel | Neighborhood | Stars | From |
|-------|-------------|-------|------|
| The Emerald Inn | Capitol Hill | 4★ | $159 |
| Pike Place Suites | Downtown / Pike Place | 5★ | $289 |
| Ballard Nordic Lodge | Ballard | 3★ | $129 |
| The Waterfront Grand | Waterfront | 5★ | $319 |
| Fremont Artisan Hotel | Fremont | 3★ | $149 |
| South Lake Union Tech Hotel | South Lake Union | 4★ | $179 |
| Pioneer Square Heritage Hotel | Pioneer Square | 3★ | $139 |
| Green Lake Nature Retreat | Green Lake | 4★ | $169 |

## Project Structure

```
src/sample-hosted-agent-no-aspire/
├── SeattleHotelAgent.Hosted.NoAspire.slnx
├── azure.yaml
├── infra/                                  # Bicep templates
│   ├── main.bicep
│   ├── main.parameters.json
│   └── resources.bicep
├── SeattleHotelAgent.Hosted.NoAspire/      # The agent
│   ├── Program.cs                          # Agent setup + RunAIAgentAsync
│   ├── agent.yaml                          # Foundry Agent Service manifest
│   ├── Dockerfile
│   ├── Models/HotelData.cs                 # Fake hotel data
│   ├── Models/HotelModels.cs               # Data models
│   └── Tools/HotelTools.cs                 # AI tool functions
└── SeattleHotelAgent.Hosted.NoAspire.Web/  # Razor Pages web client
    ├── Program.cs                          # Web app setup
    ├── Services/AgentService.cs            # Proxy to agent /responses
    ├── Pages/Index.cshtml                  # Chat page
    ├── Pages/Hotels/Index.cshtml           # Hotel listing
    └── Pages/Hotels/Detail.cshtml          # Hotel details
```

## Web Client

A Razor Pages web app is included for interacting with the agent through a browser instead of terminal commands. It provides a chat page, a hotel listing, and hotel detail pages.

To use it, start the agent first, then the web app in a second terminal:

```powershell
# Terminal 1 — start the agent
dotnet run --project SeattleHotelAgent.Hosted.NoAspire

# Terminal 2 — start the web client
dotnet run --project SeattleHotelAgent.Hosted.NoAspire.Web
```

Then open `http://localhost:5148` in your browser.

To point the web client at the deployed agent instead of localhost:

```powershell
$env:AgentEndpoint = "https://YOUR-APP.azurecontainerapps.io"
dotnet run --project SeattleHotelAgent.Hosted.NoAspire.Web
```

## Troubleshooting

**"AZURE_OPENAI_ENDPOINT is not set"**
→ Set the `AZURE_OPENAI_ENDPOINT` environment variable to your AI Services endpoint.

**Tenant mismatch error**
→ Set `AZURE_AI_TENANT_ID` to your subscription's tenant ID.

**TypeLoadException: UserInputRequestContent**
→ Package version mismatch. Use `Microsoft.Extensions.AI` version **10.3.0** (not 10.4.x) with `AgentFramework` beta.9. The newer beta.11 has the same issue — it compiles but fails at runtime.

**Capability host provisioning failure**
→ Deploying to Foundry Agent Service requires a capability host, which may fail on some subscriptions (especially MSDN). Use local testing as the primary workflow.

## Tutorial

See [quickstart-hosted-agent-no-aspire.md](../../tutorials/quickstart-hosted-agent-no-aspire.md) for a step-by-step guide.
