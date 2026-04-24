# Learnings: Building an Azure AI Foundry Agent with Aspire

These learnings were captured during the development of the Seattle Hotel Booking Agent.
They should be used when writing the Quick Start tutorial.

## 1. Aspire Foundry Integration

### Two Package Approaches
- **`Aspire.Hosting.Foundry`** (hosting) — Use in the AppHost to declare Foundry resources and model deployments. Provides a strongly-typed `FoundryModel` catalog (e.g., `FoundryModel.OpenAI.Gpt4oMini`).
- **`Aspire.Azure.AI.Inference`** (client) — Use in API projects for DI-friendly chat client registration.
- ⚠️ There's also an older `Aspire.Hosting.Azure.AIFoundry` package — use the newer `Aspire.Hosting.Foundry` instead.

### Local Development: Use Connection Strings
For local development with **existing** Azure resources, use the connection string pattern in AppHost:

```csharp
var chat = builder.AddConnectionString("chat");
builder.AddProject<Projects.MyApi>("api")
    .WithReference(chat);
```

Set the connection string via user secrets:
```
dotnet user-secrets set "ConnectionStrings:chat" "Endpoint=https://YOUR-RESOURCE.cognitiveservices.azure.com;DeploymentId=chat" --project AppHost
```

The `AddFoundry().AddDeployment()` pattern works for provisioning but blocks the app from starting while it resolves Azure resources.

### Provisioning: Use AddFoundry for azd
For `azd up` provisioning, use the full Foundry integration:

```csharp
var foundry = builder.AddFoundry("foundry");
var chat = foundry.AddDeployment("chat", FoundryModel.OpenAI.Gpt4oMini);
```

This generates Bicep and provisions the Foundry resource + model deployment automatically.

## 2. AzureOpenAIClient vs. Aspire Client

### Proven Pattern: Direct AzureOpenAIClient
The most reliable approach (used by ClaimsAgent reference project):

```csharp
var chatClient = new AzureOpenAIClient(new Uri(endpoint), credential)
    .GetChatClient(deploymentName)
    .AsIChatClient();

builder.Services.AddChatClient(chatClient)
    .UseFunctionInvocation()
    .UseOpenTelemetry(sourceName: "MyAgent");
```

**Key packages:**
- `Azure.AI.OpenAI` — Azure-specific OpenAI client
- `Azure.Identity` — `DefaultAzureCredential` for keyless auth
- `Microsoft.Extensions.AI.OpenAI` — `.AsIChatClient()` extension
- `Microsoft.Extensions.AI` — `IChatClient`, `AIFunctionFactory`, `ChatMessage`

## 3. Authentication Gotchas

### Multi-Tenant DefaultAzureCredential
If your Azure subscription is in a different tenant than your default login (common for MSDN/personal subscriptions), `DefaultAzureCredential` will fail with:

> "Tenant provided in token does not match resource token"

**Fix:** Specify the tenant ID:
```csharp
var credential = new DefaultAzureCredential(new DefaultAzureCredentialOptions
{
    TenantId = builder.Configuration["AzureAI:TenantId"]
});
```

### How to Find Your Tenant ID
```bash
az account show --query tenantId -o tsv
```

## 4. Foundry Endpoint Configuration

### Endpoint vs. Project Endpoint
- **Foundry endpoint** (for AzureOpenAIClient): `https://YOUR-RESOURCE.cognitiveservices.azure.com`
- **Project endpoint** (for Azure.AI.Projects): `https://YOUR-RESOURCE.services.ai.azure.com/api/projects/PROJECT-NAME`
- AzureOpenAIClient needs the **base host only** — strip any path

### Deployment Names
- The model deployment name (e.g., `chat`) is NOT the model name (e.g., `gpt-4o-mini`)
- When using `azd`, the deployment name matches what you set in `AddDeployment("chat", ...)`
- Connection string uses `DeploymentId=chat` (the deployment name, not model name)

## 5. Tool/Function Calling Pattern

### Define Tools as Static C# Methods
```csharp
[Description("Search for hotels by criteria")]
public static string SearchHotels(
    [Description("Neighborhood to filter by")] string? neighborhood = null)
{
    // Return string results
}
```

