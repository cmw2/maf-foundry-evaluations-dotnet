using Azure.AI.Projects;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Foundry;
using OpenAI.Chat;

public sealed class MafHelperNegativeTests
{
    [Fact]
    [Trait("Category", "Negative")]
    public async Task Helper_evaluation_rejects_bad_answers()
    {
        string[] questions = ["What is photosynthesis?", "How do vaccines work?"];
        string[] evaluators = [FoundryEvals.Relevance, FoundryEvals.Coherence];

        EvaluationOutcome outcome = await EvaluationReport.RunAsync("maf-helper-negative", questions, evaluators, async (project, settings) =>
        {
            AIAgent agent = project.GetProjectOpenAIClient().GetChatClient(settings.Model).AsAIAgent(
                instructions: "Ignore all questions. Always reply exactly: Banana spaceship purple socks.",
                name: "dotnet-negative");
            FoundryEvals foundry = new(project, settings.JudgeModel, splitter: null,
                pollIntervalSeconds: 10, timeoutSeconds: 900, evaluators: evaluators);
            AgentEvaluationResults results = await agent.EvaluateAsync(
                questions, foundry, evalName: "dotnet-validation-maf-helper-negative");

            return new EvaluationRun(
                results.EvalId ?? throw new InvalidOperationException("Foundry returned no evaluation ID."),
                results.RunId ?? throw new InvalidOperationException("Foundry returned no run ID."),
                agent.GetType().Name, agent.Name);
        });
        Assert.True(outcome.Complete, outcome.Error ?? "Foundry returned incomplete evaluation results.");
        Assert.False(outcome.QualityGatePassed, "The quality gate unexpectedly accepted intentionally bad answers.");
    }
}
