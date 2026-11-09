using System.Globalization;
using System.ClientModel;
using System.ClientModel.Primitives;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Azure.AI.Projects;
using Azure.Identity;

public sealed record EvaluationRun(string EvalId, string RunId, string AgentType, string? AgentName, string? AgentVersion = null);
public sealed record EvaluationOutcome(bool Complete, bool QualityGatePassed, string? Error = null);

public static class EvaluationReport
{
    public static async Task<EvaluationOutcome> RunAsync(
        string testName, string[] queries, string[] expectedEvaluators,
        Func<AIProjectClient, EvaluationSettings, Task<EvaluationRun>> evaluate)
    {
        string output = Environment.GetEnvironmentVariable("EVAL_OUTPUT") ?? "results";
        DateTimeOffset startedAt = DateTimeOffset.UtcNow;
        try
        {
            EvaluationSettings settings = EvaluationSettings.Load();
            string model = settings.Model;
            string judgeModel = settings.JudgeModel;
            AIProjectClient project = settings.CreateProject();
            Console.WriteLine($"Running {testName}; model: {model}; judge: {judgeModel}");
            EvaluationRun evaluation = await evaluate(project, settings);
            var evalClient = project.GetProjectOpenAIClient().GetEvaluationClient();
            string evalId = evaluation.EvalId;
            string runId = evaluation.RunId;
            Console.WriteLine($"Eval: {evalId}; run: {runId}");
            DateTimeOffset deadline = DateTimeOffset.UtcNow.AddMinutes(15);
            JsonElement run;
            do
            {
                var polled = await evalClient.GetEvaluationRunAsync(evalId, runId, new RequestOptions());
                using JsonDocument polledJson = JsonDocument.Parse(polled.GetRawResponse().Content);
                run = polledJson.RootElement.Clone();
                if (run.GetProperty("status").GetString() is "completed" or "failed" or "canceled")
                {
                    break;
                }
                if (DateTimeOffset.UtcNow >= deadline)
                {
                    throw new TimeoutException($"Foundry run {runId} did not finish within 15 minutes.");
                }
                await Task.Delay(TimeSpan.FromSeconds(10));
            } while (true);
            var counts = run.GetProperty("result_counts");
            var results = new
            {
                EvalId = evalId,
                RunId = runId,
                Status = run.GetProperty("status").GetString(),
                Error = run.TryGetProperty("error", out var runError) && runError.ValueKind != JsonValueKind.Null
                    ? runError.ToString() : null,
                ReportUrl = run.TryGetProperty("report_url", out var report) ? report.GetString() : null,
                Total = counts.GetProperty("total").GetInt32(),
                Passed = counts.GetProperty("passed").GetInt32()
            };

            Directory.CreateDirectory(output);
            await File.WriteAllTextAsync(Path.Combine(output, $"{testName}-run.json"),
                JsonSerializer.Serialize(run, new JsonSerializerOptions { WriteIndented = true }));
            var rawItems = new List<JsonElement>();
            string? cursor = null;
            do
            {
                var page = await evalClient.GetEvaluationRunOutputItemsAsync(
                    results.EvalId!, results.RunId!, limit: 100, order: null, after: cursor,
                    outputItemStatus: null, new RequestOptions());
                using JsonDocument document = JsonDocument.Parse(page.GetRawResponse().Content);
                rawItems.AddRange(document.RootElement.GetProperty("data").EnumerateArray().Select(item => item.Clone()));
                bool more = document.RootElement.TryGetProperty("has_more", out var hasMore) && hasMore.GetBoolean();
                cursor = more ? document.RootElement.GetProperty("last_id").GetString()
                    ?? throw new InvalidOperationException("Foundry pagination returned a null cursor.") : null;
            } while (cursor is not null);
            await File.WriteAllTextAsync(Path.Combine(output, $"{testName}-raw.json"),
                JsonSerializer.Serialize(rawItems, new JsonSerializerOptions { WriteIndented = true }));
            var rows = rawItems.OrderBy(item => item.GetProperty("datasource_item_id").GetInt32()).Select(item => new
            {
                ItemId = item.GetProperty("datasource_item_id").GetInt32(),
                Status = item.GetProperty("status").GetString(),
                Query = item.GetProperty("datasource_item").GetProperty("query").GetString(),
                Response = item.GetProperty("datasource_item").GetProperty("response").GetString(),
                Scores = item.GetProperty("results").EnumerateArray().Select(score => new
                {
                    Name = score.GetProperty("name").GetString(),
                    Score = score.TryGetProperty("score", out var value) && value.ValueKind == JsonValueKind.Number &&
                        value.TryGetDouble(out double number)
                        ? (double?)number : null,
                    Passed = score.TryGetProperty("passed", out var flag) && flag.ValueKind is JsonValueKind.True or JsonValueKind.False
                        ? (bool?)flag.GetBoolean() : null,
                    Reason = score.TryGetProperty("reason", out var reason) ? reason.GetString() : null,
                    Error = score.TryGetProperty("sample", out var sample) && sample.TryGetProperty("error", out var error)
                        && error.ValueKind != JsonValueKind.Null ? error.ToString() : null
                }).ToArray()
            }).ToArray();
            bool complete = results.Status == "completed" && results.Error is null &&
                results.Total == queries.Length && rows.Length == queries.Length && rawItems.Count == queries.Length &&
                results.ReportUrl is not null && !string.IsNullOrEmpty(results.EvalId) &&
                !string.IsNullOrEmpty(results.RunId) &&
                rawItems.All(item => item.GetProperty("results").EnumerateArray().All(score =>
                    score.TryGetProperty("score", out var value) && value.ValueKind == JsonValueKind.Number &&
                    score.TryGetProperty("passed", out var flag) && flag.ValueKind is JsonValueKind.True or JsonValueKind.False &&
                    (!score.TryGetProperty("status", out var status) || status.GetString() != "error"))) &&
                rows.Select(row => row.ItemId).SequenceEqual(Enumerable.Range(0, queries.Length)) &&
                rows.Select(row => row.Query).SequenceEqual(queries) &&
                rows.All(row => row.Status == "completed" &&
                    row.Scores.Length == expectedEvaluators.Length &&
                    row.Scores.Select(score => score.Name).Order().SequenceEqual(expectedEvaluators.Order()) &&
                    row.Scores.All(score => score.Error is null && score.Score is { } number &&
                        double.IsFinite(number) && score.Passed is not null));
            bool passed = complete && results.Passed == results.Total &&
                rows.All(row => row.Scores.All(score => score.Passed == true && score.Score >= 3));
            int passedCases = rows.Count(row => row.Status == "completed" &&
                row.Scores.Length == expectedEvaluators.Length &&
                row.Scores.Select(score => score.Name).Order().SequenceEqual(expectedEvaluators.Order()) &&
                row.Scores.All(score => score.Error is null && score.Passed == true && score.Score >= 3));

            var evidence = new
            {
                Test = testName,
                evaluation.AgentType,
                evaluation.AgentName,
                evaluation.AgentVersion,
                Model = model,
                JudgeModel = judgeModel,
                CompletedAt = DateTimeOffset.UtcNow,
                results.EvalId,
                results.RunId,
                results.Status,
                results.Error,
                results.ReportUrl,
                results.Total,
                ServiceReportedPassed = results.Passed,
                PassedCases = passedCases,
                Complete = complete,
                QualityGatePassed = passed,
                Items = rows
            };
            await File.WriteAllTextAsync(Path.Combine(output, $"{testName}.json"),
                JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true }));