### Register Tools with AIFunctionFactory
```csharp
builder.Services.AddSingleton<IList<AITool>>(
[
    AIFunctionFactory.Create(HotelTools.SearchHotels),
    AIFunctionFactory.Create(HotelTools.GetHotelDetails),
]);
```

### Pass Tools in Chat Options
```csharp
var response = await chatClient.GetResponseAsync(
    messages,
    new() { Tools = [.. tools] });
```

### Important: Include IDs in Tool Output
When tools return search results, always include IDs so the model can chain subsequent calls:
```
- [ID: ballard-lodge] Ballard Nordic Lodge (3★) — from $129/night
```
Without IDs, the model can't call `GetHotelDetails(hotelId)` or `BookRoom(hotelId, ...)`.

## 6. Azure Resource Provisioning

### Using azd with Aspire
```bash
cd src/sample-aca
azd init --from-code              # Detects Aspire project
azd env set AZURE_LOCATION eastus2
azd up                            # Provisions + deploys
```

### What Gets Provisioned
- Azure Cognitive Services (AI Services) account
- Model deployment (gpt-4o-mini)
- Container Registry (for deployment)
- Container Apps Environment
- Log Analytics workspace

### Manual Provisioning (alternative)
```bash
az group create --name rg-hotel-agent --location eastus2
az cognitiveservices account create --name my-foundry --resource-group rg-hotel-agent --location eastus2 --kind AIServices --sku S0
az cognitiveservices account deployment create --name my-foundry --resource-group rg-hotel-agent --deployment-name chat --model-name gpt-4o-mini --model-version 2024-07-18 --model-format OpenAI --sku-capacity 1 --sku-name GlobalStandard
```

## 7. Project Structure

```
SeattleHotelAgent/
├── SeattleHotelAgent.sln
├── azure.yaml                          # azd configuration
├── SeattleHotelAgent.AppHost/          # Aspire orchestrator
│   ├── AppHost.cs                      # Resource declarations
│   └── appsettings.json
├── SeattleHotelAgent.ServiceDefaults/  # Shared Aspire config
│   └── Extensions.cs                  # OpenTelemetry, health checks
└── SeattleHotelAgent.Api/             # Hotel booking API
    ├── Program.cs                      # DI, chat client, endpoints
    ├── Models/
    │   ├── HotelModels.cs             # Hotel, Room, Booking records
    │   └── HotelData.cs              # Fake Seattle hotel data
    └── Tools/
        └── HotelTools.cs             # AI tool functions
```

## 9. Deployment Gotchas

### Aspire AddConnectionString + azd Deploy Conflict
When using `AddConnectionString("chat")` in the AppHost, `azd` generates a deploy template that expects a `securedParameter("chat")` in the Bicep deployment. This creates a dependency that is hard to wire with custom Bicep.

**Workaround:** Don't use `AddConnectionString` in the AppHost for deployed scenarios. Instead:
1. Deploy the container app without the connection string
2. Configure the connection string as an environment variable on the container app post-deployment using `az containerapp update --set-env-vars`

### Foundry Capability Host Error
When using `Aspire.Hosting.Foundry` with `AddFoundry().AddDeployment()` in publish mode, Aspire may try to create a "Foundry capability host" which requires VNet configuration. This fails with: `"The environment network configuration is invalid"`.

**Workaround:** Provision the Foundry resource via custom Bicep or `az` CLI instead of through the Aspire Foundry hosting integration.

### Container App Managed Identity for Foundry Auth
The deployed container app needs a managed identity to authenticate with the Foundry endpoint. Steps:
1. Assign a user-assigned managed identity to the container app
2. Grant the identity "Cognitive Services User" role on the AI Services resource
3. Set `AZURE_CLIENT_ID` env var on the container app to the identity's client ID so `DefaultAzureCredential` uses it


- `ChatResponse` exists in both `Microsoft.Extensions.AI` and your models — rename yours to `AgentResponse` or similar.
- `ChatRequest` has no conflicts in MEAI but might in other libraries.

