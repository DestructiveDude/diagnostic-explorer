using DiagnosticExplorer;
using DiagnosticExplorer.Extensions.Logging;
using DiagnosticExplorer.Log4Net;
using DiagnosticExplorer.NLog;
using DiagnosticExplorer.Serilog;
using Microsoft.Extensions.Logging;
using NLog;
using NLog.Config;
using Serilog;

namespace DiagnosticExplorer.Samples.Logging;

/// <summary>Four logging frameworks feeding the same diagnostics stream, with an executable check.</summary>
internal static class Program
{
    private static void Main(string[] args)
    {
        if (args.Length > 1)
        {
            throw new ArgumentException("Usage: Logging [diagnostics-hub-url]");
        }

        DiagnosticManager.Configure(config =>
        {
            config.ConfigureEventRouting(routes => routes.Route("Sample", route => route.To("Logging", "Events")));
            config.ConfigureLogEventRetention(retention => retention.WithMaxEvents(100));
        });

        EmitMicrosoftLogging();
        EmitNLog();
        EmitSerilog();
        EmitLog4Net();

        var snapshot = DiagnosticManager.LogEventStore.CreateInitialization();
        var events = snapshot.ReplayEvents;
        string[] expected = ["MEL", "NLog", "Serilog", "log4net"];
        if (!events.Select(evt => evt.Message).SequenceEqual(expected))
        {
            throw new InvalidOperationException("Each logging adapter must publish exactly one event to the stream.");
        }

        if (snapshot.Routing.Routes.Count != 1)
        {
            throw new InvalidOperationException(
                "Closing the logging frameworks must leave only the configured base route."
            );
        }

        foreach (var evt in events)
        {
            Console.WriteLine($"{evt.Sequence}: {evt.LoggerCategory} — {evt.Message}");
        }

        if (args.Length == 0)
        {
            return;
        }

        try
        {
            DiagnosticHostingService.Start(args[0]);
            Console.WriteLine("Open this process in the diagnostics viewer. Press Enter to stop.");
            Console.ReadLine();
        }
        finally
        {
            DiagnosticHostingService.Stop().GetAwaiter().GetResult();
        }
    }

    private static void EmitMicrosoftLogging()
    {
        using var factory = LoggerFactory.Create(builder => builder.AddDiagnosticExplorer());
        factory.CreateLogger("Sample.MEL").LogInformation("MEL");
    }

    private static void EmitNLog()
    {
        using var factory = new LogFactory();
        factory.Configuration = new LoggingConfiguration().AddDiagnosticExplorer();
        factory.GetLogger("Sample.NLog").Info("NLog");
    }

    private static void EmitSerilog()
    {
        using var logger = new LoggerConfiguration().WriteTo.DiagnosticExplorer().CreateLogger();
        logger.ForContext("SourceContext", "Sample.Serilog").Information("Serilog");
    }

    private static void EmitLog4Net()
    {
        var repository = (log4net.Repository.Hierarchy.Hierarchy)log4net.LogManager.CreateRepository("LoggingSample");
        try
        {
            repository.Root.Level = log4net.Core.Level.Info;
            repository.ConfigureDiagnosticExplorer();
            log4net.LogManager.GetLogger(repository.Name, "Sample.log4net").Info("log4net");
        }
        finally
        {
            repository.Shutdown();
        }
    }
}
