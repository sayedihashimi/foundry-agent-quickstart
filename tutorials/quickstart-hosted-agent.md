# Build and Deploy a Foundry Hosted Agent with Aspire

In this tutorial we will build a hotel booking agent that runs as a Foundry Hosted Agent using the [Responses protocol](https://platform.openai.com/docs/api-reference/responses). We will use [Aspire](https://aspire.dev/) to orchestrate the app locally, provision Azure resources declaratively, and deploy the agent to [Microsoft Foundry](https://learn.microsoft.com/azure/foundry/what-is-foundry) as a hosted container agent. The completed code for this tutorial can be found at [foundry-agent-quickstart](https://github.com/sayedihashimi/foundry-agent-quickstart/tree/main/src/sample-hosted-agent).

In this tutorial we will cover the following.

- Creating an Aspire solution with a web-based agent project
- Defining Azure AI Foundry resources declaratively in the AppHost
- Building hotel data models and AI tool functions
- Wiring up the agent with the Foundry Hosting library and the Responses protocol
- Running and testing the agent locally with `aspire run`
- Deploying the agent to Foundry with `aspire deploy`
- Testing the deployed hosted agent

<!-- daily-build-note-start -->
> **Note:** This tutorial currently requires a daily build of the Aspire CLI. The Foundry hosting integration (`Aspire.Hosting.Foundry`) has not yet shipped in a stable release. To install the daily Aspire CLI, run the following command in PowerShell.
>
> ```powershell
> iex "& { $(irm https://aspire.dev/install.ps1) } -Quality dev"
> ```
>
> You can verify the installation with `aspire --version`. You should see a version like `13.3.0-preview.x.xxxxx.x` or later.
<!-- daily-build-note-end -->

## Prerequisites

Before getting started, ensure you have the following installed.

| Prerequisite | Description |
|---|---|
| [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) | .NET 10 or later |
| [Aspire CLI](https://aspire.dev/get-started/what-is-aspire/) | See the daily build note above |
| [Azure CLI](https://learn.microsoft.com/cli/azure/install-azure-cli) | `az` command line tool |
| [Docker Desktop](https://docs.docker.com/desktop/) | Required for `aspire deploy` |
| Azure subscription | An active Azure subscription |

You do not need to create any Azure resources ahead of time. Aspire will provision the AI Foundry account, project, model deployment, and container registry for you.

## Getting started — creating the Aspire solution

To get started we need to create a new Aspire solution. Open a terminal and run the following commands.

```
mkdir SeattleHotelAgent.Hosted
cd SeattleHotelAgent.Hosted
dotnet new aspire -n SeattleHotelAgent.Hosted -o .
```

This creates the AppHost and ServiceDefaults projects. Now create the agent project as a web application and wire it up to the solution.

```
dotnet new web -n SeattleHotelAgent.Hosted.Agent -o SeattleHotelAgent.Hosted.Agent -f net10.0
dotnet sln add SeattleHotelAgent.Hosted.Agent
dotnet add SeattleHotelAgent.Hosted.Agent reference SeattleHotelAgent.Hosted.ServiceDefaults
dotnet add SeattleHotelAgent.Hosted.AppHost reference SeattleHotelAgent.Hosted.Agent
```

Now add the NuGet packages to the agent project.

```
cd SeattleHotelAgent.Hosted.Agent
dotnet add package Azure.AI.Projects --version 2.1.0-beta.1
dotnet add package Azure.Identity --version 1.21.0
dotnet add package Microsoft.Agents.AI.Foundry --version 1.2.0
dotnet add package Microsoft.Agents.AI.Foundry.Hosting --version 1.2.0-preview.260421.1
cd ..
```

Next, add the `Aspire.Hosting.Foundry` package to the AppHost project. We also need to update the AppHost to use the daily Aspire SDK. Run the following from the solution root.

```
aspire update --channel daily --yes --nuget-config-dir .
```

This will update the `Aspire.AppHost.Sdk` version and add a `Aspire.Hosting.Foundry` daily package along with a *nuget.config* file that points to the daily feed.

Now add the Foundry hosting package to the AppHost.

```
dotnet add SeattleHotelAgent.Hosted.AppHost package Aspire.Hosting.Foundry
```

Build the solution to ensure everything is configured correctly.

```
dotnet build
```

This would be a good time to create a commit in case you need to roll back to a good state later.

## Adding hotel data and tools

Now let's add the hotel data that our agent will use. Create the *Models* and *Tools* folders in the agent project.

```
mkdir SeattleHotelAgent.Hosted.Agent/Models
mkdir SeattleHotelAgent.Hosted.Agent/Tools
```

### Hotel models

Create a file named *HotelModels.cs* in the *Models* folder. This defines the record types for hotels, rooms, bookings, and chat messages.

**Models/HotelModels.cs**

```csharp
namespace SeattleHotelAgent.Hosted.Agent.Models;

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

### Hotel data

Create a file named *HotelData.cs* in the *Models* folder. This contains 8 fake Seattle hotels with rooms, amenities, and pricing. The agent's tools will query this data.

**Models/HotelData.cs**

```csharp
namespace SeattleHotelAgent.Hosted.Agent.Models;

public static class HotelData
{
    public static readonly List<Hotel> Hotels =
    [
        new()
        {
            Id = "emerald-inn",
            Name = "The Emerald Inn",
            Description = "A cozy boutique hotel nestled in the heart of Capitol Hill, offering a warm Pacific Northwest ambiance with locally sourced breakfast and stunning city views.",
            Address = "1425 Broadway E, Seattle, WA 98102",
            Neighborhood = "Capitol Hill",
            Rating = 4.6,
            StarRating = 4,
            Rooms =
            [
                new() { Type = "Standard Queen", Description = "Comfortable room with queen bed and city view", PricePerNight = 159m, MaxGuests = 2, AvailableCount = 8 },
                new() { Type = "Deluxe King", Description = "Spacious room with king bed, sitting area, and panoramic city views", PricePerNight = 229m, MaxGuests = 2, AvailableCount = 4 },
                new() { Type = "Suite", Description = "One-bedroom suite with separate living area and kitchenette", PricePerNight = 349m, MaxGuests = 4, AvailableCount = 2 }
            ],
            Amenities = ["Free WiFi", "Complimentary Breakfast", "Rooftop Terrace", "Bike Rentals", "EV Charging"]
        },
        new()
        {
            Id = "pike-place-suites",
            Name = "Pike Place Suites",
            Description = "Steps from the iconic Pike Place Market, this all-suite hotel offers spacious accommodations with full kitchens and floor-to-ceiling windows overlooking Elliott Bay.",
            Address = "86 Pine St, Seattle, WA 98101",
            Neighborhood = "Downtown / Pike Place",
            Rating = 4.8,
            StarRating = 5,
            Rooms =
            [
                new() { Type = "Studio Suite", Description = "Open-concept suite with kitchenette and market views", PricePerNight = 289m, MaxGuests = 2, AvailableCount = 6 },
                new() { Type = "One-Bedroom Suite", Description = "Separate bedroom with full kitchen and Elliott Bay views", PricePerNight = 419m, MaxGuests = 3, AvailableCount = 4 },
                new() { Type = "Penthouse Suite", Description = "Luxury two-bedroom penthouse with wraparound terrace and 360° views", PricePerNight = 799m, MaxGuests = 4, AvailableCount = 1 }
            ],
            Amenities = ["Free WiFi", "Full Kitchen", "Concierge Service", "Spa", "Fitness Center", "Valet Parking"]
        },
        new()
        {
            Id = "ballard-lodge",
            Name = "Ballard Nordic Lodge",
            Description = "Inspired by the neighborhood's Scandinavian heritage, this charming lodge features hygge-inspired rooms, a sauna, and is walking distance to Ballard's breweries.",
            Address = "5300 Ballard Ave NW, Seattle, WA 98107",
            Neighborhood = "Ballard",
            Rating = 4.5,
            StarRating = 3,
            Rooms =
            [
                new() { Type = "Standard Double", Description = "Nordic-themed room with two double beds", PricePerNight = 129m, MaxGuests = 4, AvailableCount = 10 },
                new() { Type = "Deluxe King", Description = "Spacious room with king bed, fireplace, and garden view", PricePerNight = 189m, MaxGuests = 2, AvailableCount = 5 },
                new() { Type = "Family Suite", Description = "Two-room suite ideal for families, with bunk beds and play area", PricePerNight = 269m, MaxGuests = 6, AvailableCount = 3 }
            ],
            Amenities = ["Free WiFi", "Sauna", "Free Parking", "Pet Friendly", "Brewery Tours"]
        },
        new()
        {
            Id = "waterfront-grand",
            Name = "The Waterfront Grand",
            Description = "An upscale waterfront hotel on Alaskan Way with direct access to the Seattle Great Wheel, featuring elegant rooms and a renowned seafood restaurant.",
            Address = "1001 Alaskan Way, Seattle, WA 98101",
            Neighborhood = "Waterfront",
            Rating = 4.7,
            StarRating = 5,
            Rooms =
            [
                new() { Type = "Harbor View Queen", Description = "Elegant room with queen bed overlooking the harbor", PricePerNight = 319m, MaxGuests = 2, AvailableCount = 7 },
                new() { Type = "Premium King", Description = "Premium room with king bed, balcony, and sunset views", PricePerNight = 449m, MaxGuests = 2, AvailableCount = 4 },
                new() { Type = "Presidential Suite", Description = "Expansive two-bedroom suite with private dining room and butler service", PricePerNight = 1199m, MaxGuests = 4, AvailableCount = 1 }
            ],
            Amenities = ["Free WiFi", "Waterfront Restaurant", "Spa & Wellness Center", "Indoor Pool", "Valet Parking", "Room Service"]
        },
        new()
        {
            Id = "fremont-artisan",
            Name = "Fremont Artisan Hotel",
            Description = "A quirky, art-filled hotel in the 'Center of the Universe' neighborhood, featuring rotating gallery exhibitions and rooms designed by local artists.",
            Address = "3601 Fremont Ave N, Seattle, WA 98103",
            Neighborhood = "Fremont",
            Rating = 4.4,
            StarRating = 3,
            Rooms =
            [
                new() { Type = "Artist Loft", Description = "Unique loft-style room with original art and skylight", PricePerNight = 149m, MaxGuests = 2, AvailableCount = 6 },
                new() { Type = "Gallery Suite", Description = "Spacious suite featuring rotating art installations", PricePerNight = 219m, MaxGuests = 3, AvailableCount = 3 },
                new() { Type = "Sculptor's Penthouse", Description = "Top-floor penthouse with rooftop sculpture garden access", PricePerNight = 379m, MaxGuests = 4, AvailableCount = 1 }
            ],
            Amenities = ["Free WiFi", "Art Gallery", "Coffee Bar", "Bike Rentals", "Garden Courtyard"]
        },
        new()
        {
            Id = "slu-tech-hotel",
            Name = "South Lake Union Tech Hotel",
            Description = "A modern, tech-forward hotel in Seattle's innovation district. Every room features smart home controls, and the hotel is steps from Amazon's campus and MOHAI.",
            Address = "401 Terry Ave N, Seattle, WA 98109",
            Neighborhood = "South Lake Union",
            Rating = 4.3,
            StarRating = 4,
            Rooms =
            [
                new() { Type = "Smart Standard", Description = "Tech-equipped room with voice controls and fast WiFi", PricePerNight = 179m, MaxGuests = 2, AvailableCount = 12 },
                new() { Type = "Innovation Suite", Description = "Suite with standing desk, dual monitors, and ergonomic workspace", PricePerNight = 299m, MaxGuests = 2, AvailableCount = 4 },
                new() { Type = "Executive Suite", Description = "Corner suite with meeting space for up to 6 and lake views", PricePerNight = 459m, MaxGuests = 3, AvailableCount = 2 }
            ],
            Amenities = ["Ultra-Fast WiFi", "Co-working Space", "Fitness Center", "Electric Shuttle", "Smart Room Controls"]
        },
        new()
        {
            Id = "pioneer-square-historic",
            Name = "Pioneer Square Heritage Hotel",
            Description = "A beautifully restored 1901 building in Seattle's oldest neighborhood, blending original brick and timber architecture with modern comforts.",
            Address = "95 S Jackson St, Seattle, WA 98104",
            Neighborhood = "Pioneer Square",
            Rating = 4.2,
            StarRating = 3,
            Rooms =
            [
                new() { Type = "Heritage Room", Description = "Charming room with exposed brick walls and period fixtures", PricePerNight = 139m, MaxGuests = 2, AvailableCount = 8 },
                new() { Type = "Loft Room", Description = "High-ceilinged loft with original timber beams", PricePerNight = 199m, MaxGuests = 2, AvailableCount = 4 },
                new() { Type = "Grand Heritage Suite", Description = "Corner suite with restored fireplace and antique furnishings", PricePerNight = 329m, MaxGuests = 3, AvailableCount = 2 }
            ],
            Amenities = ["Free WiFi", "Historical Tours", "Wine Bar", "Library Lounge", "Underground Tour Access"]
        },
        new()
        {
            Id = "green-lake-retreat",
            Name = "Green Lake Nature Retreat",
            Description = "A tranquil retreat overlooking Green Lake, perfect for nature lovers. Features a lakeside trail, kayak rentals, and farm-to-table dining.",
            Address = "7201 E Green Lake Dr N, Seattle, WA 98115",
            Neighborhood = "Green Lake",
            Rating = 4.6,
            StarRating = 4,
            Rooms =
            [
                new() { Type = "Garden Room", Description = "Ground-floor room with private garden patio", PricePerNight = 169m, MaxGuests = 2, AvailableCount = 6 },
                new() { Type = "Lake View King", Description = "Upper-floor room with king bed and lake panorama", PricePerNight = 249m, MaxGuests = 2, AvailableCount = 4 },
                new() { Type = "Nature Suite", Description = "Two-room suite with binoculars, nature library, and balcony", PricePerNight = 359m, MaxGuests = 4, AvailableCount = 2 }
            ],
            Amenities = ["Free WiFi", "Kayak Rentals", "Nature Trails", "Farm-to-Table Restaurant", "Yoga Classes", "Free Parking"]
        }
    ];
}
```

### Hotel tools

The agent uses four tool functions that the LLM can invoke during a conversation. These are regular C# static methods, but the `[Description]` attributes are what make them work as AI tools. Let's take a closer look at how this works.

Create a file named *HotelTools.cs* in the *Tools* folder with the following code.

**Tools/HotelTools.cs**

```csharp
using System.ComponentModel;
using SeattleHotelAgent.Hosted.Agent.Models;

namespace SeattleHotelAgent.Hosted.Agent.Tools;

public static class HotelTools
{
    [Description("Search for hotels in Seattle. You can filter by neighborhood, minimum star rating, maximum price per night, and number of guests. Returns a list of matching hotels with their details.")]
    public static string SearchHotels(
        [Description("Optional neighborhood to filter by (e.g., 'Capitol Hill', 'Ballard', 'Downtown')")] string? neighborhood = null,
        [Description("Minimum star rating (1-5)")] int? minStarRating = null,
        [Description("Maximum price per night in USD")] decimal? maxPricePerNight = null,
        [Description("Number of guests to accommodate")] int? guests = null)
    {
        var results = HotelData.Hotels.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(neighborhood))
        {
            results = results.Where(h =>
                h.Neighborhood.Contains(neighborhood, StringComparison.OrdinalIgnoreCase));
        }

        if (minStarRating.HasValue)
        {
            results = results.Where(h => h.StarRating >= minStarRating.Value);
        }

        if (maxPricePerNight.HasValue)
        {
            results = results.Where(h =>
                h.Rooms.Any(r => r.PricePerNight <= maxPricePerNight.Value));
        }

        if (guests.HasValue)
        {
            results = results.Where(h =>
                h.Rooms.Any(r => r.MaxGuests >= guests.Value && r.AvailableCount > 0));
        }

        var hotels = results.ToList();
        if (hotels.Count == 0)
            return "No hotels found matching your criteria.";

        var lines = hotels.Select(h =>
        {
            var cheapest = h.Rooms.Min(r => r.PricePerNight);
            return $"- [ID: {h.Id}] {h.Name} ({h.StarRating}★, {h.Rating}/5.0) in {h.Neighborhood} — from ${cheapest}/night. {h.Description}";
        });

        return $"Found {hotels.Count} hotel(s):\n{string.Join("\n", lines)}";
    }

    [Description("Get detailed information about a specific hotel including all room types, prices, and amenities.")]
    public static string GetHotelDetails(
        [Description("The hotel ID (e.g., 'emerald-inn', 'pike-place-suites')")] string hotelId)
    {
        var hotel = HotelData.Hotels.FirstOrDefault(h =>
            h.Id.Equals(hotelId, StringComparison.OrdinalIgnoreCase));

        if (hotel is null)
            return $"Hotel with ID '{hotelId}' not found. Use SearchHotels to find available hotels.";

        var rooms = string.Join("\n", hotel.Rooms.Select(r =>
            $"  - {r.Type}: ${r.PricePerNight}/night (up to {r.MaxGuests} guests, {r.AvailableCount} available) — {r.Description}"));

        return $"""
            Hotel: {hotel.Name} ({hotel.StarRating}★, {hotel.Rating}/5.0 rating)
            Location: {hotel.Address} ({hotel.Neighborhood})
            Description: {hotel.Description}
            
            Rooms:
            {rooms}
            
            Amenities: {string.Join(", ", hotel.Amenities)}
            """;
    }

    [Description("Check room availability at a specific hotel for given dates. Returns available rooms and total prices.")]
    public static string CheckAvailability(
        [Description("The hotel ID")] string hotelId,
        [Description("Check-in date (YYYY-MM-DD)")] string checkInDate,
        [Description("Check-out date (YYYY-MM-DD)")] string checkOutDate,
        [Description("Number of guests")] int guests = 1)
    {
        var hotel = HotelData.Hotels.FirstOrDefault(h =>
            h.Id.Equals(hotelId, StringComparison.OrdinalIgnoreCase));

        if (hotel is null)
            return $"Hotel with ID '{hotelId}' not found.";

        if (!DateOnly.TryParse(checkInDate, out var checkIn) || !DateOnly.TryParse(checkOutDate, out var checkOut))
            return "Invalid date format. Please use YYYY-MM-DD.";

        if (checkOut <= checkIn)
            return "Check-out date must be after check-in date.";

        var nights = checkOut.DayNumber - checkIn.DayNumber;

        var availableRooms = hotel.Rooms
            .Where(r => r.MaxGuests >= guests && r.AvailableCount > 0)
            .ToList();

        if (availableRooms.Count == 0)
            return $"No rooms available at {hotel.Name} for {guests} guest(s) on those dates.";

        var lines = availableRooms.Select(r =>
            $"  - {r.Type}: ${r.PricePerNight}/night × {nights} nights = ${r.PricePerNight * nights} total ({r.AvailableCount} rooms left)");

        return $"""
            Availability at {hotel.Name} ({checkIn:MMM d} → {checkOut:MMM d}, {nights} night(s), {guests} guest(s)):
            {string.Join("\n", lines)}
            """;
    }

    [Description("Book a hotel room. Returns a confirmation with booking details and total price.")]
    public static string BookRoom(
        [Description("The hotel ID")] string hotelId,
        [Description("Room type to book (e.g., 'Standard Queen', 'Deluxe King')")] string roomType,
        [Description("Full name of the guest")] string guestName,
        [Description("Check-in date (YYYY-MM-DD)")] string checkInDate,
        [Description("Check-out date (YYYY-MM-DD)")] string checkOutDate)
    {
        var hotel = HotelData.Hotels.FirstOrDefault(h =>
            h.Id.Equals(hotelId, StringComparison.OrdinalIgnoreCase));

        if (hotel is null)
            return $"Hotel with ID '{hotelId}' not found.";

        var room = hotel.Rooms.FirstOrDefault(r =>
            r.Type.Equals(roomType, StringComparison.OrdinalIgnoreCase));

        if (room is null)
            return $"Room type '{roomType}' not found at {hotel.Name}. Available types: {string.Join(", ", hotel.Rooms.Select(r => r.Type))}";

        if (room.AvailableCount <= 0)
            return $"Sorry, no {room.Type} rooms are currently available at {hotel.Name}.";

        if (!DateOnly.TryParse(checkInDate, out var checkIn) || !DateOnly.TryParse(checkOutDate, out var checkOut))
            return "Invalid date format. Please use YYYY-MM-DD.";

        if (checkOut <= checkIn)
            return "Check-out date must be after check-in date.";

        var nights = checkOut.DayNumber - checkIn.DayNumber;
        var totalPrice = room.PricePerNight * nights;
        var confirmationNumber = $"SEA-{Guid.NewGuid().ToString()[..8].ToUpperInvariant()}";

        return $"""
            ✅ Booking Confirmed!
            
            Confirmation #: {confirmationNumber}
            Hotel: {hotel.Name}
            Room: {room.Type}
            Guest: {guestName}
            Check-in: {checkIn:ddd, MMM d, yyyy}
            Check-out: {checkOut:ddd, MMM d, yyyy}
            Duration: {nights} night(s)
            Rate: ${room.PricePerNight}/night
            Total: ${totalPrice}
            
            Please present this confirmation number at check-in. Enjoy your stay in Seattle!
            """;
    }
}
```

The `[Description]` attributes are the key to making these methods work as AI tools. When we call `AIFunctionFactory.Create(HotelTools.SearchHotels)` later in *Program.cs*, the framework reads these attributes and generates a tool schema that gets sent to the LLM. The `[Description]` on the method itself tells the model **what the tool does** and **when to use it**. The `[Description]` on each parameter tells the model **what values to pass** and in **what format**.

For example, when a user says "find me a hotel in Ballard under $200", the LLM sees the `SearchHotels` tool description and knows to call it with `neighborhood: "Ballard"` and `maxPricePerNight: 200`. The descriptions are essentially prompts that help the model choose the right tool and provide the right arguments. Writing clear, specific descriptions here directly impacts how well the agent responds to user requests.

A few things to note about the tool implementation.

- All tools return `string` rather than complex objects. This keeps things simple — the LLM receives the text response and formats it for the user.
- Parameters use nullable types with default values (`string? neighborhood = null`). This allows the LLM to call the tool with only the parameters the user specified, leaving the rest as defaults.
- The tools query the in-memory `HotelData.Hotels` list. In a real application, you would replace this with database queries or API calls.

Build the solution to make sure everything compiles.

```
dotnet build
```

## Configuring the AppHost

Now let's configure the AppHost to declare the Azure AI Foundry resources and wire up the agent for hosted deployment. This is the key part that makes everything work — Aspire handles provisioning the Foundry account, project, model deployment, and container registry for you.

Open *AppHost.cs* in the AppHost project and replace the contents with the following code.

**AppHost.cs**

```csharp
using Aspire.Hosting.Foundry;

var builder = DistributedApplication.CreateBuilder(args);

var foundry = builder.AddFoundry("hotel-foundry");
var project = foundry.AddProject("hotel-project");
var chat = project.AddModelDeployment("chat", FoundryModel.OpenAI.Gpt4oMini);

builder.AddProject<Projects.SeattleHotelAgent_Hosted_Agent>("hotel-agent")
    .WithReference(project)
    .WithReference(chat).WaitFor(chat)
    .PublishAsHostedAgent(project);

builder.Build().Run();
```

Let's walk through what each line does.

`AddFoundry("hotel-foundry")` declares an AI Foundry account resource. When you deploy, Aspire will create this as a `Microsoft.CognitiveServices/accounts` resource of kind `AIServices` in Azure.

`AddProject("hotel-project")` creates an AI Foundry project under that account. The project is the container for your agents, model deployments, and connections.

`AddModelDeployment("chat", FoundryModel.OpenAI.Gpt4oMini)` creates a GPT-4o-mini model deployment named "chat" within the project.

`.WithReference(project)` and `.WithReference(chat)` inject connection strings into the agent process as environment variables. The agent will receive `ConnectionStrings__hotel-project` and `ConnectionStrings__chat` automatically.

`.PublishAsHostedAgent(project)` tells Aspire that in publish mode, this project should be deployed as a Foundry hosted agent container. Aspire will build a Docker image, push it to an Azure Container Registry, and create a hosted agent version in the Foundry project.

## Wiring up the agent in Program.cs

Now let's write the agent code. Open *Program.cs* in the agent project and replace the contents with the following code.

**Program.cs**

```csharp
using System.Data.Common;
using Azure.AI.Projects;
using Azure.Core;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Foundry.Hosting;
using Microsoft.Extensions.AI;
using SeattleHotelAgent.Hosted.Agent.Tools;

string projectConnectionString = Environment.GetEnvironmentVariable("ConnectionStrings__hotel-project")
    ?? throw new InvalidOperationException("ConnectionStrings__hotel-project is not set.");
string chatConnectionString = Environment.GetEnvironmentVariable("ConnectionStrings__chat")
    ?? throw new InvalidOperationException("ConnectionStrings__chat is not set.");

DbConnectionStringBuilder projectConnectionBuilder = new() { ConnectionString = projectConnectionString };
DbConnectionStringBuilder chatConnectionBuilder = new() { ConnectionString = chatConnectionString };

string projectEndpoint = GetRequiredConnectionValue(projectConnectionBuilder, "Endpoint");
string deploymentName = GetRequiredConnectionValue(chatConnectionBuilder, "Deployment");

if (!Uri.TryCreate(projectEndpoint, UriKind.Absolute, out Uri? projectUri) || projectUri is null)
{
    throw new InvalidOperationException("ConnectionStrings__hotel-project contains an invalid Endpoint value.");
}

// Chained credential: try a temporary dev token first (for local Docker debugging),
// then fall back to DefaultAzureCredential (for local dev / managed identity in production).
TokenCredential credential = new ChainedTokenCredential(
    new DevTemporaryTokenCredential(),
    new DefaultAzureCredential());

// Register hotel tools for function calling
var tools = new AIFunction[]
{
    AIFunctionFactory.Create(HotelTools.SearchHotels),
    AIFunctionFactory.Create(HotelTools.GetHotelDetails),
    AIFunctionFactory.Create(HotelTools.CheckAvailability),
    AIFunctionFactory.Create(HotelTools.BookRoom)
};

AIAgent agent = new AIProjectClient(projectUri, credential)
    .AsAIAgent(
        model: deploymentName,
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
        name: "SeattleHotelConcierge",
        description: "A hotel booking agent for Seattle with search, availability, and booking tools",
        tools: tools);

string port = Environment.GetEnvironmentVariable("DEFAULT_AD_PORT") ?? "8088";

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls($"http://+:{port}");
builder.Services.AddFoundryResponses(agent);

var app = builder.Build();
app.MapFoundryResponses();
app.MapGet("/liveness", () => Results.Ok("Healthy"));
app.MapGet("/readiness", () => Results.Ok("Ready"));

if (app.Environment.IsDevelopment())
{
    app.MapFoundryResponses("openai/v1");
}

app.Run();

string GetRequiredConnectionValue(DbConnectionStringBuilder connectionBuilder, string key)
{
    if (!connectionBuilder.TryGetValue(key, out object? rawValue) || rawValue is null)
    {
        throw new InvalidOperationException($"Connection string is missing '{key}'.");
    }

    string? value = rawValue.ToString();

    if (string.IsNullOrWhiteSpace(value))
    {
        throw new InvalidOperationException($"Connection string has an empty '{key}' value.");
    }

    return value;
}

/// <summary>
/// A <see cref="TokenCredential"/> for local Docker debugging only.
/// Reads a pre-fetched bearer token from the <c>AZURE_BEARER_TOKEN</c> environment variable.
///
/// Generate a token on your host and pass it to the container:
///   export AZURE_BEARER_TOKEN=$(az account get-access-token --resource https://ai.azure.com --query accessToken -o tsv)
///   docker run -e AZURE_BEARER_TOKEN=$AZURE_BEARER_TOKEN ...
/// </summary>
internal sealed class DevTemporaryTokenCredential : TokenCredential
{
    private const string EnvironmentVariable = "AZURE_BEARER_TOKEN";
    private readonly string? _token;

    public DevTemporaryTokenCredential()
    {
        _token = Environment.GetEnvironmentVariable(EnvironmentVariable);
    }

    public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
        => GetAccessToken();

    public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
        => new(GetAccessToken());

    private AccessToken GetAccessToken()
    {
        if (string.IsNullOrEmpty(_token) || _token == "DefaultAzureCredential")
        {
            throw new CredentialUnavailableException($"{EnvironmentVariable} environment variable is not set.");
        }

        return new AccessToken(_token, DateTimeOffset.UtcNow.AddHours(1));
    }
}
```

Let's walk through the key parts of this code.

**Connection strings from Aspire.** The agent reads `ConnectionStrings__hotel-project` and `ConnectionStrings__chat` which are automatically injected by Aspire's `.WithReference()` calls in the AppHost. These connection strings contain the Foundry project endpoint and the model deployment name.

**AIProjectClient.** We create an `AIProjectClient` from the `Azure.AI.Projects` SDK and call `.AsAIAgent()` to create an agent with instructions, tools, and a model deployment. This is the [Microsoft Agent Framework](https://aka.ms/agent-framework) pattern for building hosted agents.

**AddFoundryResponses / MapFoundryResponses.** These methods from `Microsoft.Agents.AI.Foundry.Hosting` register the agent in the DI container and map the Responses protocol endpoints (`POST /responses`). The Foundry platform will route requests to this endpoint when the agent is deployed.

**DevTemporaryTokenCredential.** This is a helper for local Docker debugging. When running inside a Docker container, `DefaultAzureCredential` cannot access your Azure CLI session. You can pre-fetch a token and pass it via the `AZURE_BEARER_TOKEN` environment variable. In production, Foundry injects a managed identity automatically.

Build the solution to make sure everything compiles.

```
dotnet build
```

## Running the agent locally

To run the agent locally, you need to be logged in to Azure so that Aspire can provision the Foundry resources.

```powershell
az login
```

Now start the app with `aspire run`.

```powershell
aspire run --apphost SeattleHotelAgent.Hosted.AppHost/SeattleHotelAgent.Hosted.AppHost.csproj
```

The first time you run this, the Aspire dashboard will open and prompt you to configure your Azure tenant, subscription, and resource group. Select the appropriate values and click submit. Aspire will then provision the AI Foundry account, project, and model deployment in Azure. This initial provisioning can take a few minutes.

Once the resources are provisioned, the agent will start automatically. You should see the `hotel-agent` resource transition to **Running** in the Aspire dashboard.

## Testing the agent locally

Open a separate terminal to test the agent. The Responses protocol uses a single `POST /responses` endpoint with an `input` field.

```powershell
$body = '{"input": "Find me a budget hotel in Ballard for 2 guests, under 200 dollars per night"}'
$r = Invoke-RestMethod -Uri "http://localhost:8088/responses" -Method POST -Body $body -ContentType "application/json" -TimeoutSec 60
$r.output | Where-Object { $_.type -eq "message" } | ForEach-Object { $_.content } | ForEach-Object { $_.text }
```

You should see the agent respond with the Ballard Nordic Lodge recommendation, including the price and amenities. The agent is using the `SearchHotels` tool function behind the scenes to query the in-memory hotel data.

You can also test booking.

```powershell
$body = '{"input": "Book a Standard Double at ballard-lodge for Jane Doe from 2026-06-01 to 2026-06-03"}'
$r = Invoke-RestMethod -Uri "http://localhost:8088/responses" -Method POST -Body $body -ContentType "application/json" -TimeoutSec 60
$r.output | Where-Object { $_.type -eq "message" } | ForEach-Object { $_.content } | ForEach-Object { $_.text }
```

Stop the app with Ctrl+C when you are done testing locally.

## Deploying to Foundry

Now let's deploy the agent to Microsoft Foundry as a hosted agent. Make sure Docker Desktop is running, then run the following command.

```powershell
aspire deploy --apphost SeattleHotelAgent.Hosted.AppHost/SeattleHotelAgent.Hosted.AppHost.csproj
```

Aspire will prompt you to select your Azure tenant, subscription, and resource group. If you already ran `aspire run`, it will reuse the existing provisioned resources. The deploy process will.

1. Build a Docker image for the agent
2. Push the image to the Azure Container Registry
3. Provision the AI Foundry resources (if not already provisioned)
4. Create a hosted agent version in the Foundry project

The full deploy typically takes 5–7 minutes on first run.

### Starting the agent in the Foundry portal

After `aspire deploy` completes, you need to manually start the agent version in the Foundry portal. This is a current limitation of the platform.

1. Go to [https://ai.azure.com](https://ai.azure.com)
2. Switch to the **New Foundry** experience if prompted
3. Navigate to your project
4. Click **Agents** in the left sidebar
5. Find your agent and click **Start** on the agent version

You can also use the **Log Stream** button on the same page to view container logs if you need to troubleshoot.

> **Note:** After deploying, you may also need to grant the agent's managed identity the **Cognitive Services OpenAI User** role on the Foundry account. You can do this from the Azure portal under the Foundry account's Access Control (IAM) page, or with the Azure CLI.
>
> ```powershell
> # Get the agent's managed identity principal ID from the Foundry portal or ARM API
> az role assignment create --assignee <agent-principal-id> --role "Cognitive Services OpenAI User" --scope <foundry-account-resource-id>
> ```

## Testing the deployed agent

Once the agent is running in Foundry, you can test it via the Responses API. You will need a bearer token scoped to `https://ai.azure.com`.

```powershell
$token = az account get-access-token --resource "https://ai.azure.com" --query accessToken -o tsv
$endpoint = "https://<your-foundry-account>.services.ai.azure.com/api/projects/<your-project>"
$body = '{"input":"Find me a hotel in Ballard under $200","agent_reference":{"type":"agent_reference","name":"<your-agent-name>"}}'

$r = Invoke-RestMethod -Uri "$endpoint/openai/v1/responses" `
    -Headers @{"Authorization"="Bearer $token";"Content-Type"="application/json"} `
    -Method POST -Body $body -TimeoutSec 90

$r.output | Where-Object { $_.type -eq "message" } | ForEach-Object { $_.content } | ForEach-Object { $_.text }
```

Replace `<your-foundry-account>`, `<your-project>`, and `<your-agent-name>` with the values from your deployment. You can find the agent name by listing agents.

```powershell
$token = az account get-access-token --resource "https://ai.azure.com" --query accessToken -o tsv
Invoke-RestMethod -Uri "$endpoint/agents?api-version=v1" `
    -Headers @{"Authorization"="Bearer $token"} -Method GET
```

You can also test the agent directly in the Foundry portal playground by navigating to your agent and clicking the playground tab.

## Summary

In this tutorial we built a Foundry Hosted Agent using Aspire and the Microsoft Agent Framework. We used Aspire's declarative resource model to provision all Azure resources, and deployed the agent as a hosted container to Microsoft Foundry.

| Technology | Purpose |
|---|---|
| [Aspire.Hosting.Foundry](https://aspire.dev/) | Declarative Foundry resource provisioning and hosted agent deployment |
| [Microsoft.Agents.AI.Foundry.Hosting](https://aka.ms/agent-framework) | Responses protocol hosting (`AddFoundryResponses` / `MapFoundryResponses`) |
| [Microsoft.Agents.AI](https://www.nuget.org/packages/Microsoft.Agents.AI) | Agent abstraction (`AIProjectClient.AsAIAgent()`) |
| [Azure.AI.Projects](https://www.nuget.org/packages/Azure.AI.Projects) | Azure AI Foundry project client |
| [DefaultAzureCredential](https://learn.microsoft.com/dotnet/azure/sdk/authentication) | Keyless authentication |

The completed source code is available at [foundry-agent-quickstart/src/sample-hosted-agent](https://github.com/sayedihashimi/foundry-agent-quickstart/tree/main/src/sample-hosted-agent).

## Resources

- [Microsoft Agent Framework documentation](https://aka.ms/agent-framework)
- [Aspire documentation](https://aspire.dev/)
- [Microsoft Foundry documentation](https://learn.microsoft.com/azure/foundry/what-is-foundry)
- [Foundry hosted agents samples](https://github.com/microsoft/agent-framework/tree/main/dotnet/samples/04-hosting/FoundryHostedAgents)
- [Aspire Foundry playground](https://github.com/microsoft/aspire/tree/main/playground/FoundryHostedAgents)