            StringBuilder summary = new();
            summary.AppendLine($"## .NET {testName} evaluation");
            summary.AppendLine($"Agent: `{evaluation.AgentType}`. Foundry status: `{results.Status}`.");
            summary.AppendLine($"Agent model: `{model}`. Judge deployment: `{judgeModel}`.");
            summary.AppendLine($"Quality gate: **{(passed ? "PASS" : "FAIL")}**; {passedCases}/{queries.Length} cases passed all required evaluators.");
            summary.AppendLine($"[Open Foundry evaluation report]({results.ReportUrl})");
            summary.AppendLine();
            summary.AppendLine("| Case | Evaluator | Score | Passed |");
            summary.AppendLine("|---|---|---:|---|");
            foreach (var row in rows)
            {
                foreach (var score in row.Scores)
                {
                    summary.AppendLine($"| {row.Query} | {score.Name} | {score.Score?.ToString("F1", CultureInfo.InvariantCulture) ?? "UNSCORED"} | {score.Passed} |");
                }
            }
            if (!complete)
            {
                summary.AppendLine("ERROR: Incomplete, errored, missing, or unscored results. Failing closed.");
            }
            await File.WriteAllTextAsync(Path.Combine(output, $"{testName}-summary.md"), summary.ToString());
            if (Environment.GetEnvironmentVariable("GITHUB_STEP_SUMMARY") is { Length: > 0 } githubSummary)
            {
                await File.AppendAllTextAsync(githubSummary, summary.ToString());
            }

