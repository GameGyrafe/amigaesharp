using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using ESharp.Compiler;
using Microsoft.CodeAnalysis;

namespace ESharp.Cli;

internal static class Program
{
    private const string Usage = @"esharp - C# for the Amiga

Usage:
  esharp new <name>                 Create a new hello-world project
  esharp build [path] [options]     Check the subset, emit C, and run the target C compiler
  esharp run | test | assets        Not implemented yet (phase 1 covers build only)

Build options:
  -o <dir>          Output directory (default: obj next to the sources)
  --emit-only       Stop after writing the C file
  --cc <compiler>   C compiler (default: m68k-amigaos-gcc)
  --cflags <flags>  Extra C compiler flags (default: -noixemul -m68000 -O2)
  --allow-float     Permit float/double
";

    private static int Main(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            Console.Out.Write(Usage);
            return args.Length == 0 ? 1 : 0;
        }

        return args[0] switch
        {
            "new" => New(args.Skip(1).ToArray()),
            "build" => Build(args.Skip(1).ToArray()),
            "run" or "test" or "assets" => NotYet(args[0]),
            _ => Fail($"unknown command '{args[0]}'"),
        };
    }

    private static int Fail(string message)
    {
        Console.Error.WriteLine($"esharp: {message}");
        return 1;
    }

    private static int NotYet(string command)
    {
        Console.Error.WriteLine($"esharp: '{command}' is not implemented yet");
        return 2;
    }

    private static int New(string[] args)
    {
        if (args.Length != 1) return Fail("usage: esharp new <name>");
        var name = args[0];
        if (Directory.Exists(name)) return Fail($"'{name}' already exists");

        Directory.CreateDirectory(name);
        File.WriteAllText(Path.Combine(name, "Program.cs"), $@"using ESharp;
using ESharp.Platform;

namespace {name};

public static class Program
{{
    [EntryPoint]
    public static void Main()
    {{
        Dos.PutStr(""Hello from E#!\n"");
    }}
}}
");
        Console.WriteLine($"Created {name}/Program.cs. Build it with: esharp build {name}");
        return 0;
    }

    private static int Build(string[] args)
    {
        string path = ".";
        string? outDir = null;
        bool emitOnly = false, allowFloat = false;
        string cc = "m68k-amigaos-gcc";
        string cflags = "-noixemul -m68000 -O2";

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "-o" when i + 1 < args.Length: outDir = args[++i]; break;
                case "--emit-only": emitOnly = true; break;
                case "--allow-float": allowFloat = true; break;
                case "--cc" when i + 1 < args.Length: cc = args[++i]; break;
                case "--cflags" when i + 1 < args.Length: cflags = args[++i]; break;
                default:
                    if (args[i].StartsWith("-")) return Fail($"unknown option '{args[i]}'");
                    path = args[i];
                    break;
            }
        }

        var files = CollectSources(path);
        if (files.Count == 0) return Fail($"no .cs files found in '{path}'");

        var baseDir = Directory.Exists(path) ? path : Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".";
        var sources = files.Select(f => new SourceFile(Path.GetRelativePath(Directory.GetCurrentDirectory(), f), File.ReadAllText(f))).ToList();
        var result = EsCompiler.Compile(sources, new CompileOptions { AllowFloat = allowFloat });

        foreach (var d in result.Diagnostics)
            Console.Error.WriteLine(d.ToString());

        if (!result.Success)
        {
            var errors = result.Diagnostics.Count(d => d.Severity == DiagnosticSeverity.Error);
            Console.Error.WriteLine($"Build failed: {errors} error(s)");
            return 1;
        }

        outDir ??= Path.Combine(baseDir, "obj");
        Directory.CreateDirectory(outDir);
        var name = Path.GetFileNameWithoutExtension(files[0]);
        if (Directory.Exists(path)) name = new DirectoryInfo(Path.GetFullPath(path)).Name;
        var cFile = Path.Combine(outDir, name + ".c");
        File.WriteAllText(cFile, result.C!);
        Console.WriteLine($"Wrote {cFile}");
        if (emitOnly) return 0;

        var exe = Path.Combine(outDir, name);
        var psi = new ProcessStartInfo(cc) { UseShellExecute = false };
        foreach (var f in cflags.Split(' ', StringSplitOptions.RemoveEmptyEntries)) psi.ArgumentList.Add(f);
        psi.ArgumentList.Add("-o");
        psi.ArgumentList.Add(exe);
        psi.ArgumentList.Add(cFile);

        try
        {
            using var proc = Process.Start(psi)!;
            proc.WaitForExit();
            if (proc.ExitCode != 0) return Fail($"{cc} failed with exit code {proc.ExitCode}");
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return Fail($"C compiler '{cc}' not found. Install Bebbo's amiga-gcc, or pass --cc, or use --emit-only.");
        }

        Console.WriteLine($"Built {exe}");
        return 0;
    }

    private static List<string> CollectSources(string path)
    {
        if (File.Exists(path)) return new List<string> { path };
        if (!Directory.Exists(path)) return new List<string>();
        var sep = Path.DirectorySeparatorChar;
        return Directory.EnumerateFiles(path, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{sep}obj{sep}") && !f.Contains($"{sep}bin{sep}"))
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();
    }
}
