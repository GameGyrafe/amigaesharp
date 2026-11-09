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

    /// <summary>Writes the project into <paramref name="dir"/> (which must exist). <paramref name="cliDll"/> is the esharp.dll the tasks call.</summary>
    public static void Write(string dir, string name, string? root, string cliDll)
    {
        File.WriteAllText(Path.Combine(dir, "Program.cs"), $@"using ESharp;
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
        File.WriteAllText(Path.Combine(dir, name + ".csproj"), Csproj(root));
        File.WriteAllText(Path.Combine(dir, ".gitignore"), "bin/\nobj/\n");
        var vs = Path.Combine(dir, ".vscode");
        Directory.CreateDirectory(vs);
        File.WriteAllText(Path.Combine(vs, "tasks.json"), TasksJson(cliDll));
        File.WriteAllText(Path.Combine(vs, "launch.json"), LaunchJson(name));
        File.WriteAllText(Path.Combine(vs, "extensions.json"), ExtensionsJson);
    }

    /// <summary>
    /// The host project: it compiles the same Program.cs under dotnet against the E# runtime and platform (with HOST defined),
    /// and loads the subset analyzer, so ES0001-ES0010 show up in the editor and in `dotnet build`.
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

    public static string TasksJson(string cliDll)
    {
        // Every task runs the esharp CLI through dotnet, so nothing needs to be on PATH.
        string Task(string label, string group, params string[] args) =>
            $@"    {{
      ""label"": {Q(label)},
      ""type"": ""process"",
      ""command"": ""dotnet"",
      ""args"": [{string.Join(", ", Array.ConvertAll(args, Q))}],
      ""options"": {{ ""cwd"": ""${{workspaceFolder}}"" }},
      ""problemMatcher"": ""$msCompile""{group}
    }}";
        string[] Cli(params string[] more) { var l = new List<string> { cliDll }; l.AddRange(more); return l.ToArray(); }
        var image = "${input:dockerImage}";
        var folder = "${workspaceFolder}";
        return $@"{{
  ""version"": ""2.0.0"",
  ""tasks"": [
{Task("E#: build (m68k)", ",\n      \"group\": { \"kind\": \"build\", \"isDefault\": true }", Cli("build", folder, "--target", "m68k", "--docker", image))},
{Task("E#: run in FS-UAE", "", Cli("run", folder, "--docker", image))},
{Task("E#: run in FS-UAE (headless)", "", Cli("run", folder, "--docker", image, "--headless"))},
{Task("E#: check subset and emit C", "", Cli("build", folder, "--emit-only"))},
{Task("E#: run on the host", "", "run", "--project", folder)},
{Task("E#: build for the host", "", "build", folder)}
  ],
  ""inputs"": [
    {{
      ""id"": ""dockerImage"",
      ""type"": ""promptString"",
      ""description"": ""Docker image with the Amiga cross compiler (esharp-amiga-toolchain has all targets)"",
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
