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
