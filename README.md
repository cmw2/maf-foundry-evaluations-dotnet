# Evaluate a .NET agent with Microsoft Foundry

Start with a Microsoft Agent Framework (MAF) application, evaluate its answers
with Microsoft Foundry, and surface the results in GitHub Actions. The
application and evaluation code are written in C#.

## How it works

1. The **science agent** uses a model deployed in Foundry to explain science
   clearly and accurately in two or three sentences.
2. The **evaluation project** calls that same agent with two questions:
   "What is photosynthesis?" and "How do vaccines work?"
3. **Foundry evaluators** use a judge model to score each answer for relevance
   and coherence, and store the evaluation results in Foundry.
4. The **quality gate** requires every evaluator to pass and every score to be
   at least 3. Missing scores or evaluator errors fail the run.
5. **GitHub Actions** displays the scores, individual test results, and a link
   to the Foundry report, with downloadable result files.

The judge can use a different model deployment from the agent.

## Code walkthrough

| File | Responsibility |
|---|---|
| `ScienceAgent.cs` | Defines the science agent's instructions and creates the MAF agent. |
| `Program.cs` | Runs the application with a question supplied on the command line. |
| `MafAgent.csproj` | The application project at the repository root. |
| `tests/Program.cs` | Selects the evaluation mode. |
| `tests/MafEvaluation.cs` | Connects the application agent to the tests and shows the `EvaluateAsync` approach. |
| `tests/PromptEvaluation.cs` | Loads an existing Foundry prompt agent, or creates the example prompt agent. |
| `tests/ExplicitEvaluation.cs` | Invokes the agent and submits answers to Foundry with explicit orchestration. |
| `tests/EvaluationRunner.cs` | Selects the approach, retrieves scores, enforces the quality gate, and writes reports. |
| `tests/EvalValidation.csproj` | The evaluation project, which references the application project. |
| `.github/workflows/evaluate.yml` | Runs evaluations and publishes GitHub summaries, test checks, and artifacts. |

To evaluate your own MAF application, reference its project from
`tests/EvalValidation.csproj` and update the factory call in
`tests/MafEvaluation.cs`. Your application's agent instructions and tools stay
in the application; the test project owns the questions and scoring.

## Run locally

### Prerequisites

- .NET 10 SDK, Git, and Azure CLI.
- A Foundry project with an agent model deployment and a judge model deployment
  suitable for relevance and coherence evaluation.
