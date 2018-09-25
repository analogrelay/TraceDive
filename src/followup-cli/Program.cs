using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.Diagnostics.Symbols;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Etlx;
using Microsoft.Diagnostics.Tracing.Parsers.Tpl;

namespace FollowUp.CommandLine
{
    internal class Program
    {
        // Hacky Just My Code for now :)
        private static readonly HashSet<string> ExcludedModules = new HashSet<string>()
        {
            "ntdll",
            "system.private.corelib",
            "coreclr",
            "hostpolicy",
            "hostfxr",
            "dotnet",
            "kernel32",
        };

        // From https://source.dot.net/#System.Private.CoreLib/src/System/Threading/Tasks/TPLETWProvider.cs
        private static readonly Guid TplProviderId = Guid.Parse("2e5dba47-a3d2-4d16-8ee0-6671ffdcd7b5");
        private const TraceEventID TASKWAITBEGIN_ID = (TraceEventID)10;
        private const TraceEventID TASKWAITEND_ID = (TraceEventID)11;

        private static int Main(string[] args)
        {
#if DEBUG
            if (args.Any(a => a == "--debug"))
            {
                args = args.Where(a => a != "--debug").ToArray();
                Console.WriteLine($"Ready for debugger to attach. Process ID: {Process.GetCurrentProcess().Id}.");
                Console.WriteLine("Press ENTER to continue.");
                Console.ReadLine();
            }
#endif

            if (args.Length < 1)
            {
                Console.Error.WriteLine("Usage: followup-cli <trace file> [<pid>]");
                return 1;
            }

            var traceFile = args[0];

            int? pid = null;
            if (args.Length > 1)
            {
                pid = int.Parse(args[1]);
            }

            if (!traceFile.EndsWith(".zip"))
            {
                Console.Error.WriteLine("Expected a ZIP file");
                return 1;
            }

            return ProcessInput(traceFile, pid);
        }

        private static int ProcessInput(string file, int? pid)
        {
            var reader = new ZippedETLReader(file, Console.Out);
            Console.WriteLine("Unpacking ZIP archive...");
            reader.UnpackArchive();

            var symbolReader = new SymbolReader(Console.Out, SymbolPath.MicrosoftSymbolServerPath);

            // Allow any PDB to be loaded :)
            symbolReader.SecurityCheck = (_) => true;

            // Start the session
            Console.WriteLine("Loading trace file...");
            using (var traceLog = TraceLog.OpenOrConvert(reader.EtlFileName))
            {
                if (pid == null)
                {
                    // Dump all dotnet process IDs:
                    foreach (var process in traceLog.Processes.Where(p => p.Name == "dotnet"))
                    {
                        Console.WriteLine($"* {process.ProcessID} {process.Name} {process.CommandLine}");
                    }
                }
                else
                {
                    var p = traceLog.Processes.LastProcessWithID(pid.Value);
                    Console.WriteLine($"Scanning process {p.ProcessID} {p.Name}");
                    var starts = p.EventsInProcess.ByEventType<TaskWaitSendArgs>().ToDictionary(a => a.TaskID, a => (TaskWaitSendArgs)a.Clone());
                    var stops = p.EventsInProcess.ByEventType<TaskWaitStopArgs>().ToDictionary(a => a.TaskID, a => (TaskWaitStopArgs)a.Clone());
                    Console.WriteLine("Loaded.");

                    Console.WriteLine("Analyzing for hanging tasks...");

                    // Collect all this in to a buffer so that the symbol reader log messages dump first.
                    using (var writer = new StringWriter())
                    {
                        foreach (var (taskId, evt) in starts)
                        {
                            if (!stops.ContainsKey(taskId))
                            {
                                // Still hanging!
                                DumpHang(writer, evt, symbolReader);
                            }
                        }

                        // Now write it out
                        Console.Write(writer.ToString());
                    }
                }
            }

            return 0;
        }

        private static void DumpHang(TextWriter output, TaskWaitSendArgs evt, SymbolReader symbolReader)
        {
            const string indent = "    ";
            var hangType = evt.Behavior == TaskWaitBehavior.Asynchronous ? "asynchronously" : "synchronously";
            output.WriteLine($"Task {evt.TaskID} is hanging {hangType} at");
            var stack = evt.CallStack();
            if (stack == null)
            {
                output.WriteLine($"{indent}<<unknown stack location>>");
            }
            else
            {
                var inExternalCode = false;
                while (stack != null)
                {
                    // Skip external code frames
                    if (ExcludedModules.Contains(stack.CodeAddress.ModuleName))
                    {
                        inExternalCode = true;
                    }
                    else
                    {
                        if (inExternalCode)
                        {
                            // Write a line representing the external code
                            output.WriteLine($"{indent}[External Code]");
                            inExternalCode = false;
                        }
                        output.WriteLine($"{indent}{FormatCodeAddress(stack.CodeAddress, symbolReader)}");
                    }
                    stack = stack.Caller;
                }
            }
        }

        private static string FormatCodeAddress(TraceCodeAddress address, SymbolReader symbolReader)
        {
            var sourceLocation = address.GetSourceLine(symbolReader);
            var methodName = address.FullMethodName;
            if (string.IsNullOrEmpty(methodName))
            {
                var symbolFilePath = symbolReader.FindSymbolFilePath(address.ModuleFile.PdbName, address.ModuleFile.PdbSignature, address.ModuleFile.PdbAge);
                if (symbolFilePath != null)
                {
                    var nativeSymbols = symbolReader.OpenNativeSymbolFile(symbolFilePath);
                    if (nativeSymbols != null)
                    {
                        methodName = nativeSymbols.FindNameForRva((uint)address.Address);
                        sourceLocation = nativeSymbols.SourceLocationForRva((uint)address.Address);
                    }
                }
            }
            if (string.IsNullOrEmpty(methodName))
            {
                methodName = $"0x{address.Address:x} (no symbols)";
            }

            var native = "";
            if (address.ILOffset == -1)
            {
                native = " (native)";
            }

            var sourceLoc = "";
            if (sourceLocation != null)
            {
                sourceLoc = $" at {sourceLocation.SourceFile.BuildTimeFilePath}:{sourceLocation.LineNumber}";
            }
            return $"{address.ModuleName}!{methodName}{native}{sourceLoc}";
        }
    }
}
