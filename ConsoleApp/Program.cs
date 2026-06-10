using System;
using System.Diagnostics;
// using System.Reflection.Metadata.Ecma335;
using System.Threading.Tasks;
using DiagnosticExplorer;
// using DiagnosticExplorer.Common;

// using Grpc.Core;
// using Microsoft.AspNetCore.SignalR.Client;
// using Microsoft.CodeAnalysis;
namespace ConsoleApp;

internal class Program
{
    private static async Task Main()
    {
        Trace.Listeners.Add(new ConsoleTraceListener());

        while (true)
        {
            DiagnosticHostingService.Start("http://localhost:2804/diagnostics");

            Console.WriteLine("Diagnostics started");
            Console.ReadLine();

            await DiagnosticHostingService.Stop();

            Console.WriteLine("Diagnostics stopped");
            Console.ReadLine();
        }
    }
}