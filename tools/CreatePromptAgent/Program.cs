using Azure.AI.Projects.Agents;

EvaluationSettings settings = EvaluationSettings.Load();
var project = settings.CreateProject();
string name = Environment.GetEnvironmentVariable("FOUNDRY_AGENT_NAME") ?? "dotnet-eval-smoke";
ProjectsAgentVersion created = await project.AgentAdministrationClient.CreateAgentVersionAsync(
    name,
    new ProjectsAgentVersionCreationOptions(new DeclarativeAgentDefinition(settings.Model)
    {
        Instructions = "Explain science clearly and accurately in two or three sentences."
    }));
Console.WriteLine($"Agent: {created.Name}; version: {created.Version}");
