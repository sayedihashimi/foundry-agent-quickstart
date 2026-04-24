# Build a Foundry Hosted Agent Without Aspire

In this tutorial we will build a hotel booking agent that runs as a Foundry Hosted Agent using the OpenAI Responses protocol — without Aspire. This is a companion to the [Aspire-based hosted agent tutorial](quickstart-hosted-agent.md). It uses the same hotel data and tools, but as a single standalone console project with no AppHost or ServiceDefaults. The completed code for this tutorial can be found at [foundry-agent-quickstart](https://github.com/sayedihashimi/foundry-agent-quickstart).

In this tutorial we will cover the following.

- Creating a standalone .NET 10 console project
- Provisioning Azure AI Foundry resources
- Creating hotel data and AI tool functions
- Wiring up the agent with the AgentServer framework and Responses protocol
- Testing the agent locally

## Prerequisites

Before getting started, ensure you have the following installed.

| Prerequisite | Description |
|---|---|
| [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) | .NET 10 or later |
| [Azure CLI](https://learn.microsoft.com/cli/azure/install-azure-cli) | `az` command line tool |
| Azure subscription | An active Azure subscription |

You will also need to be logged in to Azure. Run the following command to sign in.

```
az login
```

## How this differs from the Aspire version

Before we start building, it is helpful to understand the key differences between this approach and the Aspire-based hosted agent.

| Aspect | Aspire Version | This (No Aspire) |
|---|---|---|
| **Projects** | 3 projects (Agent + AppHost + ServiceDefaults) | Single `.csproj` |
| **Run command** | `dotnet run --project AppHost` | `dotnet run` |
| **Aspire packages** | ServiceDefaults, service discovery | None |
| **Protocol** | Responses protocol | Responses protocol |
| **Port** | 8088 | 8088 |
| **Complexity** | More infrastructure, but with Aspire dashboard | Minimal setup |

The tradeoff is simplicity versus infrastructure. The Aspire version gives you a dashboard, service discovery, and health check infrastructure out of the box. This version is a single project that you can understand and run immediately.

## Provisioning Azure AI Foundry resources

Before we can use the AI agent, we need an Azure AI Foundry resource with a model deployment. If you have already completed the [ACA-based tutorial](quickstart-aca.md) or the [Aspire hosted agent tutorial](quickstart-hosted-agent.md), you can reuse the same Foundry resource. If not, run the following commands to create a resource group, an AI Services account, and a GPT-4o-mini deployment.

```
az group create --name rg-hotel-agent --location eastus2

az cognitiveservices account create \
  --name my-hotel-foundry \
  --resource-group rg-hotel-agent \
  --location eastus2 \
  --kind AIServices \
  --sku S0 \
  --custom-domain my-hotel-foundry

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

After provisioning, get the endpoint URL. We will need this later.

```
az cognitiveservices account show \
  --name my-hotel-foundry \
  --resource-group rg-hotel-agent \
  --query properties.endpoint -o tsv
```

You will also need to grant yourself the **Cognitive Services User** role so that `DefaultAzureCredential` can authenticate without API keys. First get your user object ID, then create the role assignment.

```
az ad signed-in-user show --query id -o tsv
```

Use the object ID from the output in the following command.

```
az role assignment create \
  --role "Cognitive Services User" \
  --assignee-object-id YOUR_OBJECT_ID \
  --assignee-principal-type User \
  --scope $(az cognitiveservices account show --name my-hotel-foundry --resource-group rg-hotel-agent --query id -o tsv)
```

## Getting started — creating the console project

Unlike the Aspire version which requires multiple projects, we only need a single console app. Open a terminal and run the following commands.

```
mkdir SeattleHotelAgent.Hosted.NoAspire
cd SeattleHotelAgent.Hosted.NoAspire
dotnet new console --framework net10.0 -n SeattleHotelAgent.Hosted.NoAspire -o SeattleHotelAgent.Hosted.NoAspire
dotnet new sln -n SeattleHotelAgent.Hosted.NoAspire
dotnet sln add SeattleHotelAgent.Hosted.NoAspire/SeattleHotelAgent.Hosted.NoAspire.csproj
```

This creates a single console project with a solution file. Now add the NuGet packages. The AgentServer framework provides the Responses protocol server, and Microsoft.Extensions.AI gives us the `IChatClient` abstraction with tool/function calling.

```
cd SeattleHotelAgent.Hosted.NoAspire
dotnet add package Azure.AI.AgentServer.AgentFramework --version 1.0.0-beta.9
dotnet add package Azure.AI.OpenAI --version 2.5.0-beta.1
dotnet add package Azure.Identity --version 1.20.0
dotnet add package Microsoft.Agents.AI.OpenAI --version 1.0.0-rc1
dotnet add package Microsoft.Extensions.AI --version 10.3.0
dotnet add package Microsoft.Extensions.AI.OpenAI --version 10.3.0
cd ..
```

> **Important:** The AgentServer framework has strict version requirements. `AgentFramework` beta.9 requires `Microsoft.Extensions.AI` 10.3.0 — using 10.4.x causes a runtime `TypeLoadException` for `UserInputRequestContent`. The newer beta.11 has the same issue. Always use the exact versions shown above.

Build the solution to ensure everything is configured correctly.

```
dotnet build
```

## Adding hotel data

Now we will add the data that the agent will use. Create a **Models** folder and a **Tools** folder in the project.

```
mkdir SeattleHotelAgent.Hosted.NoAspire/Models
mkdir SeattleHotelAgent.Hosted.NoAspire/Tools
```

First, add the model classes. Create a file named **HotelModels.cs** in the Models folder with the following content.

**Models/HotelModels.cs**

```csharp
namespace SeattleHotelAgent.Hosted.NoAspire.Models;

public record Hotel
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required string Address { get; init; }
    public required string Neighborhood { get; init; }
    public required double Rating { get; init; }
    public required int StarRating { get; init; }
    public required List<Room> Rooms { get; init; }
    public required List<string> Amenities { get; init; }
}

public record Room
{
    public required string Type { get; init; }
    public required string Description { get; init; }
    public required decimal PricePerNight { get; init; }
    public required int MaxGuests { get; init; }
    public required int AvailableCount { get; init; }
}

public record BookingRequest
{
    public required string HotelId { get; init; }
    public required string RoomType { get; init; }
    public required string GuestName { get; init; }
    public required DateOnly CheckIn { get; init; }
    public required DateOnly CheckOut { get; init; }
    public int Guests { get; init; } = 1;
}

public record Booking
{
    public required string ConfirmationNumber { get; init; }
    public required string HotelName { get; init; }
    public required string RoomType { get; init; }
    public required string GuestName { get; init; }
    public required DateOnly CheckIn { get; init; }
    public required DateOnly CheckOut { get; init; }
    public required decimal TotalPrice { get; init; }
    public required int Nights { get; init; }
}
```

Next, create **HotelData.cs** in the Models folder with fake Seattle hotel data. For this tutorial the data is stored in-memory. Below is a shortened version showing two of the eight hotels. The full file with all eight hotels is available in the [source repository](https://github.com/sayedihashimi/foundry-agent-quickstart/blob/main/src/sample-hosted-agent-no-aspire/SeattleHotelAgent.Hosted.NoAspire/Models/HotelData.cs).

**Models/HotelData.cs**

```csharp
namespace SeattleHotelAgent.Hosted.NoAspire.Models;

public static class HotelData
{
    public static readonly List<Hotel> Hotels =
    [
        new()
        {
            Id = "emerald-inn",
            Name = "The Emerald Inn",
            Description = "A cozy boutique hotel in Capitol Hill with locally sourced breakfast and city views.",
            Address = "1425 Broadway E, Seattle, WA 98102",
            Neighborhood = "Capitol Hill",
            Rating = 4.6,
            StarRating = 4,
            Rooms =
            [
                new() { Type = "Standard Queen", Description = "Queen bed with city view", PricePerNight = 159m, MaxGuests = 2, AvailableCount = 8 },
                new() { Type = "Deluxe King", Description = "King bed with panoramic views", PricePerNight = 229m, MaxGuests = 2, AvailableCount = 4 },
                new() { Type = "Suite", Description = "One-bedroom suite with kitchenette", PricePerNight = 349m, MaxGuests = 4, AvailableCount = 2 }
            ],
            Amenities = ["Free WiFi", "Complimentary Breakfast", "Rooftop Terrace", "Bike Rentals", "EV Charging"]
        },
        new()
        {
            Id = "ballard-lodge",
            Name = "Ballard Nordic Lodge",
            Description = "Scandinavian-inspired lodge near Ballard's breweries with a sauna and hygge-inspired rooms.",
            Address = "5300 Ballard Ave NW, Seattle, WA 98107",
            Neighborhood = "Ballard",
            Rating = 4.5,
            StarRating = 3,
            Rooms =
            [
                new() { Type = "Standard Double", Description = "Two double beds, Nordic theme", PricePerNight = 129m, MaxGuests = 4, AvailableCount = 10 },
                new() { Type = "Deluxe King", Description = "King bed with fireplace", PricePerNight = 189m, MaxGuests = 2, AvailableCount = 5 },
                new() { Type = "Family Suite", Description = "Two-room suite with bunk beds", PricePerNight = 269m, MaxGuests = 6, AvailableCount = 3 }
            ],
            Amenities = ["Free WiFi", "Sauna", "Free Parking", "Pet Friendly", "Brewery Tours"]
        },
        // ... 6 more hotels — see the full source file for all entries
    ];
}
```

## Creating AI tool functions

The agent uses C# functions as tools that the AI model can call. These tools allow the model to search hotels, check availability, and book rooms. Create a file named **HotelTools.cs** in the Tools folder with the following content.

**Tools/HotelTools.cs**

```csharp
using System.ComponentModel;
using SeattleHotelAgent.Hosted.NoAspire.Models;

namespace SeattleHotelAgent.Hosted.NoAspire.Tools;

public static class HotelTools
{
    [Description("Search for hotels in Seattle by neighborhood, star rating, price, and guest count.")]
    public static string SearchHotels(
        [Description("Optional neighborhood to filter by")] string? neighborhood = null,
        [Description("Minimum star rating (1-5)")] int? minStarRating = null,
        [Description("Maximum price per night in USD")] decimal? maxPricePerNight = null,
        [Description("Number of guests to accommodate")] int? guests = null)
    {
        var results = HotelData.Hotels.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(neighborhood))
            results = results.Where(h => h.Neighborhood.Contains(neighborhood, StringComparison.OrdinalIgnoreCase));
        if (minStarRating.HasValue)
            results = results.Where(h => h.StarRating >= minStarRating.Value);
        if (maxPricePerNight.HasValue)
            results = results.Where(h => h.Rooms.Any(r => r.PricePerNight <= maxPricePerNight.Value));
        if (guests.HasValue)
            results = results.Where(h => h.Rooms.Any(r => r.MaxGuests >= guests.Value && r.AvailableCount > 0));

        var hotels = results.ToList();
        if (hotels.Count == 0) return "No hotels found matching your criteria.";

        var lines = hotels.Select(h =>
        {
            var cheapest = h.Rooms.Min(r => r.PricePerNight);
            return $"- [ID: {h.Id}] {h.Name} ({h.StarRating}★, {h.Rating}/5.0) in {h.Neighborhood} — from ${cheapest}/night";
        });
        return $"Found {hotels.Count} hotel(s):\n{string.Join("\n", lines)}";
    }

    [Description("Get detailed information about a specific hotel including room types and amenities.")]
    public static string GetHotelDetails(
        [Description("The hotel ID (e.g., 'emerald-inn', 'ballard-lodge')")] string hotelId)
    {
        var hotel = HotelData.Hotels.FirstOrDefault(h => h.Id.Equals(hotelId, StringComparison.OrdinalIgnoreCase));
        if (hotel is null) return $"Hotel '{hotelId}' not found. Use SearchHotels to find available hotels.";

        var rooms = string.Join("\n", hotel.Rooms.Select(r =>
            $"  - {r.Type}: ${r.PricePerNight}/night (up to {r.MaxGuests} guests, {r.AvailableCount} available)"));
        return $"Hotel: {hotel.Name} ({hotel.StarRating}★)\nLocation: {hotel.Address}\n\nRooms:\n{rooms}\n\nAmenities: {string.Join(", ", hotel.Amenities)}";
    }

    [Description("Check room availability at a hotel for specific dates and guest count.")]
    public static string CheckAvailability(
        [Description("The hotel ID")] string hotelId,
        [Description("Check-in date (YYYY-MM-DD)")] string checkInDate,
        [Description("Check-out date (YYYY-MM-DD)")] string checkOutDate,
        [Description("Number of guests")] int guests = 1)
    {
        var hotel = HotelData.Hotels.FirstOrDefault(h => h.Id.Equals(hotelId, StringComparison.OrdinalIgnoreCase));
        if (hotel is null) return $"Hotel '{hotelId}' not found.";
        if (!DateOnly.TryParse(checkInDate, out var checkIn) || !DateOnly.TryParse(checkOutDate, out var checkOut))
            return "Invalid date format. Please use YYYY-MM-DD.";
        if (checkOut <= checkIn) return "Check-out date must be after check-in date.";

        var nights = checkOut.DayNumber - checkIn.DayNumber;
        var available = hotel.Rooms.Where(r => r.MaxGuests >= guests && r.AvailableCount > 0).ToList();
        if (available.Count == 0) return $"No rooms at {hotel.Name} for {guests} guest(s) on those dates.";

        var lines = available.Select(r => $"  - {r.Type}: ${r.PricePerNight}/night × {nights} nights = ${r.PricePerNight * nights} total");
        return $"Availability at {hotel.Name} ({checkIn:MMM d} → {checkOut:MMM d}, {nights} night(s)):\n{string.Join("\n", lines)}";
    }

    [Description("Book a hotel room and receive a confirmation number.")]
    public static string BookRoom(
        [Description("The hotel ID")] string hotelId,
        [Description("Room type (e.g., 'Standard Queen', 'Deluxe King')")] string roomType,
        [Description("Guest full name")] string guestName,
        [Description("Check-in date (YYYY-MM-DD)")] string checkInDate,
        [Description("Check-out date (YYYY-MM-DD)")] string checkOutDate)
    {
        var hotel = HotelData.Hotels.FirstOrDefault(h => h.Id.Equals(hotelId, StringComparison.OrdinalIgnoreCase));
        if (hotel is null) return $"Hotel '{hotelId}' not found.";
        var room = hotel.Rooms.FirstOrDefault(r => r.Type.Equals(roomType, StringComparison.OrdinalIgnoreCase));
        if (room is null) return $"Room type '{roomType}' not found at {hotel.Name}.";
        if (!DateOnly.TryParse(checkInDate, out var checkIn) || !DateOnly.TryParse(checkOutDate, out var checkOut))
            return "Invalid date format.";

        var nights = checkOut.DayNumber - checkIn.DayNumber;
        var total = room.PricePerNight * nights;
        var confirmation = $"SEA-{Guid.NewGuid().ToString()[..8].ToUpperInvariant()}";

        return $"✅ Booking Confirmed!\nConfirmation #: {confirmation}\nHotel: {hotel.Name}\nRoom: {room.Type}\nGuest: {guestName}\nCheck-in: {checkIn:ddd, MMM d, yyyy}\nCheck-out: {checkOut:ddd, MMM d, yyyy}\nTotal: ${total}";
    }
}
```

Each tool function is a `static` method decorated with a `[Description]` attribute. This is what the AI model uses to understand when and how to call each function. The parameters also have descriptions so the model knows what values to pass.

## Wiring up the agent in Program.cs

Now let's wire everything together. This is where the big difference from the Aspire version is most visible. Instead of `WebApplication.CreateBuilder()` with AppHost and ServiceDefaults, we create a `ChatClientAgent` directly and call `RunAIAgentAsync()`. Open **Program.cs** and replace the contents with the following code.

**Program.cs**

```csharp
using Azure.AI.AgentServer.AgentFramework.Extensions;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Azure.AI.OpenAI;
using Azure.Identity;
using SeattleHotelAgent.Hosted.NoAspire.Tools;

var openAiEndpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT")
    ?? throw new InvalidOperationException("AZURE_OPENAI_ENDPOINT is not set.");
var deploymentName = Environment.GetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT_NAME") ?? "chat";
var tenantId = Environment.GetEnvironmentVariable("AZURE_AI_TENANT_ID");

var credential = new DefaultAzureCredential(new DefaultAzureCredentialOptions
{
    TenantId = tenantId
});

// Register hotel tools for function calling
var tools = new AIFunction[]
{
    AIFunctionFactory.Create(HotelTools.SearchHotels),
    AIFunctionFactory.Create(HotelTools.GetHotelDetails),
    AIFunctionFactory.Create(HotelTools.CheckAvailability),
    AIFunctionFactory.Create(HotelTools.BookRoom)
};

var chatClient = new AzureOpenAIClient(new Uri(openAiEndpoint), credential)
    .GetChatClient(deploymentName)
    .AsIChatClient()
    .AsBuilder()
    .UseFunctionInvocation()
    .UseOpenTelemetry(sourceName: "SeattleHotelAgent", configure: cfg => cfg.EnableSensitiveData = true)
    .Build();

var agent = new ChatClientAgent(chatClient,
    name: "SeattleHotelConcierge",
    instructions: """
        You are the Seattle Hotel Concierge, a friendly and knowledgeable AI assistant that helps
        travelers find and book hotels in Seattle, Washington.

        Your capabilities:
        - Search for hotels by neighborhood, star rating, price, and guest count
        - Provide detailed information about specific hotels
        - Check room availability for specific dates
        - Book hotel rooms

        Guidelines:
        - Always be warm and welcoming — Seattle is a great city to visit!
        - When users ask vague questions, help narrow down their preferences
        - Suggest neighborhoods based on what they want to do (e.g., Pike Place for food lovers,
          Capitol Hill for nightlife, Ballard for breweries, Fremont for quirky arts)
        - Always confirm booking details before finalizing
        - Mention relevant amenities that match what the user seems to care about
        - If dates aren't provided, ask for them before checking availability
        """,
    tools: tools)
    .AsBuilder()
    .UseOpenTelemetry(sourceName: "SeattleHotelAgent", configure: cfg => cfg.EnableSensitiveData = true)
    .Build();

