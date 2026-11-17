using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace ESharp.Cli;

/// <summary>The files `esharp new` writes: the program, a csproj for the host run and the editor, and the VS Code setup.</summary>
public static class ProjectTemplate
{
    public const string DefaultImage = "amigadev/crosstools:m68k-amigaos@sha256:32ec76dac03a642203f0f09f0416480d6c25de1092ed7ed472db870304a64859";

    /// <summary>The project name for a path given to `esharp new`: its last segment (games/demo gives demo).</summary>
    public static string ProjectName(string path) =>
        Path.GetFileName(path.TrimEnd('/', '\\'));

    /// <summary>Why <paramref name="name"/> cannot be a C# namespace and csproj name, or null when it can.</summary>
    public static string? NameProblem(string name)
    {
        if (name.Length == 0) return "it is empty";
        if (!Microsoft.CodeAnalysis.CSharp.SyntaxFacts.IsValidIdentifier(name)) return "it is not a valid C# identifier";
        if (Microsoft.CodeAnalysis.CSharp.SyntaxFacts.GetKeywordKind(name) != Microsoft.CodeAnalysis.CSharp.SyntaxKind.None) return "it is a C# keyword";
        return null;
    }

    /// <summary>A valid identifier close to <paramref name="name"/>, offered in the error message.</summary>
    public static string Suggest(string name)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var c in name) sb.Append(char.IsLetterOrDigit(c) && c < 128 ? c : '_');
        var s = sb.ToString().Trim('_');
        if (s.Length == 0) s = "Game";
        if (char.IsDigit(s[0])) s = "_" + s;
        return NameProblem(s) == null ? s : "_" + s;
    }

    /// <summary>
    /// Writes the project into <paramref name="dir"/> (which must exist). With <paramref name="root"/> (an E# checkout) the
    /// csproj references its projects and the tasks run <paramref name="cliDll"/> through dotnet (for contributors); without
    /// it the csproj references the AmigaESharp.Sdk package at <paramref name="packageVersion"/> and the tasks run the
    /// installed esharp command.
    /// </summary>
    public static void Write(string dir, string name, string? root, string cliDll, string? packageVersion = null)
    {
        File.WriteAllText(Path.Combine(dir, "Program.cs"), ProgramText(name));
        File.WriteAllText(Path.Combine(dir, name + ".csproj"), root != null ? Csproj(root) : PackageCsproj(packageVersion ?? SourceTree.ToolVersion));
        File.WriteAllText(Path.Combine(dir, ".gitignore"), "bin/\nobj/\n");
        var vs = Path.Combine(dir, ".vscode");
        Directory.CreateDirectory(vs);
        File.WriteAllText(Path.Combine(vs, "tasks.json"), root != null ? TasksJson(cliDll) : TasksJson("esharp", Array.Empty<string>()));
        File.WriteAllText(Path.Combine(vs, "launch.json"), LaunchJson(name));
        File.WriteAllText(Path.Combine(vs, "extensions.json"), ExtensionsJson);
    }

    /// <summary>
    /// The program: a greeting and one check. `esharp test` runs it on the host (and with --target m68k also on the Amiga)
    /// and compares the lines that start with ok or FAIL.
    /// </summary>
    public static string ProgramText(string name) => $@"using ESharp;
using ESharp.Platform;

namespace {name};

public static class Program
{{
    [EntryPoint]
    public static void Main()
    {{
        Dos.PutStr(""Hello from E#!\n"");

        // esharp test runs this on the PC and on the Amiga and compares the lines that start with ok or FAIL.
        int sum = 0;
        for (int i = 1; i <= 10; i++) sum += i;
        Dos.PutStr(sum == 55 ? ""ok sum of 1 to 10\n"" : ""FAIL sum of 1 to 10\n"");
    }}
}}
";

    /// <summary>
    /// The host project for a game using the published packages: AmigaESharp.Sdk holds the runtime, platform contracts and
    /// host fakes (built with HOST) and the subset analyzer, so ES0001-ES0010 show up in the editor and in `dotnet build`.
    /// </summary>
    public static string PackageCsproj(string version) => $@"<Project Sdk=""Microsoft.NET.Sdk"">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>disable</ImplicitUsings>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <!-- The same symbol the esharp test host build defines, so #if HOST code behaves alike. -->
    <DefineConstants>$(DefineConstants);HOST</DefineConstants>
    <!-- Workbench icon: esharp build writes <name>.info next to an Amiga program (stack size, tool types that Args reads on a
         Workbench start, the picture). All optional; remove the comment marks to use them. EsIcon none writes no icon.
    <EsStackSize>65536</EsStackSize>
    <EsToolTypes>SEED=42;AUTO</EsToolTypes>
    <EsIcon>icon.png</EsIcon>
    -->
  </PropertyGroup>

  <ItemGroup>
    <!-- E# runtime, platform contracts and host fakes, plus the subset analyzer. Keep the version equal to the esharp
         tool's (esharp version): the Amiga build compiles the sources embedded in the tool. -->
    <PackageReference Include=""{SourceTree.SdkPackage}"" Version=""{version}"" />
  </ItemGroup>

</Project>
";

