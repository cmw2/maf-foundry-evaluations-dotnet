using Azure.AI.Projects;
using Demo.MafAgent;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Foundry;

public sealed class MafHelperTests
{
    [Fact]
    [Trait("Category", "Positive")]
    public async Task Science_agent_passes_helper_evaluation()
    {
        string[] questions = ["What is photosynthesis?", "How do vaccines work?"];
        string[] evaluators = [FoundryEvals.Relevance, FoundryEvals.Coherence];

        EvaluationOutcome outcome = await EvaluationReport.RunAsync("maf-helper", questions, evaluators, async (project, settings) =>
        {
            AIAgent agent = ScienceAgent.Create(project.GetProjectOpenAIClient().GetChatClient(settings.Model));
            FoundryEvals foundry = new(project, settings.JudgeModel, splitter: null,
                pollIntervalSeconds: 10, timeoutSeconds: 900, evaluators: evaluators);
            AgentEvaluationResults results = await agent.EvaluateAsync(questions, foundry, evalName: "dotnet-validation-maf-helper");

            return new EvaluationRun(
                results.EvalId ?? throw new InvalidOperationException("Foundry returned no evaluation ID."),
                results.RunId ?? throw new InvalidOperationException("Foundry returned no run ID."),
                agent.GetType().Name, agent.Name);
        });
        Assert.True(outcome.Complete, outcome.Error ?? "Foundry returned incomplete evaluation results.");
        Assert.True(outcome.QualityGatePassed, "The agent failed the Foundry quality gate. See maf-helper reports.");
    }
}
