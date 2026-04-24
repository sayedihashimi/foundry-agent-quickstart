# Seattle Hotel Booking Agent

A .NET 10 Aspire app with an Azure AI Foundry-powered hotel booking assistant for Seattle. The agent can search hotels, check availability, and book rooms using natural language.

## Architecture

- **Microsoft.Extensions.AI (MEAI)** — Model abstraction (`IChatClient`)
- **Azure.AI.OpenAI** — Azure OpenAI SDK with `DefaultAzureCredential`
- **Azure AI Foundry** — GPT-4o-mini model inference
- **Aspire** — Service orchestration, OpenTelemetry, health checks
- **Local C# tools** — Hotel search, availability, booking via `AIFunctionFactory`

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [Azure CLI](https://learn.microsoft.com/cli/azure/install-azure-cli) (`az`)
- [Azure Developer CLI](https://learn.microsoft.com/azure/developer/azure-developer-cli/install-azd) (`azd`)
- An Azure subscription

## Quick Start

### 1. Provision Azure Resources

```bash
az login
cd src/sample-aca

# Option A: Using azd (auto-provisions everything)
azd init --from-code
azd env set AZURE_LOCATION eastus2
azd provision

# Option B: Manual provisioning
az group create --name rg-hotel-agent --location eastus2

az cognitiveservices account create \
  --name my-hotel-foundry \
  --resource-group rg-hotel-agent \
  --location eastus2 \
  --kind AIServices \
  --sku S0

az cognitiveservices account deployment create \
  --name my-hotel-foundry \
  --resource-group rg-hotel-agent \
  --deployment-name chat \
  --model-name gpt-4o-mini \
  --model-version 2024-07-18 \
  --model-format OpenAI \
  --sku-capacity 1 \
  --sku-name GlobalStandard
```

### 2. Configure the Connection

Get your Foundry endpoint:
```bash
az cognitiveservices account show \
  --name my-hotel-foundry \
  --resource-group rg-hotel-agent \
  --query properties.endpoint -o tsv
```

Set it via user secrets:
```bash
dotnet user-secrets set "ConnectionStrings:chat" \
  "Endpoint=https://YOUR-RESOURCE.cognitiveservices.azure.com;DeploymentId=chat" \
  --project SeattleHotelAgent.AppHost
```

If your subscription tenant differs from your default login tenant:
```bash
# Find your tenant ID
az account show --query tenantId -o tsv

# Set it in appsettings.json or environment
export AzureAI__TenantId=YOUR-TENANT-ID
```

### 3. Run Locally

```bash
cd src/sample-aca
dotnet run --project SeattleHotelAgent.AppHost
```

The Aspire dashboard opens at `https://localhost:17005`. The API runs on a dynamically assigned port (check the dashboard for the URL).

### 4. Test the API

```bash
# List hotels
curl http://localhost:PORT/api/hotels

# Chat with the agent
curl -X POST http://localhost:PORT/api/chat \
  -H "Content-Type: application/json" \
  -d '{"message": "Find me a hotel near Pike Place Market for 2 guests under $300/night"}'

# Book a room
curl -X POST http://localhost:PORT/api/chat \
  -H "Content-Type: application/json" \
  -d '{"message": "Book a Studio Suite at pike-place-suites for John Smith, 2026-06-01 to 2026-06-03"}'
```

## API Endpoints

| Method | Path | Description |
|--------|------|-------------|
| `POST` | `/api/chat` | Chat with the hotel concierge agent |
| `GET` | `/api/hotels` | List all Seattle hotels |
| `GET` | `/api/hotels/{id}` | Get details for a specific hotel |
| `GET` | `/health` | Health check |

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
src/sample-aca/
├── SeattleHotelAgent.sln
├── azure.yaml
├── SeattleHotelAgent.AppHost/          # Aspire orchestrator
├── SeattleHotelAgent.ServiceDefaults/  # OpenTelemetry, health checks
└── SeattleHotelAgent.Api/             # Hotel booking API
    ├── Program.cs                      # DI + endpoints
    ├── Models/HotelData.cs            # Fake hotel data
    ├── Models/HotelModels.cs          # Data models
    └── Tools/HotelTools.cs           # AI tool functions
```

## Troubleshooting

**"Tenant provided in token does not match resource token"**
→ Set `AzureAI:TenantId` to your subscription's tenant ID (see step 2).

**Chat endpoint times out**
→ Verify the deployment name in your connection string matches the Azure deployment (usually `chat`, not `gpt-4o-mini`).

**Aspire app starts but API doesn't launch**
→ If using `AddFoundry().AddDeployment()` pattern, Aspire blocks while provisioning. Switch to `AddConnectionString("chat")` for local dev.

