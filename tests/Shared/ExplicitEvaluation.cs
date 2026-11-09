using System.ClientModel;
using System.ClientModel.Primitives;
using System.Text.Json;
using Azure.AI.Projects;

public static class ExplicitEvaluation
{
    public static async Task<(string EvalId, string RunId)> RunAsync(
        AIProjectClient project, IEnumerable<object> inputs, string judgeModel, string[] evaluators, string testName)
    {
        var evalClient = project.GetProjectOpenAIClient().GetEvaluationClient();
        var definition = new
        {
            name = $"dotnet-validation-{testName}-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}",
            data_source_config = new
            {
                type = "custom",
                item_schema = new
                {
                    type = "object",
                    properties = new { query = new { type = "string" }, response = new { type = "string" } },
                    required = new[] { "query", "response" }
                }
            },
            testing_criteria = evaluators.Select(name => new
            {
                type = "azure_ai_evaluator",
                name,
                evaluator_name = $"builtin.{name}",
                initialization_parameters = new { model = judgeModel },
                data_mapping = new { query = "{{item.query}}", response = "{{item.response}}" }
            })
        };
        var created = await evalClient.CreateEvaluationAsync(
            BinaryContent.Create(BinaryData.FromObjectAsJson(definition)), new RequestOptions());
        using JsonDocument createdJson = JsonDocument.Parse(created.GetRawResponse().Content);
        string evalId = createdJson.RootElement.GetProperty("id").GetString()
            ?? throw new InvalidOperationException("Foundry returned no evaluation ID.");
        var submitted = await evalClient.CreateEvaluationRunAsync(evalId,
            BinaryContent.Create(BinaryData.FromObjectAsJson(new
            {
                name = definition.name,
                data_source = new { type = "jsonl", source = new { type = "file_content", content = inputs } }
            })), new RequestOptions());
        using JsonDocument submittedJson = JsonDocument.Parse(submitted.GetRawResponse().Content);
        string runId = submittedJson.RootElement.GetProperty("id").GetString()
            ?? throw new InvalidOperationException("Foundry returned no run ID.");
        return (evalId, runId);
    }
}
