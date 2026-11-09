using Azure.AI.Projects;
using Azure.Identity;

public sealed record EvaluationSettings(string Endpoint, string Model, string JudgeModel)
{
    public static EvaluationSettings Load()
    {
        string model = Required("FOUNDRY_MODEL");
        return new(Required("FOUNDRY_PROJECT_ENDPOINT"), model,
            Environment.GetEnvironmentVariable("FOUNDRY_JUDGE_MODEL") is { Length: > 0 } judge ? judge : model);
    }

    public AIProjectClient CreateProject() =>
        new(new Uri(Endpoint), new AzureCliCredential(new AzureCliCredentialOptions
        {
            ProcessTimeout = TimeSpan.FromSeconds(120)
        }));

    public static string Required(string name) =>
        Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException($"{name} is required.");
}
