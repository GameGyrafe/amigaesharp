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
        File.WriteAllText(Path.Combine(vs, "settings.json"), SettingsJson);
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
    public static string PackageCsproj(string version, string? libraryId = null) => $@"<Project Sdk=""Microsoft.NET.Sdk"">

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
    <PackageReference Include=""{SourceTree.SdkPackage}"" Version=""{version}"" />{LibraryReference(libraryId)}
  </ItemGroup>

</Project>
";

    /// <summary>
    /// The host project for contributors, against an E# checkout: it compiles the same Program.cs under dotnet against the
    /// E# runtime and platform (with HOST defined), and loads the subset analyzer, so ES0001-ES0010 show up in the editor
    /// and in `dotnet build`.
    /// </summary>
    public static string Csproj(string? root, string? libraryId = null)
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
    <ProjectReference Include=""$(ESharpRoot)/src/ESharp.Analyzers/ESharp.Analyzers.csproj"" OutputItemType=""Analyzer"" ReferenceOutputAssembly=""false"" />{LibraryReference(libraryId)}
  </ItemGroup>

</Project>
";
    }


    private static string LibraryReference(string? libraryId) =>
        libraryId == null ? "" : $"\n    <PackageReference Include=\"{libraryId}\" Version=\"{LibraryVersion}\" />";

    public const string LibraryVersion = "1.0.0";

    /// <summary>The PackageId a new library starts with: a placeholder to change (the AmigaESharp prefix is reserved on nuget.org).</summary>
    public static string LibraryId(string name) => "MyCompany." + name;

    /// <summary>
    /// Writes a library project into <paramref name="dir"/> (which must exist): the library (<c>name/</c>), a demo program that uses it
    /// (<c>name.Demo/</c>), a nuget.config with the local folder feed <c>feed/</c>, the VS Code files and a README. Without
    /// <paramref name="root"/> both csproj files reference the AmigaESharp.Sdk package at <paramref name="packageVersion"/>; with it
    /// (an E# checkout) they reference its projects and the library imports the library step from the checkout's Sdk folder.
    /// </summary>
    public static void WriteLibrary(string dir, string name, string? root, string cliDll, string? packageVersion = null)
    {
        var version = packageVersion ?? SourceTree.ToolVersion;
        var id = LibraryId(name);
        var lib = Path.Combine(dir, name);
        var demo = Path.Combine(dir, name + ".Demo");
        Directory.CreateDirectory(lib);
        Directory.CreateDirectory(demo);
        Directory.CreateDirectory(Path.Combine(dir, "feed"));
        File.WriteAllText(Path.Combine(lib, name + ".csproj"), LibraryCsproj(name, root, version));
        File.WriteAllText(Path.Combine(lib, "Counter.cs"), LibraryClassText(name));
        File.WriteAllText(Path.Combine(demo, name + ".Demo.csproj"), root != null ? Csproj(root, id) : PackageCsproj(version, id));
        File.WriteAllText(Path.Combine(demo, "Program.cs"), LibraryDemoText(name));
        File.WriteAllText(Path.Combine(dir, "nuget.config"), NuGetConfig);
        File.WriteAllText(Path.Combine(dir, "feed", ".gitkeep"), "");
        File.WriteAllText(Path.Combine(dir, ".gitignore"), "bin/\nobj/\nfeed/*.nupkg\n");
        File.WriteAllText(Path.Combine(dir, "README.md"), LibraryReadme(name, root != null));
        var vs = Path.Combine(dir, ".vscode");
        Directory.CreateDirectory(vs);
        File.WriteAllText(Path.Combine(vs, "tasks.json"), root != null ? TasksJson("dotnet", new[] { cliDll }, name + ".Demo", name) : TasksJson("esharp", Array.Empty<string>(), name + ".Demo", name));
        File.WriteAllText(Path.Combine(vs, "launch.json"), LaunchJson(name + ".Demo", name + ".Demo"));
        File.WriteAllText(Path.Combine(vs, "extensions.json"), ExtensionsJson);
        File.WriteAllText(Path.Combine(vs, "settings.json"), SettingsJson);
    }

    /// <summary>The local folder feed next to the projects (nuget.org stays: the AmigaESharp.Sdk package comes from there).</summary>
    public const string NuGetConfig = @"<?xml version=""1.0"" encoding=""utf-8""?>
