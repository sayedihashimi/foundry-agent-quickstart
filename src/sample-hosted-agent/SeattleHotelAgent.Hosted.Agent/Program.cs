using Azure.AI.Projects;
using Azure.Core;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Foundry.Hosting;
using Microsoft.Extensions.AI;
using SeattleHotelAgent.Hosted.Agent.Tools;

string endpoint = Environment.GetEnvironmentVariable("AZURE_AI_PROJECT_ENDPOINT")
    ?? throw new InvalidOperationException("AZURE_AI_PROJECT_ENDPOINT is not set.");
string deploymentName = Environment.GetEnvironmentVariable("AZURE_AI_MODEL_DEPLOYMENT_NAME") ?? "chat";

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

AIAgent agent = new AIProjectClient(new Uri(endpoint), credential)
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
        name: Environment.GetEnvironmentVariable("AGENT_NAME") ?? "SeattleHotelConcierge",
        description: "A hotel booking agent for Seattle with search, availability, and booking tools",
        tools: tools);

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddFoundryResponses(agent);

var app = builder.Build();
app.MapFoundryResponses();

if (app.Environment.IsDevelopment())
{
    app.MapFoundryResponses("openai/v1");
}

app.Run();

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
