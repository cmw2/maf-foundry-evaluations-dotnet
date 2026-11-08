# .NET + Foundry evaluation validation

Small synthetic smoke test, not a production quality benchmark. No customer
data, Python source, API keys, or credential caches are included.

This adapter uses Microsoft Agent Framework's .NET agents at commit
`924a6e50ec0336f3c965ee8b1151c54085888b6e`, and the .NET Foundry/OpenAI
evaluation protocol methods:

- `maf`: evaluates the independently runnable MAF application at the repository root;
  Foundry scores its outputs.
- `prompt`: a .NET client retrieves and invokes an existing Foundry prompt agent by
  name and pinned version; Foundry scores its outputs.
- `negative`: deliberately irrelevant agent responses must produce a failed
  quality gate, rather than a successful-looking workflow.
- `setup`: create a synthetic prompt agent version once, before configuring CI.
  CI does not create or update agents.

The two test questions are the science questions from the official simple
evaluation sample. Relevance and coherence are evaluated server-side.
The cloud request follows the current
[C# dataset-evaluation documentation](https://learn.microsoft.com/en-us/azure/foundry/observability/how-to/cloud-evaluation-datasets),
including `initialization_parameters.model`. The pinned `FoundryEvals` helper
instead serializes `deployment_name`; the adapter does not modify the pinned
Microsoft source or claim to be that unchanged sample.
The quality gate requires complete scored results, both evaluator pass flags,
and every score >= 3. Missing or errored scores fail closed.
Raw output items, evaluator reasons/errors, run metadata, and per-case identities
are preserved. A service status of `completed` or a nonzero aggregate passed
count alone is not sufficient: every required evaluator on every case must score.

## Code layout

- `ScienceAgent.cs`: application-owned MAF agent behavior. It has no
  test questions, judge configuration, scoring, or reporting code.
- `Program.cs` and `MafAgent.csproj`: standalone application entry point and
  project. Ask it any question
  without running an evaluation.
- `tests/Program.cs` and `tests/EvalValidation.csproj`: evaluation entry point
  and project; selects `maf`, `prompt`, `negative`, or `setup`.
- `tests/MafEvaluation.cs`: evaluation-side adapter that references the application's
  agent factory. The deliberately bad agent exists only in this test adapter.
- `tests/PromptEvaluation.cs`: retrieves a pinned Foundry prompt agent, or creates
  the synthetic version for the one-time `setup` command.
- `tests/EvaluationRunner.cs`: shared test questions, agent invocation, Foundry
  evaluation submission/polling, strict quality gate, and JSON/Markdown/JUnit
  reporting. Both agent paths use the same scoring and reporting behavior.

Both projects explicitly list their own source files instead of compiling each
other's code or the nested Microsoft reference checkout.
The agent is a separate project referenced by the evaluation executable, not
defined by it. Both entry points use the same application agent implementation.

## Run the MAF application first

With your approved `AZURE_CONFIG_DIR`, `FOUNDRY_PROJECT_ENDPOINT`, and
`FOUNDRY_MODEL` configured, run the application independently:

```powershell
dotnet run --project MafAgent.csproj -- "What is photosynthesis?"
```

This prints the agent's answer and does not submit an evaluation. It needs no
judge deployment configuration or test reporting. Then, from the same folder:

```powershell
dotnet run --project tests\EvalValidation.csproj -- maf
```

The separate evaluation executable invokes that same agent over the test
questions and scores its responses. For a customer's existing MAF application,
replace the agent project reference and the factory call in
`tests\MafEvaluation.cs` with their agent implementation. Agent instructions and
tools belong in their application, not in the evaluation harness.

## Local execution

The verified route is GitHub Actions workload identity, described below.
Interactive-user Azure CLI evaluations in the validation environment produced
incomplete scores with nested authentication errors even though agent and
direct judge inference succeeded. Their root cause remains unresolved; these
local instructions are not a certified passing user-authentication recipe.

Requires .NET SDK 10.0.403 and an authenticated Azure CLI profile. Set
`AZURE_CONFIG_DIR` explicitly to your approved profile; do not sign in to a
different account just to run the example.

Clone Microsoft's repository into the sibling `agent-framework-validation`
directory and check out the pinned commit above. From this folder:

```powershell
$env:AZURE_CONFIG_DIR = '<approved Azure CLI profile directory>'
$env:FOUNDRY_PROJECT_ENDPOINT = 'https://<resource>.services.ai.azure.com/api/projects/<project>'
$env:FOUNDRY_MODEL = '<existing model deployment>'
$env:FOUNDRY_JUDGE_MODEL = '<evaluation judge deployment>'
$env:FOUNDRY_AGENT_NAME = 'dotnet-eval-smoke'
dotnet run --project tests\EvalValidation.csproj -- setup
$env:FOUNDRY_AGENT_VERSION = '<version printed by setup>'
dotnet run --project tests\EvalValidation.csproj -- maf
dotnet run --project tests\EvalValidation.csproj -- prompt
dotnet run --project tests\EvalValidation.csproj -- negative
```

Alternatively clone the reference repository into `reference` under this
folder and supply `-p:ReferenceRoot=reference`.

## GitHub Actions

Configure a dedicated federated Azure identity trusted only for this repository's
`main` branch. Assign Foundry User on the parent Foundry account: a project-only
assignment does not authorize the evaluator's model inference.
Owner by itself does not grant the required data-plane access.
See the [evaluation permissions guidance](https://learn.microsoft.com/en-us/azure/foundry/observability/how-to/evaluation-permissions).

Use only synthetic data in a public repository: workflow logs, summaries, test
reports, and downloadable artifacts can expose questions, responses, errors,
and Foundry report links. Never commit credentials or Azure CLI profile files.

Set repository variables `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`,
`AZURE_SUBSCRIPTION_ID`, `FOUNDRY_PROJECT_ENDPOINT`, `FOUNDRY_MODEL`,
`FOUNDRY_AGENT_NAME`, and `FOUNDRY_AGENT_VERSION`.
Set `FOUNDRY_JUDGE_MODEL` to a separately selected judge deployment when needed;
when unset or empty, it defaults to `FOUNDRY_MODEL`. This does not change the
prompt agent's pinned version or application model.

Dispatch `.NET Foundry evaluations` with a mode. Each run publishes:

1. A native GitHub job summary with scores and the Foundry-provided report URL.
2. Individual case results in a check run through `dorny/test-reporter`.
3. JSON, Markdown, and JUnit artifacts, including on quality-gate failure.

Artifact upload is enabled by default and fails explicitly if GitHub storage
is unavailable. Set the dispatch input `upload_artifacts` to false only when
intentionally omitting downloadable artifacts, such as during a storage-quota
incident. Foundry publication, quality enforcement, and GitHub summaries/test
reporting remain enabled; this switch never turns an upload failure into success.

JUnit is generated by .NET; this is not a TRX or Azure DevOps Test tab demonstration.
Suite duration covers the whole evaluation; individual case durations are not
separately measured and use zero rather than producing a `NaN` display.
GitHub does not have the same built-in test dashboard as Azure DevOps.
The workflow runs only on explicit dispatch, not on untrusted pull requests.
The default is a GitHub-hosted runner. Where hosted runners are disabled but
self-hosted runners are permitted, `temporary-validation` selects an explicitly
provisioned Windows x64 runner with the `dotnet-eval-validation` label. Only run
trusted synthetic code on such a runner; this is not isolation from other files
on its host. Use an ephemeral runner and remove its working directory afterward.
The SDK uses the runner's tool cache and the Azure CLI profile uses the job
workspace, not the host's default profile. No persistent runner service is required.
Match the federated subject to the repository's actual OIDC assertion. Some
GitHub accounts include immutable owner/repository IDs in that subject, so a
name-only `repo:owner/repository:ref:refs/heads/main` string can fail with
`AADSTS700213`. Keep the trust bound to the exact repository and branch.

## Scope and limitations

### Validation history

An earlier version of this adapter produced complete MAF and prompt-agent
scores with workload identity and correctly failed a fully scored negative
case. GitHub displayed native summaries and individual-test checks. Those
runs preceded the current project layout and do not establish that this
reorganized version has passed end to end. Foundry results were retrieved
through .NET; the portal UI was not visually verified. Artifact upload was
omitted in those demonstrations because the test account's storage quota was
exhausted. Earlier interactive-user evaluation runs produced incomplete scores
with authentication errors; the cause remains unresolved.

### Integration boundaries

The reference is built from pinned Microsoft source because these samples use
preview APIs and source project references. This is a validation adapter, not an
unchanged official quickstart or a stable package-only recipe. Azure.AI.Projects
3.0.0-beta.5 and related APIs may change. Source builds and LLM judge calls add
latency; model calls and evaluations incur consumption charges.

No tool-call, multi-turn, safety, production-trace, custom-rubric, or statistical
regression coverage is implied by this two-case smoke test. The alternative
`microsoft/ai-agent-evals` action uses Python internally; this workflow does not
use that action. Foundry itself implements its server-side evaluators.

Official starting points:

- [Simple .NET evaluation](https://github.com/microsoft/agent-framework/tree/924a6e50ec0336f3c965ee8b1151c54085888b6e/dotnet/samples/02-agents/Evaluation/Evaluation_SimpleEval)
- [Existing-agent rubric evaluation](https://github.com/microsoft/agent-framework/tree/924a6e50ec0336f3c965ee8b1151c54085888b6e/dotnet/samples/05-end-to-end/Evaluation/Evaluation_FoundryRubric)
