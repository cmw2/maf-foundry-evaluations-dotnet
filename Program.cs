using Azure.AI.Projects;
using Azure.Identity;
using Demo.MafAgent;
using Microsoft.Agents.AI;

if (args.Length == 0)
{
    Console.Error.WriteLine("Supply a question, for example: \"What is photosynthesis?\"");
    return 2;
}

string endpoint = Required("FOUNDRY_PROJECT_ENDPOINT");
string model = Required("FOUNDRY_MODEL");
AIProjectClient project = new(new Uri(endpoint), new AzureCliCredential(new AzureCliCredentialOptions
{
    ProcessTimeout = TimeSpan.FromSeconds(120)
}));

AIAgent agent = ScienceAgent.Create(project.GetProjectOpenAIClient().GetChatClient(model));
AgentResponse response = await agent.RunAsync(string.Join(" ", args));
Console.WriteLine(response.Text);
return 0;

static string Required(string name) =>
    Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
        ? value
        : throw new InvalidOperationException($"{name} is required.");
