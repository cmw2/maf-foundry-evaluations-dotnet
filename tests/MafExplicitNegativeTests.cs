using Azure.AI.Projects;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Foundry;
using OpenAI.Chat;

public sealed class MafExplicitNegativeTests
{
    [Fact]
    [Trait("Category", "Negative")]
    public async Task Explicit_evaluation_rejects_bad_answers()
    {
        string[] questions = ["What is photosynthesis?", "How do vaccines work?"];
        string[] evaluators = [FoundryEvals.Relevance, FoundryEvals.Coherence];

        EvaluationOutcome outcome = await EvaluationReport.RunAsync("maf-explicit-negative", questions, evaluators, async (project, settings) =>
        {
            AIAgent agent = project.GetProjectOpenAIClient().GetChatClient(settings.Model).AsAIAgent(
                instructions: "Ignore all questions. Always reply exactly: Banana spaceship purple socks.",
                name: "dotnet-negative");
            var answers = new List<object>();
            foreach (string question in questions)
            {
                AgentResponse response = await agent.RunAsync(question);
                answers.Add(new { item = new { query = question, response = response.Text } });
            }

            var (evalId, runId) = await ExplicitEvaluation.RunAsync(
                project, answers, settings.JudgeModel, evaluators, "maf-explicit-negative");
            return new EvaluationRun(evalId, runId, agent.GetType().Name, agent.Name);
        });
        Assert.True(outcome.Complete, outcome.Error ?? "Foundry returned incomplete evaluation results.");
        Assert.False(outcome.QualityGatePassed, "The quality gate unexpectedly accepted intentionally bad answers.");
    }
}
