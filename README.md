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
| `tests/AgentEvaluations.Tests.csproj` | One xUnit test project referencing the application. |
| `tests/MafExplicitTests.cs` | Evaluates the application agent with an explicit question/answer loop and Foundry submission. |
| `tests/MafHelperTests.cs` | Evaluates the application agent using `agent.EvaluateAsync`. |
| `tests/PromptAgentTests.cs` | Loads an existing Foundry prompt agent version, invokes it, and submits its answers for evaluation. |
| `tests/MafExplicitNegativeTests.cs` | Asserts that explicit evaluation rejects an intentionally bad agent. |
| `tests/MafHelperNegativeTests.cs` | Asserts that helper evaluation rejects an intentionally bad agent. |
| `tests/EvaluationReportTests.cs` | Offline tests for fail-closed configuration errors and reports. |
| `tests/Shared/ExplicitEvaluation.cs` | Submits collected answers to Foundry; does not create or invoke agents. |
| `tests/Shared/EvaluationReport.cs` | Retrieves raw scores, enforces the quality gate, and writes reports; does not select a test or approach. |
| `tests/Shared/EvaluationSettings.cs` | Reads configuration and creates an authenticated project client. |
| `tools/CreatePromptAgent/Program.cs` | Creates the optional example prompt agent separately from tests. |
| `.github/workflows/evaluate.yml` | Runs the three positive xUnit tests. |
| `.github/workflows/evaluate-negative.yml` | Runs the two negative xUnit tests. |
| `.github/workflows/evaluate-project.yml` | Shared CI setup, `dotnet test`, and TRX reporting. |

All tests belong to one normal xUnit project. Each live test method owns its
questions, evaluators, agent construction, evaluation calls, and assertions.
There is no mode argument or dispatcher. Shared source files handle configuration, submission of
collected answers, and result validation/reporting, without knowing which test
or agent implementation is running. Tests run sequentially to avoid competing
summary writes and concurrent judge load.

To evaluate your own MAF application, reference its project from
`tests/AgentEvaluations.Tests.csproj`
and update the agent factory call in `MafExplicitTests.cs` or `MafHelperTests.cs`. Your
application's instructions and tools stay in the application; each test owns
its questions and scoring.

## Run locally

### Prerequisites

- .NET 10 SDK, Git, and Azure CLI.
- A Foundry project with an agent model deployment and a judge model deployment
  suitable for relevance and coherence evaluation.
