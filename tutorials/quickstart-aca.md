# Build an AI Hotel Booking Agent with Aspire and Azure AI Foundry

In this tutorial we will build an AI-powered hotel booking agent using C#, Aspire, and Azure AI Foundry. The agent can search for hotels, check availability, and book rooms in Seattle using natural language. To get started you will need the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0), the [Azure CLI](https://learn.microsoft.com/cli/azure/install-azure-cli), and the [Azure Developer CLI (azd)](https://learn.microsoft.com/azure/developer/azure-developer-cli/install-azd). The completed code for this tutorial can be found at [foundry-agent-quickstart](https://github.com/sayedihashimi/foundry-agent-quickstart).

In this tutorial we will cover the following.

- Creating an Aspire solution
- Provisioning Azure AI Foundry resources
- Creating hotel data and AI tool functions
- Wiring up the agent with Microsoft.Extensions.AI
- Adding REST API endpoints
- Testing the agent locally and in Azure

## Prerequisites

Before getting started, ensure you have the following installed.

| Prerequisite | Description |
|---|---|
| [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) | .NET 10 or later |
| [Azure CLI](https://learn.microsoft.com/cli/azure/install-azure-cli) | `az` command line tool |
| [Azure Developer CLI](https://learn.microsoft.com/azure/developer/azure-developer-cli/install-azd) | `azd` command line tool |
| Azure subscription | An active Azure subscription |

You will also need to be logged in to Azure. Run the following commands to sign in.

```
az login
azd auth login
```

## Getting started – creating the Aspire solution

To get started we need to create a new Aspire solution. In this tutorial we will use the Aspire Empty App template and then add an API project to it. Open a terminal and run the following commands.

```
mkdir SeattleHotelAgent
cd SeattleHotelAgent
dotnet new aspire -n SeattleHotelAgent
```

This creates a solution with two projects: **SeattleHotelAgent.AppHost** (the Aspire orchestrator) and **SeattleHotelAgent.ServiceDefaults** (shared configuration for OpenTelemetry, health checks, and service discovery). Now add the API project where the agent will live.

```
dotnet new webapi -n SeattleHotelAgent.Api --use-minimal-apis
dotnet sln add SeattleHotelAgent.Api/SeattleHotelAgent.Api.csproj
dotnet add SeattleHotelAgent.Api/SeattleHotelAgent.Api.csproj reference SeattleHotelAgent.ServiceDefaults/SeattleHotelAgent.ServiceDefaults.csproj
dotnet add SeattleHotelAgent.AppHost/SeattleHotelAgent.AppHost.csproj reference SeattleHotelAgent.Api/SeattleHotelAgent.Api.csproj
```

Now add the NuGet packages needed for the AI agent. We need packages for the Azure OpenAI SDK, keyless authentication, and the Microsoft.Extensions.AI abstractions.

```
cd SeattleHotelAgent.Api
dotnet add package Azure.AI.OpenAI --prerelease
dotnet add package Azure.Identity
dotnet add package Microsoft.Extensions.AI --prerelease
dotnet add package Microsoft.Extensions.AI.OpenAI --prerelease
cd ..
```

Build the solution to ensure everything is configured correctly.

```
dotnet build
```

## Provisioning Azure AI Foundry resources

Before we can use the AI agent, we need an Azure AI Foundry resource with a model deployment. We will provision this using the Azure CLI. Run the following commands to create a resource group, an AI Services account, and a GPT-4o-mini deployment.

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

> **Note:** If your Azure subscription is in a different tenant than your default login (common with MSDN subscriptions), you will need to set the `AzureAI:TenantId` configuration value. You can find your tenant ID by running `az account show --query tenantId -o tsv`.

## Adding hotel data

Now we will add the data that the agent will use. Create a **Models** folder and a **Tools** folder in the API project.

```
mkdir SeattleHotelAgent.Api/Models
mkdir SeattleHotelAgent.Api/Tools
```

First, add the model classes. Create a file named **HotelModels.cs** in the Models folder with the following content.

**Models/HotelModels.cs**

```csharp
namespace SeattleHotelAgent.Api.Models;

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

public record ChatRequest
{
    public required string Message { get; init; }
    public string? SessionId { get; init; }
}

public record AgentResponse
{
    public required string Reply { get; init; }
    public required string SessionId { get; init; }
}
```

Next, create **HotelData.cs** in the Models folder with fake Seattle hotel data. For this tutorial the data is stored in-memory. Below is a shortened version showing two of the eight hotels. The full file with all eight hotels is available in the [source repository](https://github.com/sayedihashimi/foundry-agent-quickstart/blob/main/src/sample-aca/SeattleHotelAgent.Api/Models/HotelData.cs).

**Models/HotelData.cs**

```csharp
namespace SeattleHotelAgent.Api.Models;

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
using SeattleHotelAgent.Api.Models;

namespace SeattleHotelAgent.Api.Tools;

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

There are a few things to note in the code above. Each tool function is a `static` method decorated with a `[Description]` attribute. This attribute is what the AI model uses to understand when and how to call each function. The parameters also have descriptions so that the model knows what values to pass. All four functions return a string, which the model uses to formulate its response to the user.

It is important to include the hotel ID in the search results (e.g., `[ID: ballard-lodge]`). Without the ID, the model cannot chain calls to `GetHotelDetails`, `CheckAvailability`, or `BookRoom` because those functions require the hotel ID as a parameter.

## Wiring up the agent in Program.cs

Now let's wire everything together. Open **Program.cs** in the API project and replace the contents with the following code.

**Program.cs**

```csharp
using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Extensions.AI;
using SeattleHotelAgent.Api.Models;
using SeattleHotelAgent.Api.Tools;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.Services.AddOpenApi();

// Get Foundry endpoint from connection string or configuration
var connectionString = builder.Configuration.GetConnectionString("chat");
string endpoint;
string deploymentName;

if (!string.IsNullOrEmpty(connectionString))
{
    var parts = connectionString.Split(';')
        .Select(p => p.Split('=', 2))
        .ToDictionary(p => p[0].Trim(), p => p[1].Trim());
    endpoint = parts["Endpoint"].TrimEnd('/');
    deploymentName = parts.GetValueOrDefault("DeploymentId", "chat");
}
else
{
    endpoint = builder.Configuration["AzureAI:Endpoint"]
        ?? throw new InvalidOperationException("Set ConnectionStrings:chat or AzureAI:Endpoint");
    deploymentName = builder.Configuration["AzureAI:DeploymentName"] ?? "chat";
}

var credential = new DefaultAzureCredential(new DefaultAzureCredentialOptions
{
    TenantId = builder.Configuration["AzureAI:TenantId"]
});

var chatClient = new AzureOpenAIClient(new Uri(endpoint), credential)
    .GetChatClient(deploymentName)
    .AsIChatClient();

builder.Services.AddChatClient(chatClient)
    .UseFunctionInvocation()
    .UseOpenTelemetry(sourceName: "SeattleHotelAgent");

builder.Services.AddSingleton<IList<AITool>>(
[
    AIFunctionFactory.Create(HotelTools.SearchHotels),
    AIFunctionFactory.Create(HotelTools.GetHotelDetails),
    AIFunctionFactory.Create(HotelTools.CheckAvailability),
    AIFunctionFactory.Create(HotelTools.BookRoom)
]);

var app = builder.Build();
app.MapDefaultEndpoints();
app.MapOpenApi();

var agentInstructions = """
    You are the Seattle Hotel Concierge, a friendly and knowledgeable AI assistant that helps 
    travelers find and book hotels in Seattle, Washington.

    Your capabilities:
    - Search for hotels by neighborhood, star rating, price, and guest count
    - Provide detailed information about specific hotels
    - Check room availability for specific dates
    - Book hotel rooms

    Guidelines:
    - Always be warm and welcoming
    - When users ask vague questions, help narrow down their preferences
    - Suggest neighborhoods based on what they want to do
    - Always confirm booking details before finalizing
    - If dates aren't provided, ask for them before checking availability
    """;

app.MapPost("/api/chat", async (ChatRequest request, IChatClient chatClient, IList<AITool> tools) =>
{
    var messages = new List<ChatMessage>
    {
        new(ChatRole.System, agentInstructions),
        new(ChatRole.User, request.Message)
    };

    var response = await chatClient.GetResponseAsync(
        messages,
        new() { Tools = [.. tools] });

    return Results.Ok(new AgentResponse
    {
        Reply = response.Text ?? "I'm sorry, I couldn't process that request.",
        SessionId = request.SessionId ?? Guid.NewGuid().ToString()[..8]
    });
});

app.MapGet("/api/hotels", () => Results.Ok(HotelData.Hotels));

app.MapGet("/api/hotels/{id}", (string id) =>
{
    var hotel = HotelData.Hotels.FirstOrDefault(h =>
        h.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
    return hotel is not null ? Results.Ok(hotel) : Results.NotFound();
});

app.Run();
```

Let's walk through the key parts of this file.

**Configuration.** The endpoint and deployment name are read from a connection string (`ConnectionStrings:chat`) in the format `Endpoint=https://...;DeploymentId=chat`. If no connection string is set, it falls back to `AzureAI:Endpoint` configuration.

**Authentication.** We use [DefaultAzureCredential](https://learn.microsoft.com/dotnet/azure/sdk/authentication/credential-chains#defaultazurecredential-overview) for keyless authentication. This works with `az login` during local development and with managed identities when deployed.

**Chat client.** The `AzureOpenAIClient` is wrapped as an `IChatClient` using the `.AsIChatClient()` extension from [Microsoft.Extensions.AI](https://learn.microsoft.com/dotnet/ai/ai-extensions). The `.UseFunctionInvocation()` call enables the client to automatically invoke our tool functions when the model requests them.

**Tools.** The four hotel tool functions are registered using `AIFunctionFactory.Create()`, which converts them into `AITool` instances that the model can call. These are passed to `GetResponseAsync` via the `ChatOptions.Tools` property.

**Endpoints.** We have three API endpoints: `POST /api/chat` for interacting with the agent, `GET /api/hotels` for listing all hotels, and `GET /api/hotels/{id}` for getting details on a specific hotel. The `MapOpenApi()` call exposes the OpenAPI document at `/openapi/v1.json`, which you can use to explore the API schema in any OpenAPI-compatible tool.

## Configuring the AppHost

Open the **AppHost.cs** file in the AppHost project and update it to register the API project.

**AppHost.cs**

```csharp
var builder = DistributedApplication.CreateBuilder(args);

builder.AddProject<Projects.SeattleHotelAgent_Api>("hotel-api")
    .WithExternalHttpEndpoints();

builder.Build().Run();
```

## Running the app locally

Now we are ready to test. First, set the connection string so that the API knows where the Foundry resource is. Replace the endpoint below with the value you got from the provisioning step earlier.

```
$env:ConnectionStrings__chat = "Endpoint=https://YOUR-RESOURCE.cognitiveservices.azure.com;DeploymentId=chat"
```

If your subscription tenant differs from your default login tenant, also set the tenant ID.

```
# Get your tenant ID
az account show --query tenantId -o tsv

# Set it
$env:AzureAI__TenantId = "YOUR-TENANT-ID"
```

Run the API project directly to test it.

```
dotnet run --project SeattleHotelAgent.Api
```

The API will start and the URL will be displayed in the terminal output (e.g., `Now listening on: http://localhost:5031`). Note the port number and use it in the following test commands. Open a new terminal window and replace `PORT` with the port from the output.

```powershell
# List all hotels
Invoke-RestMethod -Uri "http://localhost:PORT/api/hotels" | ConvertTo-Json -Depth 2

# Chat with the agent
Invoke-RestMethod -Uri "http://localhost:PORT/api/chat" -Method Post `
  -Body '{"message":"Find me a budget hotel in Ballard for 2 guests for next Monday, under $300/night"}' `
  -ContentType "application/json" | Select-Object -ExpandProperty reply
```

You should see the agent respond with hotel recommendations from the Ballard neighborhood. You can also try more complex requests like booking a room.

```powershell
Invoke-RestMethod -Uri "http://localhost:PORT/api/chat" -Method Post `
  -Body '{"message":"Book a Standard Double at ballard-lodge for Jane Doe from 2026-06-01 to 2026-06-03"}' `
  -ContentType "application/json" | Select-Object -ExpandProperty reply
```

The agent will call the `BookRoom` tool and return a confirmation number.

## Deploying to Azure

To deploy the app to Azure Container Apps, we will use the Azure Developer CLI. Navigate to the folder that contains the solution file and run the following commands.

```
cd SeattleHotelAgent
azd init --from-code
```

When prompted, select **Confirm and continue initializing my app**. You will also be asked to enter a unique environment name — this can be any value you choose (e.g., `hotel-agent-dev`). It is used to name the Azure resource group as `rg-<environment-name>`. This will generate the `azure.yaml` file needed for deployment. Next, set the Azure region and run the deployment.

```
azd env set AZURE_LOCATION eastus2
azd up
```

The `azd up` command will provision the infrastructure (Container Registry, Container Apps Environment, Log Analytics) and deploy the API as a container app.

After deployment, you will need to configure the container app with the Foundry connection string and a managed identity so it can authenticate with the AI Services resource. Run the following commands, replacing the resource names with yours.

```bash
# Assign a managed identity to the container app
az containerapp identity assign \
  --name hotel-api \
  --resource-group rg-hotel-agent \
  --user-assigned $(az identity show --name YOUR-MI-NAME --resource-group rg-hotel-agent --query id -o tsv)

# Set the connection string and managed identity client ID
az containerapp update \
  --name hotel-api \
  --resource-group rg-hotel-agent \
  --set-env-vars \
    "ConnectionStrings__chat=Endpoint=https://YOUR-RESOURCE.cognitiveservices.azure.com;DeploymentId=chat" \
    "AZURE_CLIENT_ID=YOUR-MI-CLIENT-ID"
```

Once the container app restarts, test the deployed endpoint.

```powershell
Invoke-RestMethod -Uri "https://YOUR-APP.azurecontainerapps.io/api/chat" -Method Post `
  -Body '{"message":"What hotels do you have near Pike Place Market?"}' `
  -ContentType "application/json" | Select-Object -ExpandProperty reply
```

## Summary

In this tutorial we created an AI-powered hotel booking agent using Aspire and Azure AI Foundry. We covered how to provision Azure AI resources, create tool functions that the model can call, wire up the agent using Microsoft.Extensions.AI, and deploy the app to Azure Container Apps.

The key technologies we used are summarized below.

| Technology | Purpose |
|---|---|
| [Aspire](https://aspire.dev/) | Service orchestration, OpenTelemetry, health checks |
| [Azure AI Foundry](https://learn.microsoft.com/azure/ai-foundry) | GPT-4o-mini model inference |
| [Microsoft.Extensions.AI](https://learn.microsoft.com/dotnet/ai/ai-extensions) | `IChatClient` abstraction, tool/function calling |
| [Azure.AI.OpenAI](https://www.nuget.org/packages/Azure.AI.OpenAI) | Azure OpenAI SDK |
| [DefaultAzureCredential](https://learn.microsoft.com/dotnet/azure/sdk/authentication) | Keyless authentication |

We encourage you to extend the agent with additional features, such as conversation history, persistent storage, or additional tools. We would love to hear your feedback. If you have any questions or suggestions, please leave a comment below.