            XElement suite = new("testsuite", new XAttribute("name", $"dotnet-{testName}"),
                new XAttribute("tests", queries.Length),
                new XAttribute("time", (DateTimeOffset.UtcNow - startedAt).TotalSeconds.ToString("F3", CultureInfo.InvariantCulture)),
                new XAttribute("failures", complete ? queries.Length - passedCases : 0),
                new XAttribute("errors", complete ? 0 : queries.Length));
            for (int index = 0; index < queries.Length; index++)
            {
                XElement test = new("testcase", new XAttribute("classname", $"Foundry.{testName}"),
                    new XAttribute("name", queries[index]), new XAttribute("time", "0"));
                var row = index < rows.Length ? rows[index] : null;
                if (!complete || row is null || row.Scores.Any(score => score.Passed != true || score.Score < 3))
                {
                    test.Add(new XElement(complete ? "failure" : "error",
                        new XAttribute("message", complete ? "Foundry quality gate failed" : "Evaluation results incomplete or errored"),
                        row is null ? "Missing results." : JsonSerializer.Serialize(row)));
                }
                suite.Add(test);
            }
            new XDocument(new XElement("testsuites", suite)).Save(Path.Combine(output, $"{testName}-junit.xml"));
            Console.WriteLine(summary);
            Console.WriteLine($"Eval: {results.EvalId}; run: {results.RunId}");
            return new EvaluationOutcome(complete, passed, results.Error);
        }
        catch (Exception exception) when (exception is ClientResultException or AuthenticationFailedException
            or TimeoutException or InvalidOperationException)
        {
            Directory.CreateDirectory(output);
            string detail = $"{exception.GetType().Name}: {exception.Message}";
            Console.Error.WriteLine(detail);
            await File.WriteAllTextAsync(Path.Combine(output, $"{testName}.json"),
                JsonSerializer.Serialize(new
                {
                    Test = testName,
                    Complete = false,
                    QualityGatePassed = false,
                    Error = detail,
                    CompletedAt = DateTimeOffset.UtcNow
                }, new JsonSerializerOptions { WriteIndented = true }));
            string summary = $"## .NET {testName} evaluation\n**ERROR: evaluation did not produce complete scored results.**\n\n{detail}\n";
            await File.WriteAllTextAsync(Path.Combine(output, $"{testName}-summary.md"), summary);
            if (Environment.GetEnvironmentVariable("GITHUB_STEP_SUMMARY") is { Length: > 0 } path)
            {
                await File.AppendAllTextAsync(path, summary);
            }
            XElement suite = new("testsuite", new XAttribute("name", $"dotnet-{testName}"),
                new XAttribute("tests", queries.Length), new XAttribute("errors", queries.Length),
                new XAttribute("time", (DateTimeOffset.UtcNow - startedAt).TotalSeconds.ToString("F3", CultureInfo.InvariantCulture)));
            foreach (string query in queries)
            {
                suite.Add(new XElement("testcase", new XAttribute("classname", $"Foundry.{testName}"),
                    new XAttribute("name", query), new XAttribute("time", "0"),
                    new XElement("error", new XAttribute("message", "Evaluation infrastructure error"), detail)));
            }
            new XDocument(new XElement("testsuites", suite)).Save(Path.Combine(output, $"{testName}-junit.xml"));
            return new EvaluationOutcome(false, false, detail);
        }
    }
}
