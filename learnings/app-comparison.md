# Comparison: Four Foundry Agent Approaches

This document compares four different approaches to building AI agents with Azure AI Foundry and .NET, based on the apps built during this project.

| App | Description | Location |
|---|---|---|
| **Hotel Agent (ACA)** | Single conversational agent with custom REST API, deployed to Azure Container Apps | `src/sample-aca/` |
| **Hotel Agent (Hosted, Aspire)** | Same agent using Foundry Agent Service + Responses protocol, with Aspire | `src/sample-hosted-agent/` |
| **Hotel Agent (Hosted, No Aspire)** | Same hosted agent without Aspire — single standalone console project | `src/sample-hosted-agent-no-aspire/` |
| **ClaimsAgent** | Multi-agent workflow with persistent Foundry audit agent, RAG, and streaming | [github.com/JeremyLikness/foundrytest](https://github.com/JeremyLikness/foundrytest) |

## Status: Local Development & Deployment

| Aspect | Hotel Agent (ACA) | Hotel Agent (Hosted, Aspire) | Hotel Agent (Hosted, No Aspire) | ClaimsAgent |
|---|---|---|---|---|
| **Works locally** | ✅ Yes | ✅ Yes | ✅ Yes | ✅ Yes |
| **Deployed to Azure** | ✅ ACA via `azd up` | ❌ Blocked (capability host) | ✅ ACA via `azd up` + manual Docker push | ❌ No deployment infra |
| **Run command** | `dotnet run --project AppHost` | `dotnet run --project AppHost` | `dotnet run` | `dotnet run --project AppHost` |
| **Local port** | Dynamic (Aspire assigns) | 8088 (AgentServer default) | 8088 (AgentServer default) | Dynamic (Aspire assigns) |

## Feature Comparison

| Aspect | Hotel Agent (ACA) | Hotel Agent (Hosted, Aspire) | Hotel Agent (Hosted, No Aspire) | ClaimsAgent |
|---|---|---|---|---|
| **Number of agents** | 1 | 1 | 1 | 5 (triage + 3 specialists + audit) |
| **Orchestration** | Direct `IChatClient` call | `ChatClientAgent` + `RunAIAgentAsync()` | `ChatClientAgent` + `RunAIAgentAsync()` | MAF handoff workflow (`AgentWorkflowBuilder`) |
| **Protocol** | Custom REST API | OpenAI Responses protocol | OpenAI Responses protocol | Custom REST API |
| **Endpoints** | `/api/chat`, `/api/hotels`, `/api/hotels/{id}` | `/responses`, `/readiness` | `/responses`, `/readiness` | `/api/claims/submit`, `/api/claims/stream`, etc. |
| **OpenAPI** | Yes (`/openapi/v1.json`) | No | No | No |
| **Streaming** | No | No | No | Yes (SSE via `/api/claims/stream`) |
| **Custom endpoints** | Yes — full control | No — protocol-constrained | No — protocol-constrained | Yes — full control |
| **Stateful sessions** | No (stateless per request) | No (stateless per request) | No (stateless per request) | Yes — Foundry persistent agent with server-side memory |
| **RAG / file search** | No | No | No | Yes — Foundry vector store + file search |
| **Multi-agent handoff** | No | No | No | Yes — MAF `transfer_to_*` tools |

## Architecture Comparison

| Aspect | Hotel Agent (ACA) | Hotel Agent (Hosted, Aspire) | Hotel Agent (Hosted, No Aspire) | ClaimsAgent |
|---|---|---|---|---|
| **Project type** | ASP.NET Core Web API | Console app (AgentServer) | Console app (AgentServer) | ASP.NET Core Web API |
| **Number of projects** | 3 (Api + AppHost + ServiceDefaults) | 3 (Agent + AppHost + ServiceDefaults) | 1 (just the console app) | 3 (Api + AppHost + ServiceDefaults) |
| **Uses Aspire** | Yes | Yes | **No** | Yes |
| **HTTP server** | Kestrel via `WebApplication` | Kestrel via `RunAIAgentAsync()` (port 8088) | Kestrel via `RunAIAgentAsync()` (port 8088) | Kestrel via `WebApplication` |
| **AI client** | `AzureOpenAIClient` → `IChatClient` | `AzureOpenAIClient` → `IChatClient` → `ChatClientAgent` | `AzureOpenAIClient` → `IChatClient` → `ChatClientAgent` | `AzureOpenAIClient` → `IChatClient` + `AIProjectClient` |
| **Tool registration** | `AIFunctionFactory` → `ChatOptions.Tools` | `AIFunctionFactory` → `ChatClientAgent` constructor | `AIFunctionFactory` → `ChatClientAgent` constructor | `AIFunctionFactory` → `ChatClientAgent` constructor |
| **Agent framework** | None (MEAI only) | MAF `ChatClientAgent` + AgentServer SDK | MAF `ChatClientAgent` + AgentServer SDK | MAF `ChatClientAgent` + Workflows + Foundry Agents |

## Package Comparison

| Package | Hotel Agent (ACA) | Hotel Agent (Hosted, Aspire) | Hotel Agent (Hosted, No Aspire) | ClaimsAgent |
|---|---|---|---|---|
| `Microsoft.Extensions.AI` | ✅ 10.4.1 | ✅ 10.3.0 | ✅ 10.3.0 | ✅ 10.4.1 |
| `Microsoft.Extensions.AI.OpenAI` | ✅ 10.4.1 | ✅ 10.3.0 | ✅ 10.3.0 | ✅ 10.4.1 |
| `Azure.AI.OpenAI` | ✅ 2.9.0-beta.1 | ✅ 2.5.0-beta.1 | ✅ 2.5.0-beta.1 | ✅ 2.1.0 |
| `Azure.Identity` | ✅ 1.20.0 | ✅ 1.17.0 | ✅ 1.20.0 | ✅ 1.20.0 |
| `Azure.AI.Extensions.OpenAI` | ❌ | ❌ | ❌ | ✅ 2.0.0 |
| `Microsoft.Agents.AI` | ✅ 1.0.0 | ✅ rc1 | ✅ rc1 (transitive) | ✅ 1.0.0 |
| `Microsoft.Agents.AI.OpenAI` | ❌ | ✅ rc1 | ✅ rc1 | ✅ 1.0.0 |
| `Microsoft.Agents.AI.Workflows` | ❌ | ❌ | ❌ | ✅ 1.0.0 |
| `Microsoft.Agents.AI.Foundry` | ❌ | ❌ | ❌ | ✅ 1.0.0 |
| `Azure.AI.AgentServer.AgentFramework` | ❌ | ✅ beta.9 | ✅ beta.9 | ❌ |
| `Azure.AI.Projects` | ❌ | ❌ | ❌ | ✅ 2.0.0 |
| `Azure.AI.Projects.Agents` | ❌ | ❌ | ❌ | ✅ 2.0.0 |
| `Aspire.Azure.AI.Inference` | ✅ | ❌ | ❌ | ❌ |
| Aspire ServiceDefaults | ✅ | ✅ | ❌ | ✅ |

> **Note:** The Hosted Agent samples (both Aspire and No Aspire) must use MEAI 10.3.0 due to AgentFramework beta.9/beta.11 runtime incompatibility with 10.4.x. See [Key Issues](#key-issues-discovered) below.

## Deployment Comparison

| Aspect | Hotel Agent (ACA) | Hotel Agent (Hosted, Aspire) | Hotel Agent (Hosted, No Aspire) | ClaimsAgent |
|---|---|---|---|---|
| **Deploy target** | Azure Container Apps | Foundry Agent Service | Azure Container Apps | None (local only) |
| **Deploy command** | `azd up` | `azd deploy` (via `azure.ai.agents` ext) | `azd up` + manual Docker push | Manual ARM deploy |
| **Deploy status** | ✅ Working | ❌ Blocked (capability host failure) | ✅ Working | ❌ No deploy infra |
| **Infra format** | Bicep (AI Services + ACA + ACR) | Aspire-generated Bicep | Bicep (AI Services + ACA + ACR) | ARM JSON template (`foundry.json`) |
| **Docker required** | Yes (for `azd up`) | Yes (for container build) | Yes (manual Docker build) | No |
| **Config method** | Connection string env var | `AZURE_OPENAI_ENDPOINT` env var | `AZURE_OPENAI_ENDPOINT` env var | `appsettings.json` or `AZURE_AI_PROJECT_ENDPOINT` env var |

## Configuration Comparison

| Aspect | Hotel Agent (ACA) | Hotel Agent (Hosted, Aspire) | Hotel Agent (Hosted, No Aspire) | ClaimsAgent |
|---|---|---|---|---|
| **Endpoint type** | Base endpoint (`https://xxx.cognitiveservices.azure.com`) | Base endpoint | Base endpoint | Project endpoint (`https://xxx.services.ai.azure.com/api/projects/name`) |
| **Auth** | `DefaultAzureCredential` with optional TenantId | `DefaultAzureCredential` with optional TenantId | `DefaultAzureCredential` with optional TenantId | `DefaultAzureCredential` |
| **Config source** | `ConnectionStrings:chat` or `AzureAI:Endpoint` | `AZURE_OPENAI_ENDPOINT` env var | `AZURE_OPENAI_ENDPOINT` env var | `AzureAI:ProjectEndpoint` in appsettings |

## When to Use Each Approach

### Hotel Agent (ACA) — Custom REST API on Container Apps
**Best for:** Apps that need custom endpoints, OpenAPI docs, and full control over the HTTP API. Good for web frontends or APIs consumed by other services.

**Pros:**
- Full control over HTTP endpoints, request/response shapes, and middleware
- OpenAPI document generation for client SDK generation and documentation
- Proven deployment path to ACA via `azd up`
- Can use latest MEAI and SDK versions (no AgentFramework version lock)
- Aspire dashboard for local development (traces, logs, metrics)

**Cons:**
- Requires Aspire knowledge (3 projects to manage)
- More boilerplate than the hosted agent approach
- Not compatible with Foundry playground or OpenAI-compatible clients
- No built-in protocol — you define and maintain the API contract

### Hotel Agent (Hosted, Aspire) — Foundry Agent Service with Aspire
**Best for:** Agents that should be callable via the standard OpenAI Responses protocol and may eventually deploy to Foundry Agent Service. Good for integration with Foundry playground, other agents, and OpenAI-compatible clients.

**Pros:**
- Standard OpenAI Responses protocol — interoperable with any compatible client
- Aspire dashboard for local development
- Minimal HTTP code — `RunAIAgentAsync()` handles everything
- Designed for Foundry Agent Service deployment (when capability host works)

**Cons:**
- MEAI version locked to 10.3.0 due to AgentFramework compatibility
- Deployment to Foundry Agent Service blocked by capability host provisioning issues
- No custom endpoints — constrained to the Responses protocol
- 3 projects to manage (Agent + AppHost + ServiceDefaults)
- Beta/RC packages with version conflicts

### Hotel Agent (Hosted, No Aspire) — Foundry Agent Service without Aspire
**Best for:** Developers who want the Hosted Agent / Responses protocol pattern with the simplest possible setup — a single project, no Aspire overhead.

**Pros:**
- Simplest setup — single `.csproj`, no Aspire knowledge required
- Same Responses protocol as the Aspire version
- Fastest to get running locally (`dotnet run` and done)
- Successfully deployed to ACA as a container
- Good starting point for learning the AgentServer framework

**Cons:**
- MEAI version locked to 10.3.0 due to AgentFramework compatibility
- No Aspire dashboard (no built-in traces, logs, metrics viewer)
- No custom endpoints — constrained to the Responses protocol
- No service discovery or resilience patterns (no ServiceDefaults)
- Deployment requires manual Docker build and push (pack CLI has caching issues)

### ClaimsAgent — Multi-Agent Workflow with Foundry Persistence
**Best for:** Complex domain-specific workflows with multiple specialized agents, persistent memory, RAG, and streaming. Good for enterprise scenarios where different agents handle different aspects of a process.

**Pros:**
- Most feature-rich — multi-agent handoff, RAG, persistent memory, streaming
- Demonstrates real-world enterprise pattern (triage → specialist → audit)
- Foundry persistent agent with server-side session memory
- File search via Foundry vector store (auto-chunking, auto-embedding)
- SSE streaming for real-time progress
- Uses latest stable package versions (MEAI 10.4.1, MAF 1.0.0)
- Full control over REST endpoints

**Cons:**
- Most complex — 5 agents, multiple orchestration patterns, hybrid local/Foundry architecture
- Requires a Foundry Project (not just AI Services) — more Azure resources
- No containerized deployment infra (ARM template for resources, but no Dockerfile or azure.yaml)
- In-memory claim storage (lost on restart)
- Steeper learning curve — combines MAF, MEAI, Foundry Agents, and Aspire

## Key Issues Discovered

### 1. AgentFramework Package Version Incompatibility (HIGH)
`Azure.AI.AgentServer.AgentFramework` beta.9 and beta.11 are tightly coupled to specific MEAI versions. Both compile successfully with MEAI 10.4.x but fail at runtime with `TypeLoadException` for `Microsoft.Extensions.AI.UserInputRequestContent` — a type that does not exist in any public MEAI release. Only MEAI 10.3.0 works with beta.9 at runtime. This was confirmed during the `sample-hosted-agent-no-aspire` deployment: the app built and started fine with beta.11 + MEAI 10.4.1, but returned a `server_error` on every `/responses` request until downgraded.

### 2. Foundry Capability Host Provisioning Failure (HIGH)
The capability host (`capabilityHosts@2025-10-01-preview`) fails to provision on MSDN subscriptions with VNet errors, even with `enablePublicHostingEnvironment: true`. This blocks all Foundry Agent Service deployments. The `sample-hosted-agent-no-aspire` app works around this by deploying to ACA instead.

### 3. Aspire `AddConnectionString` + azd Deploy Conflict (MEDIUM)
Aspire's `AddConnectionString()` creates a `securedParameter` dependency in azd deploy templates that doesn't work with custom Bicep. Workaround: skip `AddConnectionString` and configure env vars post-deploy.

### 4. DefaultAzureCredential Tenant Mismatch (MEDIUM)
`DefaultAzureCredential` picks the wrong tenant in multi-tenant environments (e.g., Microsoft corp tenant vs MSDN tenant). Must specify `TenantId` explicitly via `AzureAI:TenantId` config or `AZURE_AI_TENANT_ID` env var.

### 5. AI Services Custom Domain Required (LOW)
Without `--custom-domain` on `az cognitiveservices account create`, the endpoint returns a regional URL that `AzureOpenAIClient` can't use. Must set a custom domain to get a `xxx.cognitiveservices.azure.com` endpoint.

### 6. azd `pack` CLI Caching (LOW)
When using `azd deploy`, the pack CLI (Cloud Native Buildpacks) can cache previous build layers. After changing package versions in the `.csproj`, the deployed container may still use old assemblies. Workaround: build the Docker image manually with `docker build --no-cache` and push to ACR directly.