- Foundry User access on the parent Foundry account for the identity running
  evaluations. See [evaluation permissions](https://learn.microsoft.com/en-us/azure/foundry/observability/how-to/evaluation-permissions).

Both projects use NuGet packages. `dotnet run` restores their dependencies
automatically; you only need this repository.

### Sign in and configure

The application uses `AzureCliCredential`, which uses your Azure CLI sign-in.
Sign in to the tenant that contains your Foundry project and select its
subscription:

```powershell
az login --tenant '<tenant ID>'
az account set --subscription '<subscription ID>'

$env:FOUNDRY_PROJECT_ENDPOINT = 'https://<resource>.services.ai.azure.com/api/projects/<project>'
$env:FOUNDRY_MODEL = '<agent model deployment>'
$env:FOUNDRY_JUDGE_MODEL = '<judge model deployment>'
```

`FOUNDRY_JUDGE_MODEL` defaults to `FOUNDRY_MODEL` when omitted. A standard Azure
CLI sign-in is sufficient for credential acquisition; a custom
`AZURE_CONFIG_DIR` is optional for people who maintain separate CLI profiles.

### Run the agent, then evaluate it

```powershell
dotnet run --project MafAgent.csproj -- "What is photosynthesis?"
dotnet run --project tests\EvalValidation.csproj -- maf
```

The first command prints the agent's answer. The second asks both test questions
and submits the answers to Foundry for scoring. Results are written to `results/`
as JSON, Markdown, and JUnit XML. Exit code 0 means the quality gate passed;
exit code 1 indicates a quality failure or evaluation error.

### Evaluate the MAF agent with less orchestration

```powershell
dotnet run --project tests\EvalValidation.csproj -- maf-simple
```

This test passes the agent, questions, and Foundry evaluators to
`agent.EvaluateAsync`. Agent Framework calls the agent for each question,
then submits the collected responses to Foundry. The test runner retrieves
the results for the same quality gate and JSON, Markdown, and JUnit reports
as the explicit approach.
You can also supply expected answers via `expectedOutput` when using an
evaluator that compares responses with a reference; relevance and coherence
do not use expected answers.

Start with this approach for straightforward tests. Use explicit orchestration
when you need more control over agent invocation or evaluation requests.
Both use the same agent, judge criteria, and CI reporting.

### Evaluate a Foundry prompt agent

Select an existing agent and version:

```powershell
$env:FOUNDRY_AGENT_NAME = '<agent name>'
$env:FOUNDRY_AGENT_VERSION = '<agent version>'
dotnet run --project tests\EvalValidation.csproj -- prompt
```

Or create the example science prompt agent once:

```powershell
$env:FOUNDRY_AGENT_NAME = 'dotnet-eval-smoke'
dotnet run --project tests\EvalValidation.csproj -- setup
$env:FOUNDRY_AGENT_VERSION = '<version printed by setup>'
dotnet run --project tests\EvalValidation.csproj -- prompt
```

To demonstrate a failing quality gate, run `negative` for the explicit approach
or `maf-simple-negative` for the helper approach. Each substitutes an agent
that answers both questions with "Banana spaceship purple socks."

```powershell
dotnet run --project tests\EvalValidation.csproj -- negative
dotnet run --project tests\EvalValidation.csproj -- maf-simple-negative
```

### Local evaluation status

Local agent invocation and direct judge inference succeeded in testing.
Local evaluations submitted using an interactive Azure CLI user sign-in
have intermittently returned incomplete scores with the nested error
`Principal does not have access to API/Operation`. Account-scoped Foundry User
access, the documented request format, and a separate judge model did not
reliably resolve it. A temporary project-managed-identity permission test
produced a fully scored positive run and a fully scored negative run, but
another evaluation still had the same authentication error after propagation.
After the project identity's account-scoped Foundry User grant was restored,
the concise MAF example returned one incomplete score on one run and all four
scores on another. The exact denied caller identity and root cause remain
unresolved.

Earlier versions of the explicit evaluation paths produced complete scores
using GitHub Actions workload identity. The current helper mode also passed
both cases and all four evaluator scores in a private GitHub Actions run.
This successful run does not establish that the intermittent error is resolved.

## Run in GitHub Actions

This public repository is not connected to an Azure environment. To run the
workflow in your own repository, create a dedicated Azure identity with a
federated credential for that repository's `main` branch, and grant it Foundry
User on the parent Foundry account. Match the federation to the repository's
actual OIDC subject.
The workflow uses `azure/login` to sign in without a client secret.

Configure these repository variables under **Settings > Secrets and variables >
Actions > Variables**:

| Variable | Value |
|---|---|
| `AZURE_CLIENT_ID` | Federated identity's client ID |
| `AZURE_TENANT_ID` | Foundry tenant ID |
| `AZURE_SUBSCRIPTION_ID` | Foundry subscription ID |
| `FOUNDRY_PROJECT_ENDPOINT` | Foundry project endpoint |
| `FOUNDRY_MODEL` | Agent model deployment |
| `FOUNDRY_JUDGE_MODEL` | Judge model deployment; optional if using the agent model |
| `FOUNDRY_AGENT_NAME` | Existing prompt agent name; needed for `prompt` mode |
| `FOUNDRY_AGENT_VERSION` | Existing prompt agent version; needed for `prompt` mode |

Open **Actions > .NET Foundry evaluations > Run workflow** and choose `maf`,
`maf-simple`, `maf-simple-negative`, `prompt`, or `negative`. The default runner is GitHub-hosted. The
`temporary-validation` option supports a provisioned Windows x64 self-hosted
runner labeled `dotnet-eval-validation`.

Each run provides a job summary with scores and a Foundry report link,
individual case results in the **Foundry quality tests** check, and JSON,
Markdown, and JUnit artifacts. Set `upload_artifacts` to false to omit
downloadable files; scoring, quality enforcement, summaries, and test checks
remain enabled.

Use synthetic data for this public example: GitHub logs, summaries, checks,
and artifacts can expose questions, responses, errors, and report links.
Model calls and evaluations incur Azure consumption charges.

## Dependencies and verification

Use any supported .NET 10 SDK feature band. Package versions are declared in
the project files, as in a normal .NET application. The Foundry integration
currently uses preview APIs. GitHub Actions are pinned to commit hashes for
supply-chain integrity.

Both projects build using NuGet dependencies.
Earlier runs verified complete MAF and prompt-agent scores, a failing negative
quality gate, and GitHub summaries and individual-test checks. Those live runs
preceded the current root-agent/`tests` layout. The current package-based
negative test also produced all four scores of 1 and failed the quality gate
as expected. Its local positive MAF run had three scores and one evaluator
authentication error. The concise example subsequently produced a passing
local run as well as an incomplete run, and passed both cases in a private
GitHub Actions run; reliable execution remains to be established.

## References

- [C# cloud dataset evaluation](https://learn.microsoft.com/en-us/azure/foundry/observability/how-to/cloud-evaluation-datasets)
- [Official simple .NET evaluation sample](https://github.com/microsoft/agent-framework/tree/main/dotnet/samples/02-agents/Evaluation/Evaluation_SimpleEval)
- [Official existing-agent rubric evaluation sample](https://github.com/microsoft/agent-framework/tree/main/dotnet/samples/05-end-to-end/Evaluation/Evaluation_FoundryRubric)

## Disclaimer

This Sample Code is provided for the purpose of illustration only and is not
intended to be used in a production environment. THIS SAMPLE CODE AND ANY RELATED
INFORMATION ARE PROVIDED "AS IS" WITHOUT WARRANTY OF ANY KIND, EITHER EXPRESSED
OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE IMPLIED WARRANTIES OF
MERCHANTABILITY AND/OR FITNESS FOR A PARTICULAR PURPOSE.
