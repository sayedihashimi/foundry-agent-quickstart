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
