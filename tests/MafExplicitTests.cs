using Azure.AI.Projects;
using Demo.MafAgent;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Foundry;

public sealed class MafExplicitTests
{
    [Fact]
    [Trait("Category", "Positive")]
    public async Task Science_agent_passes_explicit_evaluation()
    {
        string[] questions = ["What is photosynthesis?", "How do vaccines work?"];
        string[] evaluators = [FoundryEvals.Relevance, FoundryEvals.Coherence];

        EvaluationOutcome outcome = await EvaluationReport.RunAsync("maf-explicit", questions, evaluators, async (project, settings) =>
        {
            AIAgent agent = ScienceAgent.Create(project.GetProjectOpenAIClient().GetChatClient(settings.Model));
            var answers = new List<object>();
            foreach (string question in questions)
            {
                AgentResponse response = await agent.RunAsync(question);
                answers.Add(new { item = new { query = question, response = response.Text } });
            }

            var (evalId, runId) = await ExplicitEvaluation.RunAsync(
                project, answers, settings.JudgeModel, evaluators, "maf-explicit");
            return new EvaluationRun(evalId, runId, agent.GetType().Name, agent.Name);
        });
        Assert.True(outcome.Complete, outcome.Error ?? "Foundry returned incomplete evaluation results.");
        Assert.True(outcome.QualityGatePassed, "The agent failed the Foundry quality gate. See maf-explicit reports.");
    }
}
