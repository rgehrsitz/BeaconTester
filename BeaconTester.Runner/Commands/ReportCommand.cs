using System.CommandLine;
using System.Text.Json;
using BeaconTester.Core.Models;
using Serilog;

namespace BeaconTester.Runner.Commands
{
    /// <summary>
    /// Command to generate reports from test results
    /// </summary>
    public class ReportCommand
    {
        /// <summary>
        /// Creates the report command
        /// </summary>
        public Command Create()
        {
            var command = new Command("report", "Generate reports from test results");

            // Add options
            var resultsOption = new Option<string>(
                name: "--results",
                description: "Path to the test results JSON file"
            )
            {
                IsRequired = true,
            };

            var outputOption = new Option<string>(
                name: "--output",
                description: "Path to output the report"
            )
            {
                IsRequired = true,
            };

            var formatOption = new Option<string>(
                name: "--format",
                description: "Report format (text, html, markdown)"
            )
            {
                IsRequired = false,
            };

            command.AddOption(resultsOption);
            command.AddOption(outputOption);
            command.AddOption(formatOption);

            // Set handler
            command.SetHandler(
                (resultsPath, outputPath, format) =>
                    HandleReportCommand(resultsPath, outputPath, format ?? "text"),
                resultsOption,
                outputOption,
                formatOption
            );

            return command;
        }

        /// <summary>
        /// Handles the report command
        /// </summary>
        private async Task<int> HandleReportCommand(
            string resultsPath,
            string outputPath,
            string format
        )
        {
            var logger = Log.Logger.ForContext<ReportCommand>();

            try
            {
                logger.Information(
                    "Generating {Format} report from {ResultsPath}",
                    format,
                    resultsPath
                );

                // Check if results file exists
                if (!File.Exists(resultsPath))
                {
                    logger.Error("Results file not found: {ResultsPath}", resultsPath);
                    return 1;
                }

                // Create directory for output if it doesn't exist
                var outputDir = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
                {
                    Directory.CreateDirectory(outputDir);
                }

                // Load test results
                string json = await File.ReadAllTextAsync(resultsPath);
                var options = new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                };

                var resultsDocument = JsonSerializer.Deserialize<ResultsDocument>(json, options);

                if (
                    resultsDocument == null
                    || resultsDocument.Results == null
                    || resultsDocument.Results.Count == 0
                )
                {
                    logger.Error("No test results found in {ResultsPath}", resultsPath);
                    return 1;
                }

                // Generate report based on format
                string report = format.ToLowerInvariant() switch
                {
                    "html" => GenerateHtmlReport(resultsDocument.Results),
                    "markdown" => GenerateMarkdownReport(resultsDocument.Results),
                    _ => GenerateTextReport(resultsDocument.Results),
                };

                // Write report to file
                await File.WriteAllTextAsync(outputPath, report);

                logger.Information("Report generated and saved to {OutputPath}", outputPath);
                return 0;
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error generating report");
                return 1;
            }
        }

        /// <summary>
        /// Generates a text report
        /// </summary>
        private string GenerateTextReport(List<TestResult> results)
        {
            var report = new System.Text.StringBuilder();

            report.AppendLine("BeaconTester Test Report");
            report.AppendLine("======================");
            report.AppendLine();

            // Summary
            var successCount = results.Count(r => r.Success);
            var failureCount = results.Count - successCount;
            var totalDuration = TimeSpan.FromMilliseconds(
                results.Sum(r => r.Duration.TotalMilliseconds)
            );

            report.AppendLine($"Summary: {successCount} passed, {failureCount} failed");
            report.AppendLine($"Total duration: {totalDuration.TotalSeconds:F2} seconds");
            report.AppendLine();

            // Results details
            report.AppendLine("Test Results:");
            report.AppendLine("------------");

            foreach (var result in results)
            {
                report.AppendLine($"Test: {result.Name}");
                report.AppendLine($"  Status: {(result.Success ? "PASSED" : "FAILED")}");
                report.AppendLine($"  Duration: {result.Duration.TotalMilliseconds:F2} ms");

                if (!result.Success)
                {
                    report.AppendLine(
                        $"  Error: {result.ErrorMessage ?? "Test assertions failed"}"
                    );

                    // Details of failed steps
                    var failedSteps = result.StepResults.Where(s => !s.Success).ToList();
                    if (failedSteps.Count > 0)
                    {
                        report.AppendLine("  Failed Steps:");

                        foreach (var step in failedSteps)
                        {
                            report.AppendLine(
                                $"    - Step: {step.ExpectationResults.FirstOrDefault()?.Key ?? "Unknown"}"
                            );

                            foreach (
                                var expectation in step.ExpectationResults.Where(e => !e.Success)
                            )
                            {
                                report.AppendLine($"      Key: {expectation.Key}");
                                report.AppendLine($"      Expected: {expectation.Expected}");
                                report.AppendLine($"      Actual: {expectation.Actual}");
                            }
                        }
                    }
                }

                report.AppendLine();
            }

            return report.ToString();
        }

