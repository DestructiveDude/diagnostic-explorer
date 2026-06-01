using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
// using System.Reflection.Metadata.Ecma335;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using DiagnosticExplorer;
// using DiagnosticExplorer.Common;
using JsonSerializer = Newtonsoft.Json.JsonSerializer;

// using Grpc.Core;
// using Microsoft.AspNetCore.SignalR.Client;
// using Microsoft.CodeAnalysis;
namespace ConsoleApp;

internal class Program
{
    private static async Task Main(string[] args)
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