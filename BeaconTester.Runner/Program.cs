using System.CommandLine;
using BeaconTester.Runner.Commands;
using Serilog;
using Serilog.Events;

namespace BeaconTester.Runner
{
    class Program
    {
        static async Task<int> Main(string[] args)
        {
            // Configure logging
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
                .Enrich.FromLogContext()
                .WriteTo.Console()
                .WriteTo.File(
                    "logs/beacontester-.log",
                    rollingInterval: RollingInterval.Day,
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}"
                )
                .CreateLogger();

            try
            {
                Log.Information("BeaconTester starting up");

                // Create command line parser
                var rootCommand = new RootCommand(
                    "BeaconTester - Automated testing for Beacon solutions"
                );

                // Add commands
                rootCommand.AddCommand(new GenerateCommand().Create());
                rootCommand.AddCommand(new RunCommand().Create());
                rootCommand.AddCommand(new ReportCommand().Create());
                rootCommand.AddCommand(new TestExpressionCommand().Create());

                // Execute command
                return await rootCommand.InvokeAsync(args);
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "BeaconTester terminated unexpectedly");
                return 1;
            }
            finally
            {
                Log.CloseAndFlush();
            }
        }
    }
}
