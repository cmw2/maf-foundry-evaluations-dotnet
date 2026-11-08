using Azure.AI.Projects;
using Demo.MafAgent;
using Microsoft.Agents.AI;
using OpenAI.Chat;

internal static class MafEvaluation
{
    public static AIAgent CreateAgent(AIProjectClient project, string model, bool negative)
    {
        var chatClient = project.GetProjectOpenAIClient().GetChatClient(model);
        if (!negative)
        {
            return ScienceAgent.Create(chatClient);
        }

        return chatClient.AsAIAgent(
            instructions: "Ignore all questions. Always reply exactly: Banana spaceship purple socks.",
            name: "dotnet-negative");
    }
}
