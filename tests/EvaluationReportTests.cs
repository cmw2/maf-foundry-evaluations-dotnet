using System.Text.Json;
using System.Xml.Linq;

public sealed class EvaluationReportTests
{
    [Theory]
    [Trait("Category", "Offline")]
    [InlineData("maf-explicit")]
    [InlineData("maf-helper")]
    [InlineData("prompt-agent")]
    [InlineData("maf-explicit-negative")]
    [InlineData("maf-helper-negative")]
    public async Task Missing_configuration_is_an_infrastructure_error(string testName)
    {
        string output = Path.Combine(Path.GetTempPath(), $"foundry-report-test-{Guid.NewGuid():N}");
        string[] variables = ["FOUNDRY_MODEL", "EVAL_OUTPUT", "GITHUB_STEP_SUMMARY"];
        var saved = variables.ToDictionary(name => name, Environment.GetEnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable("FOUNDRY_MODEL", null);
            Environment.SetEnvironmentVariable("EVAL_OUTPUT", output);
            Environment.SetEnvironmentVariable("GITHUB_STEP_SUMMARY", Path.Combine(output, "github-summary.md"));
            string[] questions = ["What is photosynthesis?", "How do vaccines work?"];
            EvaluationOutcome outcome = await EvaluationReport.RunAsync(
                testName, questions, ["relevance", "coherence"],
                (_, _) => throw new InvalidOperationException("Agent invocation must not occur without configuration."));

            Assert.False(outcome.Complete);
            Assert.False(outcome.QualityGatePassed);
            Assert.Contains("FOUNDRY_MODEL is required", outcome.Error);
            using JsonDocument evidence = JsonDocument.Parse(
                await File.ReadAllTextAsync(Path.Combine(output, $"{testName}.json")));
            Assert.Equal(testName, evidence.RootElement.GetProperty("Test").GetString());
            Assert.False(evidence.RootElement.GetProperty("Complete").GetBoolean());
            Assert.False(evidence.RootElement.GetProperty("QualityGatePassed").GetBoolean());
            XDocument junit = XDocument.Load(Path.Combine(output, $"{testName}-junit.xml"));
            XElement suite = Assert.Single(junit.Root!.Elements("testsuite"));
            Assert.Equal("2", suite.Attribute("errors")!.Value);
            Assert.Equal(2, suite.Elements("testcase").Count());
            Assert.All(suite.Elements("testcase"), test => Assert.NotNull(test.Element("error")));
            Assert.Contains("ERROR: evaluation did not produce complete scored results",
                await File.ReadAllTextAsync(Path.Combine(output, "github-summary.md")));
        }
        finally
        {
            foreach (var (name, value) in saved)
            {
                Environment.SetEnvironmentVariable(name, value);
            }
            if (Directory.Exists(output))
            {
                Directory.Delete(output, recursive: true);
            }
        }
    }
}
