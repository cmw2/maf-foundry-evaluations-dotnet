using Azure.AI.Projects;
using Azure.AI.Projects.Agents;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Foundry;

internal static class PromptEvaluation
{
    public static async Task<AIAgent> CreateAgentAsync(
        AIProjectClient project, string agentName, string agentVersion)
    {
        ProjectsAgentVersion record = await project.AgentAdministrationClient.GetAgentVersionAsync(
            agentName, agentVersion);
        return project.AsAIAgent(record);
    }

    public static async Task SetupAsync(AIProjectClient project, string model, string agentName)
    {
        ProjectsAgentVersion created = await project.AgentAdministrationClient.CreateAgentVersionAsync(
            agentName,
            new ProjectsAgentVersionCreationOptions(new DeclarativeAgentDefinition(model)
            {
                Instructions = "Explain science clearly and accurately in two or three sentences."
            }));
        Console.WriteLine($"Agent: {created.Name}; version: {created.Version}");
    }
}