<configuration>
  <packageSources>
    <!-- dotnet pack <library> -o feed puts the library here; the demo restores it from this folder. -->
    <add key=""local-feed"" value=""feed"" />
  </packageSources>
</configuration>
";

    /// <summary>
    /// The library project, as docs/GUIDE.md "Libraries" describes it. Against a checkout the Sdk's library step is imported from
    /// the checkout (a ProjectReference does not carry a package's build files) next to the host assemblies and the analyzer.
    /// </summary>
    public static string LibraryCsproj(string name, string? root, string version)
    {
        var rootValue = root == null ? "" : root.Replace('\\', '/');
        var sdk = root == null
            ? $@"    <!-- AmigaESharp.Sdk carries the library step (embeds the sources and the manifest), the host assemblies and the subset analyzer.
         Keep the version equal to the esharp tool's (esharp version). -->
    <PackageReference Include=""{SourceTree.SdkPackage}"" Version=""{version}"" />"
            : @"    <!-- Runtime, platform contracts and the host fakes, plus the subset rules as an analyzer. -->
    <ProjectReference Include=""$(ESharpRoot)/src/ESharp.Target.Host/ESharp.Target.Host.csproj"" />
    <ProjectReference Include=""$(ESharpRoot)/src/ESharp.Analyzers/ESharp.Analyzers.csproj"" OutputItemType=""Analyzer"" ReferenceOutputAssembly=""false"" />";
        var checkout = root == null ? "" : $@"
    <!-- The E# source tree. Set ESHARP_ROOT or edit this line if you move it. -->
    <ESharpRoot Condition=""'$(ESharpRoot)' == '' and '$(ESHARP_ROOT)' != ''"">$(ESHARP_ROOT)</ESharpRoot>
    <ESharpRoot Condition=""'$(ESharpRoot)' == ''"">{rootValue}</ESharpRoot>
    <EsMinSdkVersion>{version}</EsMinSdkVersion>
    <!-- The esharp that builds the binaries at dotnet pack: the checkout's. -->
    <EsCommand Condition=""'$(EsCommand)' == ''"">dotnet run --project ""$(ESharpRoot)/src/ESharp.Cli"" --</EsCommand>";
        var import = root == null ? "" : @"
  <!-- The library step of the Sdk (a package brings it by itself). -->
  <Import Project=""$(ESharpRoot)/src/ESharp.Sdk/buildTransitive/AmigaESharp.Sdk.targets"" />
";
        return $@"<Project Sdk=""Microsoft.NET.Sdk"">

  <!--
    An E# library: an ordinary class library whose DLL also carries its C# sources and a manifest (docs/GUIDE.md, Libraries).
    dotnet pack makes the NuGet package; a program that references it gets the HOST build for dotnet and the sources for esharp build.
  -->
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>disable</Nullable>
    <!-- Required: the Amiga build sees only the embedded files, so every file has its own using lines. -->
    <ImplicitUsings>disable</ImplicitUsings>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>{checkout}

    <EsLibrary>true</EsLibrary>
    <!-- The targets the library supports: esharp build refuses the others (ES0110). Remove the ones you have not tried. -->
    <EsTargets>m68k;os4;morphos;aros;aros-i386;sdl</EsTargets>
    <!-- Platform-level features the library uses: AmigaLib, Native, InlineAsm, Interrupt, HookEntry, NativeC. None here. -->
    <EsFeatures></EsFeatures>
    <!-- dotnet pack also builds the library for every target in EsTargets into a binary in the package, which programs link instead of
         compiling the sources (they still ship). That needs each target's toolchain: its Docker image, or ESHARP_TOOLCHAIN_<TARGET>.
         false: no binaries, sources only. -->
    <EsPrecompile>true</EsPrecompile>

    <IsPackable>true</IsPackable>
    <!-- CHANGE THIS: your own package id (Company.Library). Ids that start with AmigaESharp are reserved on nuget.org for the official packages. -->
    <PackageId>{LibraryId(name)}</PackageId>
    <Version>{LibraryVersion}</Version>
    <Authors>Your Name</Authors>
    <Description>An E# library.</Description>
    <!-- An SPDX licence expression: MIT, Apache-2.0, ... (a GPL-family licence needs EsAllowCopyleft in the programs that use it). -->
    <PackageLicenseExpression>MIT</PackageLicenseExpression>
    <PackageReadmeFile>README.md</PackageReadmeFile>
  </PropertyGroup>

  <ItemGroup>
{sdk}
    <None Include=""..\README.md"" Pack=""true"" PackagePath=""README.md"" />
  </ItemGroup>
{import}
</Project>
";
    }

    /// <summary>The sample class of a new library: public, in a namespace of its own, plain E# (no platform calls).</summary>
    public static string LibraryClassText(string name) => $@"namespace {name};

