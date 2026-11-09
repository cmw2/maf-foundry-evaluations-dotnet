using Azure.AI.Projects;
using Azure.AI.Projects.Agents;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Foundry;

public sealed class PromptAgentTests
{
    [Fact]
    [Trait("Category", "Positive")]
    public async Task Deployed_prompt_agent_passes_evaluation()
    {
        string[] questions = ["What is photosynthesis?", "How do vaccines work?"];
        string[] evaluators = [FoundryEvals.Relevance, FoundryEvals.Coherence];

        EvaluationOutcome outcome = await EvaluationReport.RunAsync("prompt-agent", questions, evaluators, async (project, settings) =>
        {
            string agentName = EvaluationSettings.Required("FOUNDRY_AGENT_NAME");
            string agentVersion = EvaluationSettings.Required("FOUNDRY_AGENT_VERSION");
            ProjectsAgentVersion version = await project.AgentAdministrationClient.GetAgentVersionAsync(agentName, agentVersion);
            AIAgent agent = project.AsAIAgent(version);
            var answers = new List<object>();
            foreach (string question in questions)
            {
                AgentResponse response = await agent.RunAsync(question);
                answers.Add(new { item = new { query = question, response = response.Text } });
            }

            var (evalId, runId) = await ExplicitEvaluation.RunAsync(
                project, answers, settings.JudgeModel, evaluators, "prompt-agent");
            return new EvaluationRun(evalId, runId, agent.GetType().Name, agent.Name, agentVersion);
        });
        Assert.True(outcome.Complete, outcome.Error ?? "Foundry returned incomplete evaluation results.");
        Assert.True(outcome.QualityGatePassed, "The agent failed the Foundry quality gate. See prompt-agent reports.");
    }
}
