var builder = DistributedApplication.CreateBuilder(args);

var projectEndpoint = builder.AddParameter("azure-ai-project-endpoint", secret: true);
var modelDeployment = builder.AddParameter("azure-ai-model-deployment", "chat");

builder.AddProject<Projects.SeattleHotelAgent_Hosted_Agent>("hotel-agent")
    .WithEnvironment("AZURE_AI_PROJECT_ENDPOINT", projectEndpoint)
    .WithEnvironment("AZURE_AI_MODEL_DEPLOYMENT_NAME", modelDeployment)
    .PublishAsHostedAgent();

builder.Build().Run();