- Foundry User access on the parent Foundry account for the identity running
  evaluations. See [evaluation permissions](https://learn.microsoft.com/en-us/azure/foundry/observability/how-to/evaluation-permissions).

All projects use NuGet packages. `dotnet run` and `dotnet test` restore dependencies
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
$env:EVAL_OUTPUT = Join-Path (Get-Location) 'results'
```

`FOUNDRY_JUDGE_MODEL` defaults to `FOUNDRY_MODEL` when omitted. A standard Azure
CLI sign-in is sufficient for credential acquisition; a custom
`AZURE_CONFIG_DIR` is optional for people who maintain separate CLI profiles.

### Run the agent, then evaluate it

```powershell
dotnet run --project MafAgent.csproj -- "What is photosynthesis?"
dotnet test tests\AgentEvaluations.Tests.csproj --filter FullyQualifiedName~MafExplicitTests
```

The first command prints the agent's answer. The second asks both test questions
and submits the answers to Foundry for scoring. Results are written to `results/`
as JSON, Markdown, and per-question JUnit XML. xUnit asserts both complete
scoring and a passing quality gate. `dotnet test` returns nonzero for failed tests.
Each live test writes its own report prefix (`maf-explicit`, `maf-helper`,
`prompt-agent`, `maf-explicit-negative`, or `maf-helper-negative`).
The JSON `Test` field identifies the evaluation.

### Evaluate the MAF agent with less orchestration

```powershell
dotnet test tests\AgentEvaluations.Tests.csproj --filter FullyQualifiedName~MafHelperTests
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
dotnet test tests\AgentEvaluations.Tests.csproj --filter FullyQualifiedName~PromptAgentTests
```

Or create the example science prompt agent once:

```powershell
$env:FOUNDRY_AGENT_NAME = 'dotnet-eval-smoke'
dotnet run --project tools\CreatePromptAgent\CreatePromptAgent.csproj
$env:FOUNDRY_AGENT_VERSION = '<version printed by setup>'
dotnet test tests\AgentEvaluations.Tests.csproj --filter FullyQualifiedName~PromptAgentTests
```

To verify rejection by the quality gate, run the negative tests. Each creates an agent
that answers both questions with "Banana spaceship purple socks."

```powershell
dotnet test tests\AgentEvaluations.Tests.csproj --filter Category=Negative
```

These xUnit tests **pass when complete scores reject the bad answers**. Missing
scores or service errors fail the tests; an infrastructure error is never
accepted as evidence of rejection. Their per-question evaluation JUnit files
still describe the rejected answers as failures. Actions publishes the xUnit
TRX results, so a correctly functioning negative test is green.

Run all tests, only positive live tests, or only offline reporting tests:

```powershell
dotnet test tests\AgentEvaluations.Tests.csproj
dotnet test tests\AgentEvaluations.Tests.csproj --filter Category=Positive
dotnet test tests\AgentEvaluations.Tests.csproj --filter Category=Offline
```

Only the offline tests run without Azure configuration. Unfiltered execution
runs all live evaluations as well and requires the prompt-agent name/version.

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
using GitHub Actions workload identity. The helper approach also passed
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
| `FOUNDRY_AGENT_NAME` | Existing prompt agent name; needed only by the `PromptAgent` test |
| `FOUNDRY_AGENT_VERSION` | Existing prompt agent version; needed only by the `PromptAgent` test |

Open **Actions > .NET Foundry evaluations > Run workflow** to run `MafExplicit`,
`MafHelper`, and `PromptAgent` as separate tests in one test project. A failure
does not stop the remaining tests. Configure the existing prompt agent before running this
workflow; the tests do not create or modify it.

The separate **.NET Foundry negative quality gates** workflow runs
`MafExplicitNegativeTests` and `MafHelperNegativeTests`. These tests assert that
the quality gate rejects bad answers, so their jobs should be green when the
gate works correctly and red for incomplete scoring or unexpected acceptance.

The default runner is GitHub-hosted. The
`temporary-validation` option supports a provisioned Windows x64 self-hosted
runner labeled `dotnet-eval-validation`.

Each run provides summaries with scores and Foundry report links,
individual xUnit test results in its **Foundry evaluation tests** check, and JSON,
Markdown, per-question JUnit, and xUnit TRX artifacts. Set `upload_artifacts` to false to omit
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

The application and test projects build using NuGet dependencies.
Earlier runs verified complete MAF and prompt-agent scores, a failing negative
quality gate, and GitHub summaries and individual-test checks. Those live runs
preceded the current root-agent/`tests` layout. The current package-based
negative test also produced all four scores of 1 and failed the quality gate
as expected. Its local positive MAF run had three scores and one evaluator
authentication error. The concise example subsequently produced a passing
local run as well as an incomplete run, and passed both cases in a private
GitHub Actions run; reliable execution remains to be established.
After consolidation into one xUnit project, a full local run against Azure on
2026-11-09 passed all 10 tests: three positive live evaluations, two negative
live evaluations, and five offline reporting cases. All 20 live evaluator scores
were present: positive scores ranged from 4 to 5; both negative agents scored 1
on all criteria and were correctly rejected. JSON, Markdown, per-question JUnit,
the combined GitHub summary, and native xUnit TRX results were verified.
This successful run does not establish that the earlier intermittent service
error is resolved. The xUnit workflow also ran in a private GitHub Actions
repository: nine of ten tests passed, including both MAF approaches, both
negative tests, and all offline reporting cases. The prompt-agent test failed
closed on an Azure model rate-limit response (HTTP 429); native TRX test
reporting worked. This is not a fully passing CI run.

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