    /// <summary>
    /// The host project for contributors, against an E# checkout: it compiles the same Program.cs under dotnet against the
    /// E# runtime and platform (with HOST defined), and loads the subset analyzer, so ES0001-ES0010 show up in the editor
    /// and in `dotnet build`.
    /// </summary>
    public static string Csproj(string? root)
    {
        var rootValue = root == null ? "" : root.Replace('\\', '/');
        return $@"<Project Sdk=""Microsoft.NET.Sdk"">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>disable</ImplicitUsings>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <!-- The same symbol the esharp test host build defines, so #if HOST code behaves alike. -->
    <DefineConstants>$(DefineConstants);HOST</DefineConstants>
    <!-- The E# source tree. Set ESHARP_ROOT or edit this line if you move it. -->
    <ESharpRoot Condition=""'$(ESharpRoot)' == '' and '$(ESHARP_ROOT)' != ''"">$(ESHARP_ROOT)</ESharpRoot>
    <ESharpRoot Condition=""'$(ESharpRoot)' == ''"">{rootValue}</ESharpRoot>
    <!-- Workbench icon: esharp build writes <name>.info next to an Amiga program (stack size, tool types that Args reads on a
         Workbench start, the picture). All optional; remove the comment marks to use them. EsIcon none writes no icon.
    <EsStackSize>65536</EsStackSize>
    <EsToolTypes>SEED=42;AUTO</EsToolTypes>
    <EsIcon>icon.png</EsIcon>
    -->
  </PropertyGroup>

  <ItemGroup>
    <!-- Runtime, platform contracts and the host fakes: the same sources the Amiga build compiles. -->
    <ProjectReference Include=""$(ESharpRoot)/src/ESharp.Target.Host/ESharp.Target.Host.csproj"" />
    <!-- The subset rules, loaded as an analyzer (not referenced as a library). -->
    <ProjectReference Include=""$(ESharpRoot)/src/ESharp.Analyzers/ESharp.Analyzers.csproj"" OutputItemType=""Analyzer"" ReferenceOutputAssembly=""false"" />
  </ItemGroup>

</Project>
";
    }

    private static string Q(string s) => JsonSerializer.Serialize(s);

    /// <summary>Tasks that run the esharp CLI through dotnet (a checkout's esharp.dll), so nothing needs to be on PATH.</summary>
    public static string TasksJson(string cliDll) => TasksJson("dotnet", new[] { cliDll });

    /// <summary>Tasks that run the CLI as <paramref name="command"/> <paramref name="prefix"/> (e.g. the installed esharp tool).</summary>
    public static string TasksJson(string command, string[] prefix)
    {
        string Task(string label, string group, params string[] args) => TaskWith("dotnet", label, group, args);
        string CliTask(string label, string group, params string[] args) => TaskWith(command, label, group, args);
        string TaskWith(string cmd, string label, string group, string[] args) =>
            $@"    {{
      ""label"": {Q(label)},
      ""type"": ""process"",
      ""command"": {Q(cmd)},
      ""args"": [{string.Join(", ", Array.ConvertAll(args, Q))}],
      ""options"": {{ ""cwd"": ""${{workspaceFolder}}"" }},
      ""problemMatcher"": ""$msCompile""{group}
    }}";
        string[] Cli(params string[] more) { var l = new List<string>(prefix); l.AddRange(more); return l.ToArray(); }
        var image = "${input:dockerImage}";
        var folder = "${workspaceFolder}";
        return $@"{{
  ""version"": ""2.0.0"",
  ""tasks"": [
{CliTask("E#: build (m68k)", ",\n      \"group\": { \"kind\": \"build\", \"isDefault\": true }", Cli("build", folder, "--target", "m68k", "--docker", image))},
{CliTask("E#: run in FS-UAE", "", Cli("run", folder, "--docker", image))},
{CliTask("E#: run in FS-UAE (headless)", "", Cli("run", folder, "--docker", image, "--headless"))},
{CliTask("E#: check subset and emit C", "", Cli("build", folder, "--emit-only"))},
{Task("E#: run on the host", "", "run", "--project", folder)},
{Task("E#: build for the host", "", "build", folder)}
  ],
  ""inputs"": [
    {{
      ""id"": ""dockerImage"",
      ""type"": ""promptString"",
      ""description"": ""Docker image with the Amiga cross compiler (esharp-amiga-toolchain: m68k, os4, morphos, aros; esharp-aros-i386: aros-i386)"",
      ""default"": {Q(DefaultImage)}
    }}
  ]
}}
";
    }

    public static string LaunchJson(string name) => $@"{{
  ""version"": ""0.2.0"",
  ""configurations"": [
    {{
      ""name"": ""E#: debug on the host (.NET)"",
      ""type"": ""coreclr"",
      ""request"": ""launch"",
      ""preLaunchTask"": ""E#: build for the host"",
      ""program"": ""${{workspaceFolder}}/bin/Debug/net10.0/{name}.dll"",
      ""cwd"": ""${{workspaceFolder}}"",
      ""console"": ""integratedTerminal"",
      ""stopAtEntry"": false
    }}
  ]
}}
";

    public const string ExtensionsJson = @"{
  ""recommendations"": [ ""ms-dotnettools.csharp"" ]
}
";
}