        /// <summary>
        /// Generates a markdown report
        /// </summary>
        private string GenerateMarkdownReport(List<TestResult> results)
        {
            var report = new System.Text.StringBuilder();

            report.AppendLine("# BeaconTester Test Report");
            report.AppendLine();

            // Summary
            var successCount = results.Count(r => r.Success);
            var failureCount = results.Count - successCount;
            var totalDuration = TimeSpan.FromMilliseconds(
                results.Sum(r => r.Duration.TotalMilliseconds)
            );

            report.AppendLine("## Summary");
            report.AppendLine();
            report.AppendLine($"- **Total Tests**: {results.Count}");
            report.AppendLine($"- **Passed**: {successCount}");
            report.AppendLine($"- **Failed**: {failureCount}");
            report.AppendLine($"- **Total Duration**: {totalDuration.TotalSeconds:F2} seconds");
            report.AppendLine();

            // Results table
            report.AppendLine("## Results");
            report.AppendLine();
            report.AppendLine("| Test | Status | Duration (ms) |");
            report.AppendLine("|------|--------|--------------|");

            foreach (var result in results)
            {
                report.AppendLine(
                    $"| {result.Name} | {(result.Success ? "✅ PASS" : "❌ FAIL")} | {result.Duration.TotalMilliseconds:F2} |"
                );
            }

            report.AppendLine();

            // Failed tests details
            var failedResults = results.Where(r => !r.Success).ToList();
            if (failedResults.Count > 0)
            {
                report.AppendLine("## Failed Tests");
                report.AppendLine();

                foreach (var result in failedResults)
                {
                    report.AppendLine($"### {result.Name}");
                    report.AppendLine();
                    report.AppendLine(
                        $"**Error**: {result.ErrorMessage ?? "Test assertions failed"}"
                    );
                    report.AppendLine();

                    // Details of failed steps
                    var failedSteps = result.StepResults.Where(s => !s.Success).ToList();
                    if (failedSteps.Count > 0)
                    {
                        report.AppendLine("#### Failed Expectations");
                        report.AppendLine();
                        report.AppendLine("| Key | Expected | Actual |");
                        report.AppendLine("|-----|----------|--------|");

                        foreach (var step in failedSteps)
                        {
                            foreach (
                                var expectation in step.ExpectationResults.Where(e => !e.Success)
                            )
                            {
                                report.AppendLine(
                                    $"| `{expectation.Key}` | `{expectation.Expected}` | `{expectation.Actual}` |"
                                );
                            }
                        }

                        report.AppendLine();
                    }
                }
            }

            return report.ToString();
        }

