# Seattle Hotel Booking Agent — Foundry Hosted Agent

A .NET 10 Aspire app that runs a hotel booking agent as a [Microsoft Foundry](https://learn.microsoft.com/azure/foundry/what-is-foundry) hosted agent using the [Responses protocol](https://platform.openai.com/docs/api-reference/responses). The agent can search for hotels, check availability, and book rooms in Seattle using natural language with local C# tool functions.

<!-- daily-build-note-start -->
> **Note:** This sample currently requires a daily build of the Aspire CLI. Install it with:
>
> ```powershell
> iex "& { $(irm https://aspire.dev/install.ps1) } -Quality dev"
> ```
<!-- daily-build-note-end -->

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [Aspire CLI](https://aspire.dev/get-started/what-is-aspire/) (daily build — see note above)
- [Azure CLI](https://learn.microsoft.com/cli/azure/install-azure-cli)
- [Docker Desktop](https://docs.docker.com/desktop/)
- An Azure subscription

You do not need to create any Azure resources ahead of time. Aspire provisions the AI Foundry account, project, model deployment, and container registry for you.

## Architecture

| Component | Purpose |
|---|---|
| [Aspire.Hosting.Foundry](https://aspire.dev/) | Declarative Foundry resource provisioning and hosted agent deployment |
| [Microsoft.Agents.AI.Foundry.Hosting](https://aka.ms/agent-framework) | Responses protocol hosting (`AddFoundryResponses` / `MapFoundryResponses`) |
| [Azure.AI.Projects](https://www.nuget.org/packages/Azure.AI.Projects) | Azure AI Foundry project client |
| [DefaultAzureCredential](https://learn.microsoft.com/dotnet/azure/sdk/authentication) | Keyless authentication |

## Run Locally

1. Log in to Azure.

    ```powershell
    az login
    ```

2. Start the app with Aspire.

    ```powershell
    aspire run --apphost SeattleHotelAgent.Hosted.AppHost/SeattleHotelAgent.Hosted.AppHost.csproj
    ```

3. Open the Aspire dashboard URL shown in the output. The dashboard will prompt you to configure your Azure tenant, subscription, and resource group. Select the appropriate values and submit. Aspire will provision the AI Foundry resources — this takes a few minutes on first run.

4. Once the `hotel-agent` resource shows **Running** in the dashboard, test it in a separate terminal.

    ```powershell
    $r = Invoke-RestMethod -Uri "http://localhost:8088/responses" -Method POST `
        -Body '{"input":"Find me a hotel in Ballard under $200 per night for 2 guests"}' `
        -ContentType "application/json" -TimeoutSec 60
    $r.output | Where-Object { $_.type -eq "message" } | ForEach-Object { $_.content } | ForEach-Object { $_.text }
    ```

5. Stop the app with Ctrl+C.

## Deploy to Foundry

1. Make sure Docker Desktop is running, then deploy.

    ```powershell
    aspire deploy --apphost SeattleHotelAgent.Hosted.AppHost/SeattleHotelAgent.Hosted.AppHost.csproj
    ```

    Follow the prompts to select your Azure tenant, subscription, and resource group. The deploy takes 5–7 minutes on first run.

2. After deploy completes, go to the [Foundry portal](https://ai.azure.com), navigate to your project, and **start the agent version** under the Agents page. This is a manual step required after each deploy.

3. If you get a permissions error, grant the agent's managed identity the **Cognitive Services OpenAI User** role on the Foundry account.

    ```powershell
    az role assignment create --assignee <agent-principal-id> `
        --role "Cognitive Services OpenAI User" `
        --scope <foundry-account-resource-id>
    ```

## Test the Deployed Agent

```powershell
$token = az account get-access-token --resource "https://ai.azure.com" --query accessToken -o tsv
$endpoint = "https://<your-foundry-account>.services.ai.azure.com/api/projects/<your-project>"
$agentName = "<your-agent-name>"  # Find this via the Foundry portal or the list command below

$body = @{
    input = "Find me a hotel in Ballard under 200 dollars"
    agent_reference = @{ type = "agent_reference"; name = $agentName }
} | ConvertTo-Json -Depth 3

$r = Invoke-RestMethod -Uri "$endpoint/openai/v1/responses" `
    -Headers @{ "Authorization" = "Bearer $token"; "Content-Type" = "application/json" } `
    -Method POST -Body $body -TimeoutSec 90

$r.output | Where-Object { $_.type -eq "message" } | ForEach-Object { $_.content } | ForEach-Object { $_.text }
```

To list agents and find the agent name:

```powershell
$token = az account get-access-token --resource "https://ai.azure.com" --query accessToken -o tsv
Invoke-RestMethod -Uri "$endpoint/agents?api-version=v1" `
    -Headers @{ "Authorization" = "Bearer $token" } -Method GET
```

## Endpoints

| Method | Path | Description |
|--------|------|-------------|
| `POST` | `/responses` | Chat via OpenAI Responses protocol |
| `GET` | `/liveness` | Liveness health check |
| `GET` | `/readiness` | Readiness health check |

## Tutorial

For a full step-by-step walkthrough of building this app from scratch, see [quickstart-hosted-agent.md](../../tutorials/quickstart-hosted-agent.md).

