using Microsoft.Agents.AI;
using OpenAI.Chat;

namespace Demo.MafAgent;

public static class ScienceAgent
{
    public static AIAgent Create(ChatClient chatClient) =>
        chatClient.AsAIAgent(
            instructions: "Explain science clearly and accurately in two or three sentences.",
            name: "dotnet-maf");
}