await agent.RunAIAgentAsync(telemetrySourceName: "SeattleHotelAgent");
```

Let's walk through the key parts of this file.

**Configuration.** The endpoint and deployment name are read from environment variables. `AZURE_OPENAI_ENDPOINT` is the base URL for your AI Services resource (e.g., `https://my-hotel-foundry.cognitiveservices.azure.com`). `AZURE_OPENAI_DEPLOYMENT_NAME` defaults to `chat` if not set.

**Authentication.** We use [DefaultAzureCredential](https://learn.microsoft.com/dotnet/azure/sdk/authentication/credential-chains#defaultazurecredential-overview) for keyless authentication. The optional `AZURE_AI_TENANT_ID` is needed when your subscription is in a different tenant than your default login.

**Chat client.** The `AzureOpenAIClient` is wrapped as an `IChatClient` using `.AsIChatClient()`, then enhanced with `.UseFunctionInvocation()` (auto-invokes tools when the model requests them) and `.UseOpenTelemetry()` (distributed tracing).

**Agent.** The `ChatClientAgent` bundles the chat client with a name, system instructions, and tool functions. The `.UseOpenTelemetry()` on the agent adds tracing at the agent level too.

**RunAIAgentAsync.** This single call starts a Kestrel HTTP server on port 8088, exposes the `/responses` endpoint (OpenAI Responses protocol) and `/readiness` (health check), and handles all request routing. There is no need for `WebApplication`, `MapGet`, or any ASP.NET Core middleware.

## Running the app locally

Set the environment variables for your Foundry resource. Replace the endpoint below with the value you got from the provisioning step earlier.

```powershell
$env:AZURE_OPENAI_ENDPOINT = "https://YOUR-RESOURCE.cognitiveservices.azure.com"
$env:AZURE_OPENAI_DEPLOYMENT_NAME = "chat"
```

If your subscription tenant differs from your default login tenant, also set the tenant ID.

```powershell
# Get your tenant ID
az account show --query tenantId -o tsv

# Set it
$env:AZURE_AI_TENANT_ID = "YOUR-TENANT-ID"
```

Run the agent.

```
dotnet run --project SeattleHotelAgent.Hosted.NoAspire
```

The agent starts and listens on `http://[::]:8088`. Open a new terminal window and test it.

```powershell
# Health check
Invoke-RestMethod -Uri "http://localhost:8088/readiness"
```

Now send a message via the Responses protocol.

```powershell
$body = @{ input = "Find me a hotel in Ballard for 2 guests, under $200/night" } | ConvertTo-Json
$resp = Invoke-WebRequest -Uri "http://localhost:8088/responses" -Method Post -Body $body -ContentType "application/json" -TimeoutSec 120
($resp.Content | ConvertFrom-Json).output | ForEach-Object {
    if ($_.type -eq "message" -and $_.content) {
        $_.content | ForEach-Object { if ($_.text) { Write-Host $_.text } }
    }
}
```

You should see the agent respond with hotel recommendations from the Ballard neighborhood. You can also try booking a room.

```powershell
$body = @{ input = "Book a Standard Double at ballard-lodge for Jane Doe from 2026-06-01 to 2026-06-03" } | ConvertTo-Json
$resp = Invoke-WebRequest -Uri "http://localhost:8088/responses" -Method Post -Body $body -ContentType "application/json" -TimeoutSec 120
($resp.Content | ConvertFrom-Json).output | ForEach-Object {
    if ($_.type -eq "message" -and $_.content) {
        $_.content | ForEach-Object { if ($_.text) { Write-Host $_.text } }
    }
}
```

The agent will call the `BookRoom` tool and return a confirmation number.

## Deployment notes

To deploy the agent to Azure, a Dockerfile and infrastructure templates are included in the project.

The `agent.yaml` file is the Foundry Agent Service manifest that describes the agent's name, protocols, and environment variables. The `Dockerfile` builds the project and exposes port 8088.

> **Note:** Deploying to Foundry Agent Service requires a capability host resource, which may fail to provision on some subscriptions (particularly MSDN subscriptions). If you encounter capability host errors, use local testing as your primary workflow. The ACA-based approach (`sample-aca`) offers a working Azure deployment alternative.

## Summary

In this tutorial we built a Foundry Hosted Agent using the Responses protocol — without any Aspire dependencies. The entire app is a single console project that sets up a `ChatClientAgent` and calls `RunAIAgentAsync()`.

The key technologies we used are summarized below.

| Technology | Purpose |
|---|---|
| [Azure AI AgentServer](https://learn.microsoft.com/dotnet/api/overview/azure/ai.agentserver.agentframework-readme) | Responses protocol server and agent hosting |
| [Microsoft Agent Framework](https://github.com/microsoft/agent-framework) | `ChatClientAgent` abstraction |
| [Microsoft.Extensions.AI](https://learn.microsoft.com/dotnet/ai/ai-extensions) | `IChatClient`, tool/function calling |
| [Azure.AI.OpenAI](https://www.nuget.org/packages/Azure.AI.OpenAI) | Azure OpenAI SDK |
| [DefaultAzureCredential](https://learn.microsoft.com/dotnet/azure/sdk/authentication) | Keyless authentication |

Compared to the Aspire version, this approach trades the Aspire dashboard and service discovery for a simpler, single-project setup. Both versions produce the same agent behavior — the choice comes down to whether you want the Aspire infrastructure or a minimal standalone project.
