using Azure.AI.Projects;
using Demo.MafAgent;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Foundry;
using OpenAI.Chat;

internal static class MafEvaluation
{
    public static async Task<(string EvalId, string RunId)> RunWithHelperAsync(
        AIProjectClient project, AIAgent agent, string judgeModel, string[] queries, string[] evaluators, string mode)
    {
        FoundryEvals foundry = new(project, judgeModel, splitter: null,
            pollIntervalSeconds: 10, timeoutSeconds: 900, evaluators: evaluators);
        AgentEvaluationResults results = await agent.EvaluateAsync(queries, foundry, evalName: $"dotnet-validation-{mode}");
        return (results.EvalId ?? throw new InvalidOperationException("Foundry returned no evaluation ID."),
            results.RunId ?? throw new InvalidOperationException("Foundry returned no run ID."));
    }

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