        /// <summary>
        /// Generates an HTML report
        /// </summary>
        private string GenerateHtmlReport(List<TestResult> results)
        {
            var report = new System.Text.StringBuilder();

            // Add HTML header and styles
            report.AppendLine("<!DOCTYPE html>");
            report.AppendLine("<html lang=\"en\">");
            report.AppendLine("<head>");
            report.AppendLine("  <meta charset=\"UTF-8\">");
            report.AppendLine(
                "  <meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\">"
            );
            report.AppendLine("  <title>BeaconTester Report</title>");
            report.AppendLine("  <style>");
            report.AppendLine(
                "    body { font-family: Arial, sans-serif; margin: 0; padding: 20px; }"
            );
            report.AppendLine("    h1, h2, h3 { color: #333; }");
            report.AppendLine(
                "    .summary { background-color: #f5f5f5; padding: 15px; border-radius: 5px; margin-bottom: 20px; }"
            );
            report.AppendLine("    .pass { color: green; }");
            report.AppendLine("    .fail { color: red; }");
            report.AppendLine(
                "    table { border-collapse: collapse; width: 100%; margin-bottom: 20px; }"
            );
            report.AppendLine(
                "    th, td { border: 1px solid #ddd; padding: 8px; text-align: left; }"
            );
            report.AppendLine("    th { background-color: #f2f2f2; }");
            report.AppendLine("    tr:nth-child(even) { background-color: #f9f9f9; }");
            report.AppendLine("    .details { margin-top: 10px; margin-bottom: 20px; }");
            report.AppendLine("  </style>");
            report.AppendLine("</head>");
            report.AppendLine("<body>");

            // Report header
            report.AppendLine("  <h1>BeaconTester Test Report</h1>");

            // Summary
            var successCount = results.Count(r => r.Success);
            var failureCount = results.Count - successCount;
            var totalDuration = TimeSpan.FromMilliseconds(
                results.Sum(r => r.Duration.TotalMilliseconds)
            );

            report.AppendLine("  <div class=\"summary\">");
            report.AppendLine("    <h2>Summary</h2>");
            report.AppendLine($"    <p><strong>Total Tests:</strong> {results.Count}</p>");
            report.AppendLine(
                $"    <p><strong>Passed:</strong> <span class=\"pass\">{successCount}</span></p>"
            );
            report.AppendLine(
                $"    <p><strong>Failed:</strong> <span class=\"fail\">{failureCount}</span></p>"
            );
            report.AppendLine(
                $"    <p><strong>Total Duration:</strong> {totalDuration.TotalSeconds:F2} seconds</p>"
            );
            report.AppendLine("  </div>");

            // Results table
            report.AppendLine("  <h2>Results</h2>");
            report.AppendLine("  <table>");
            report.AppendLine("    <tr>");
            report.AppendLine("      <th>Test</th>");
            report.AppendLine("      <th>Status</th>");
            report.AppendLine("      <th>Duration (ms)</th>");
            report.AppendLine("    </tr>");

            foreach (var result in results)
            {
                report.AppendLine("    <tr>");
                report.AppendLine($"      <td>{result.Name}</td>");
                report.AppendLine(
                    $"      <td class=\"{(result.Success ? "pass" : "fail")}\">{(result.Success ? "PASS" : "FAIL")}</td>"
                );
                report.AppendLine($"      <td>{result.Duration.TotalMilliseconds:F2}</td>");
                report.AppendLine("    </tr>");
            }

            report.AppendLine("  </table>");

            // Failed tests details
            var failedResults = results.Where(r => !r.Success).ToList();
            if (failedResults.Count > 0)
            {
                report.AppendLine("  <h2>Failed Tests</h2>");

                foreach (var result in failedResults)
                {
                    report.AppendLine("  <div class=\"details\">");
                    report.AppendLine($"    <h3>{result.Name}</h3>");
                    report.AppendLine(
                        $"    <p><strong>Error:</strong> {result.ErrorMessage ?? "Test assertions failed"}</p>"
                    );

                    // Details of failed steps
                    var failedSteps = result.StepResults.Where(s => !s.Success).ToList();
                    if (failedSteps.Count > 0)
                    {
                        report.AppendLine("    <h4>Failed Expectations</h4>");
                        report.AppendLine("    <table>");
                        report.AppendLine("      <tr>");
                        report.AppendLine("        <th>Key</th>");
                        report.AppendLine("        <th>Expected</th>");
                        report.AppendLine("        <th>Actual</th>");
                        report.AppendLine("      </tr>");

                        foreach (var step in failedSteps)
                        {
                            foreach (
                                var expectation in step.ExpectationResults.Where(e => !e.Success)
                            )
                            {
                                report.AppendLine("      <tr>");
                                report.AppendLine(
                                    $"        <td><code>{expectation.Key}</code></td>"
                                );
                                report.AppendLine(
                                    $"        <td><code>{expectation.Expected}</code></td>"
                                );
                                report.AppendLine(
                                    $"        <td><code>{expectation.Actual}</code></td>"
                                );
                                report.AppendLine("      </tr>");
                            }
                        }

                        report.AppendLine("    </table>");
                    }

                    report.AppendLine("  </div>");
                }
            }

            // HTML footer
            report.AppendLine("</body>");
            report.AppendLine("</html>");

            return report.ToString();
        }

        /// <summary>
        /// Document wrapper for results
        /// </summary>
        private class ResultsDocument
        {
            /// <summary>
            /// The test results
            /// </summary>
            public List<TestResult> Results { get; set; } = new List<TestResult>();
        }
    }
}