/// <summary>A sample class of the library: a running total and count. Replace it with your own code.</summary>
public sealed class Counter
{{
    public int Total {{ get; private set; }}
    public int Count {{ get; private set; }}

    public void Add(int value)
    {{
        Total += value;
        Count++;
    }}

    /// <summary>The whole-number mean of what was added, 0 before the first Add.</summary>
    public int Mean()
    {{
        return Count == 0 ? 0 : Total / Count;
    }}
}}
";

    /// <summary>The demo program: uses the library, prints ok/FAIL lines for `esharp test` (host, and with --target also the Amiga).</summary>
    public static string LibraryDemoText(string name) => $@"using ESharp;
using ESharp.Platform;

namespace {name}.Demo;

public static class Program
{{
    static int failures;

    static void Check(bool ok, string label)
    {{
        if (!ok) failures++;
        Dos.PutStr(ok ? ""ok "" : ""FAIL "");
        Dos.PutStr(label);
        Dos.PutStr(""\n"");
    }}

    [EntryPoint]
    public static int Main()
    {{
        Dos.PutStr(""Demo of the {name} library\n"");

        // esharp test runs this on the PC and on the Amiga and compares the lines that start with ok or FAIL.
        Counter c = new Counter();
        Check(c.Count == 0 && c.Mean() == 0, ""a new counter is empty"");
        c.Add(4);
        c.Add(8);
        c.Add(12);
        Check(c.Count == 3 && c.Total == 24, ""three values added"");
        Check(c.Mean() == 8, ""the mean is 8"");

        return failures == 0 ? 0 : 5;
    }}
}}
";

    public static string LibraryReadme(string name, bool checkout)
    {
        var id = LibraryId(name);
        var sdk = checkout
            ? $@"These projects reference the E# checkout they were made from (`ESharpRoot` in the two csproj files), not the
`{SourceTree.SdkPackage}` package: the library imports the Sdk's library step from the checkout.
That is for working on E# itself: a package packed this way depends on the checkout's project, so to publish a library
make it with `esharp new --library <name> --package` (the `{SourceTree.SdkPackage}` package at the installed tool's version)."
            : $@"Both projects reference the `{SourceTree.SdkPackage}` package at the version of the esharp tool that made them (`esharp version`);
keep the two equal.";
        return $@"# {name}

An E# library package (docs/GUIDE.md, ""Libraries"" in the E# repository), with a demo program that uses it.

- `{name}/` is the library: `{name}.csproj` (`EsLibrary`, `EsTargets`, `EsFeatures`, the package id and licence) and `Counter.cs`,
  a sample public class in the namespace `{name}`. Every file needs its own `using` lines (no implicit usings).
- `{name}.Demo/` is a program that uses the library and checks it. `esharp test {name}.Demo` runs it on the PC and, with
  `--target m68k --docker <image>`, on the Amiga too, and compares the `ok` / `FAIL` lines.
- `feed/` is a local NuGet folder feed (`nuget.config`); nothing is published anywhere.

{sdk}

**Before you pack:** change `PackageId` in `{name}/{name}.csproj` (now `{id}`) to your own id, set `Authors`, `Description` and the
licence, and use the same id in the `PackageReference` of `{name}.Demo/{name}.Demo.csproj`. Ids that start with `AmigaESharp` are
reserved on nuget.org for the official packages.

## Try it

```bash
dotnet pack {name} -o feed              # {id} {LibraryVersion}: the DLL carries the C# sources and the manifest, and a binary per target
                                        # (each target's toolchain needed; -p:EsPrecompile=false for sources only)
dotnet restore {name}.Demo              # restores the library from ./feed
esharp test {name}.Demo                 # the host run: 3 ok
esharp test {name}.Demo --target m68k --docker <image> --kickstart <rom>    # also on the Amiga (needs Docker and FS-UAE)
esharp build {name}.Demo --target m68k --docker <image>
```

NuGet keeps the first copy of a version in its package cache (`~/.nuget/packages`): after changing the library, raise
`Version` before packing again (and in the demo's `PackageReference`), or set `NUGET_PACKAGES` to an empty folder for these commands.
To publish, `dotnet nuget push` the `.nupkg` with your own account and API key: that is your decision, nothing here does it.
";
    }

    private static string Q(string s) => JsonSerializer.Serialize(s);

    /// <summary>The VS Code setting the local-toolchain tasks read: the m68k toolchain folder (holding bin/m68k-amigaos-gcc).</summary>
    public const string ToolchainSetting = "esharp.toolchain";

    /// <summary>Tasks that run the esharp CLI through dotnet (a checkout's esharp.dll), so nothing needs to be on PATH.</summary>
    public static string TasksJson(string cliDll) => TasksJson("dotnet", new[] { cliDll });

    /// <summary>Tasks that run the CLI as <paramref name="command"/> <paramref name="prefix"/> (e.g. the installed esharp tool).</summary>
    public static string TasksJson(string command, string[] prefix, string? project = null, string? library = null)
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
        // A library template: the tasks work on the demo program in its own folder, and one more task packs the library.
        var folder = project == null ? "${workspaceFolder}" : "${workspaceFolder}/" + project;
        var pack = library == null ? "" : ",\n" + Task("E#: pack the library into ./feed", "", "pack", "${workspaceFolder}/" + library, "-o", "${workspaceFolder}/feed");
        // A local toolchain instead of Docker: the folder from the esharp.toolchain setting. VS Code cannot resolve
        // ${config:...} for a setting that is not defined anywhere, so `esharp new` writes it empty into .vscode/settings.json
        // (SettingsJson); empty counts as not given, and esharp then reads ESHARP_TOOLCHAIN_M68K / ESHARP_TOOLCHAIN, else PATH.
        var toolchain = "${config:" + ToolchainSetting + "}";
        return $@"{{
  ""version"": ""2.0.0"",
  ""tasks"": [
{CliTask("E#: build (m68k)", ",\n      \"group\": { \"kind\": \"build\", \"isDefault\": true }", Cli("build", folder, "--target", "m68k", "--docker", image))},
{CliTask("E#: run in FS-UAE", "", Cli("run", folder, "--docker", image))},
{CliTask("E#: run in FS-UAE (headless)", "", Cli("run", folder, "--docker", image, "--headless"))},
{CliTask("E#: build (m68k, local toolchain)", "", Cli("build", folder, "--target", "m68k", "--toolchain", toolchain))},
{CliTask("E#: run in FS-UAE (local toolchain)", "", Cli("run", folder, "--toolchain", toolchain))},
{CliTask("E#: check subset and emit C", "", Cli("build", folder, "--emit-only"))},
{Task("E#: run on the host", "", "run", "--project", folder)},
{Task("E#: build for the host", "", "build", folder)}{pack}
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

    public static string LaunchJson(string name, string? project = null) => $@"{{
  ""version"": ""0.2.0"",
  ""configurations"": [
    {{
      ""name"": ""E#: debug on the host (.NET)"",
      ""type"": ""coreclr"",
      ""request"": ""launch"",
      ""preLaunchTask"": ""E#: build for the host"",
      ""program"": ""${{workspaceFolder}}/{(project == null ? "" : project + "/")}bin/Debug/net10.0/{name}.dll"",
      ""cwd"": ""${{workspaceFolder}}"",
      ""console"": ""integratedTerminal"",
      ""stopAtEntry"": false
    }}
  ]
}}
";

    /// <summary>
    /// Workspace settings: <see cref="ToolchainSetting"/> defined (empty), so the local-toolchain tasks' ${config:...} always
    /// resolves. Put the toolchain folder here, or delete the line to use the same setting from the user settings.json.
    /// </summary>
    public const string SettingsJson = @"{
  ""esharp.toolchain"": """"
}
";

    public const string ExtensionsJson = @"{
  ""recommendations"": [ ""ms-dotnettools.csharp"" ]
}
";
}
