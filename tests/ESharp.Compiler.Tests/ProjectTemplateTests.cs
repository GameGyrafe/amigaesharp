using System.Text.Json;
using ESharp.Cli;

namespace ESharp.Compiler.Tests;

/// <summary>What `esharp new` writes: valid VS Code files with the expected tasks, and a program the compiler accepts.</summary>
public class ProjectTemplateTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "esharp-new-" + Guid.NewGuid().ToString("N"));

    public ProjectTemplateTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    private JsonDocument Json(string rel) => JsonDocument.Parse(File.ReadAllText(Path.Combine(_dir, rel)));

    [Fact]
    public void New_writes_valid_vscode_files_with_the_expected_tasks()
    {
        ProjectTemplate.Write(_dir, "Demo", "/opt/esharp", "/opt/esharp/esharp.dll");

        var tasks = Json(".vscode/tasks.json").RootElement.GetProperty("tasks").EnumerateArray().ToList();
        var labels = tasks.Select(t => t.GetProperty("label").GetString()).ToList();
        Assert.Contains("E#: build (m68k)", labels);
        Assert.Contains("E#: run in FS-UAE", labels);
        Assert.Contains("E#: run on the host", labels);

        string Args(string label) => string.Join(" ", tasks.Single(t => t.GetProperty("label").GetString() == label)
            .GetProperty("args").EnumerateArray().Select(a => a.GetString()));
        Assert.Equal("/opt/esharp/esharp.dll build ${workspaceFolder} --target m68k --docker ${input:dockerImage}", Args("E#: build (m68k)"));
        Assert.StartsWith("/opt/esharp/esharp.dll run ${workspaceFolder}", Args("E#: run in FS-UAE"));
        Assert.Equal("run --project ${workspaceFolder}", Args("E#: run on the host"));
        var inputs = Json(".vscode/tasks.json").RootElement.GetProperty("inputs").EnumerateArray().Select(i => i.GetProperty("id").GetString());
        Assert.Contains("dockerImage", inputs);

        // Issue #122: the same build and run with a local toolchain, from the esharp.toolchain setting (empty when unset: esharp
        // then reads ESHARP_TOOLCHAIN_M68K / ESHARP_TOOLCHAIN, else PATH). Docker stays the default build task.
        Assert.Equal("/opt/esharp/esharp.dll build ${workspaceFolder} --target m68k --toolchain ${config:esharp.toolchain}", Args("E#: build (m68k, local toolchain)"));
        Assert.Equal("/opt/esharp/esharp.dll run ${workspaceFolder} --toolchain ${config:esharp.toolchain}", Args("E#: run in FS-UAE (local toolchain)"));
        var defaults = tasks.Where(t => t.TryGetProperty("group", out var g) && g.GetProperty("isDefault").GetBoolean()).Select(t => t.GetProperty("label").GetString());
        Assert.Equal(new[] { "E#: build (m68k)" }, defaults);
        Assert.All(tasks.Where(t => (t.GetProperty("label").GetString() ?? "").Contains("local toolchain")),
            t => Assert.DoesNotContain("--docker", t.GetProperty("args").EnumerateArray().Select(a => a.GetString())));

        // VS Code fails a task whose ${config:...} names a setting that is defined nowhere, so the workspace settings define it
        // (empty: esharp treats an empty --toolchain as not given).
        var toolchainSetting = Json(".vscode/settings.json").RootElement.GetProperty(ProjectTemplate.ToolchainSetting);
        Assert.Equal(JsonValueKind.String, toolchainSetting.ValueKind);
        Assert.Equal("", toolchainSetting.GetString());

        var launch = Json(".vscode/launch.json").RootElement.GetProperty("configurations")[0];
        Assert.Equal("coreclr", launch.GetProperty("type").GetString());
        Assert.Contains("Demo.dll", launch.GetProperty("program").GetString());
        Assert.Contains(launch.GetProperty("preLaunchTask").GetString(), labels);

        var rec = Json(".vscode/extensions.json").RootElement.GetProperty("recommendations").EnumerateArray().Select(r => r.GetString());
        Assert.Contains("ms-dotnettools.csharp", rec);
    }

    [Fact]
    public void New_project_csproj_loads_the_analyzer_and_the_host_target()
    {
        ProjectTemplate.Write(_dir, "Demo", "/opt/esharp", "/opt/esharp/esharp.dll");
        var csproj = File.ReadAllText(Path.Combine(_dir, "Demo.csproj"));
        Assert.Contains("/opt/esharp", csproj);
        Assert.Contains("ESharp.Target.Host.csproj", csproj);
        Assert.Contains("ESharp.Analyzers.csproj\" OutputItemType=\"Analyzer\" ReferenceOutputAssembly=\"false\"", csproj);
        System.Xml.Linq.XDocument.Parse(csproj);
    }

    [Fact]
    public void New_program_passes_the_subset_check()
    {
        ProjectTemplate.Write(_dir, "Demo", null, "esharp.dll");
        var r = TestHelpers.Compile(File.ReadAllText(Path.Combine(_dir, "Program.cs")));
        Assert.True(r.Success, string.Join("\n", r.Diagnostics.Select(d => d.ToString())));
    }

    [Fact]
    public void New_program_prints_the_greeting_and_an_ok_check_when_compiled_to_C()
    {
        if (!TestHelpers.HasGcc) return;
        var c = TestHelpers.CompileOk(ProjectTemplate.ProgramText("Demo"));
        var (code, output) = TestHelpers.RunWithHostGcc(c);
        Assert.Equal(0, code);
        Assert.Equal("Hello from E#!\nok sum of 1 to 10\n", output);
        Assert.Equal("1 ok, 0 FAIL", TestReport.Parse(output).Summary);
    }

    [Fact]
    public void Without_a_checkout_the_csproj_references_the_Sdk_package_and_the_tasks_call_esharp()
    {
        ProjectTemplate.Write(_dir, "Demo", null, "/somewhere/esharp.dll", "1.2.3");
        var csproj = File.ReadAllText(Path.Combine(_dir, "Demo.csproj"));
        var doc = System.Xml.Linq.XDocument.Parse(csproj);
        var pkg = doc.Descendants("PackageReference").Single();
        Assert.Equal("AmigaESharp.Sdk", pkg.Attribute("Include")!.Value);
        Assert.Equal("1.2.3", pkg.Attribute("Version")!.Value);
        Assert.Empty(doc.Descendants("ProjectReference"));
        Assert.Contains("$(DefineConstants);HOST", csproj);

        var tasks = Json(".vscode/tasks.json").RootElement.GetProperty("tasks").EnumerateArray().ToList();
        var build = tasks.Single(t => t.GetProperty("label").GetString() == "E#: build (m68k)");
        Assert.Equal("esharp", build.GetProperty("command").GetString());
        Assert.Equal("build ${workspaceFolder} --target m68k --docker ${input:dockerImage}",
            string.Join(" ", build.GetProperty("args").EnumerateArray().Select(a => a.GetString())));
        var host = tasks.Single(t => t.GetProperty("label").GetString() == "E#: run on the host");
        Assert.Equal("dotnet", host.GetProperty("command").GetString());
        Assert.DoesNotContain("/somewhere", File.ReadAllText(Path.Combine(_dir, ".vscode", "tasks.json")));
    }

    [Fact]
    public void Package_csproj_uses_the_tool_version_by_default()
    {
        ProjectTemplate.Write(_dir, "Demo", null, "esharp.dll");
        var pkg = System.Xml.Linq.XDocument.Parse(File.ReadAllText(Path.Combine(_dir, "Demo.csproj"))).Descendants("PackageReference").Single();
        Assert.Equal(SourceTree.ToolVersion, pkg.Attribute("Version")!.Value);
        Assert.DoesNotContain("+", SourceTree.ToolVersion);
        Assert.Matches(@"^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$", SourceTree.ToolVersion);
    }

    [Fact]
    public void The_cli_carries_the_host_libraries_and_the_os4vm_helper()
    {
        // The installed tool has no checkout: esharp test builds against host/, esharp os4vm runs os4vm/os4vm.py.
        var host = SourceTree.BundledHostLibs(AppContext.BaseDirectory);
        Assert.NotNull(host);
        foreach (var a in new[] { "ESharp.Runtime.dll", "ESharp.Platform.dll", "ESharp.Target.Host.dll" })
            Assert.True(File.Exists(Path.Combine(host!, a)), a);
        Assert.True(File.Exists(Path.Combine(AppContext.BaseDirectory, "os4vm", "os4vm.py")));
        Assert.True(File.Exists(Path.Combine(AppContext.BaseDirectory, "os4vm", "estype.c")));
    }

    [Fact]
    public void SourceTree_walks_up_and_honours_ESHARP_ROOT()
    {
        foreach (var p in new[] { "ESharp.Target.Host", "ESharp.Analyzers" })
        {
            Directory.CreateDirectory(Path.Combine(_dir, "src", p));
            File.WriteAllText(Path.Combine(_dir, "src", p, p + ".csproj"), "");
        }
        var deep = Path.Combine(_dir, "a", "b");
        Directory.CreateDirectory(deep);
        var saved = Environment.GetEnvironmentVariable("ESHARP_ROOT");
        var savedOld = Environment.GetEnvironmentVariable("ESHARP_REPO");
        try
        {
            Environment.SetEnvironmentVariable("ESHARP_ROOT", null);
            Environment.SetEnvironmentVariable("ESHARP_REPO", null);
            Assert.Equal(Path.GetFullPath(_dir), SourceTree.Find(deep));
            Environment.SetEnvironmentVariable("ESHARP_ROOT", _dir);
            Assert.Equal(Path.GetFullPath(_dir), SourceTree.Find("/"));
            Environment.SetEnvironmentVariable("ESHARP_ROOT", null);
            Environment.SetEnvironmentVariable("ESHARP_REPO", _dir);
            Assert.Equal(Path.GetFullPath(_dir), SourceTree.Find("/"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("ESHARP_ROOT", saved);
            Environment.SetEnvironmentVariable("ESHARP_REPO", savedOld);
        }
    }

    [Fact]
    public void Csproj_defines_HOST()
    {
        Assert.Contains("$(DefineConstants);HOST", ProjectTemplate.Csproj("/opt/esharp"));
    }

    [Theory]
    [InlineData("demo", "demo")]
    [InlineData("games/demo", "demo")]
    [InlineData("games/demo/", "demo")]
    [InlineData("a/b/c", "c")]
    public void ProjectName_is_the_last_segment(string path, string expected) =>
        Assert.Equal(expected, ProjectTemplate.ProjectName(path));

    [Theory]
    [InlineData("my-game")]
    [InlineData("1game")]
    [InlineData("class")]
    [InlineData("a b")]
    [InlineData("")]
    public void Invalid_names_are_rejected(string name) => Assert.NotNull(ProjectTemplate.NameProblem(name));

    [Theory]
    [InlineData("Demo")]
    [InlineData("my_game2")]
    public void Valid_names_are_accepted(string name) => Assert.Null(ProjectTemplate.NameProblem(name));

    [Fact]
    public void Suggest_gives_a_valid_name()
    {
        foreach (var n in new[] { "my-game", "1game", "class", "a b", "---" })
            Assert.Null(ProjectTemplate.NameProblem(ProjectTemplate.Suggest(n)));
        Assert.Equal("my_game", ProjectTemplate.Suggest("my-game"));
    }

    private static (int Exit, string Err) Cli(string workDir, params string[] args)
    {
        var dll = Path.Combine(AppContext.BaseDirectory, "esharp.dll");
        var psi = new System.Diagnostics.ProcessStartInfo("dotnet") { WorkingDirectory = workDir, RedirectStandardError = true, RedirectStandardOutput = true };
        psi.ArgumentList.Add(dll);
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = System.Diagnostics.Process.Start(psi)!;
        var err = p.StandardError.ReadToEndAsync();
        p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        return (p.ExitCode, err.Result);
    }

    [Fact]
    public void Esharp_new_with_a_nested_path_names_the_project_after_the_last_segment()
    {
        var (exit, _) = Cli(_dir, "new", "sub/dir");
        Assert.Equal(0, exit);
        Assert.True(File.Exists(Path.Combine(_dir, "sub", "dir", "dir.csproj")));
        Assert.Contains("namespace dir;", File.ReadAllText(Path.Combine(_dir, "sub", "dir", "Program.cs")));
        Assert.True(File.Exists(Path.Combine(_dir, "sub", "dir", ".vscode", "tasks.json")));
    }

    [Fact]
    public void Esharp_new_from_a_checkout_references_it_and_package_forces_the_package()
    {
        // The test's esharp.dll lives in this checkout, so the default is the checkout (contributors).
        Assert.Equal(0, Cli(_dir, "new", "fromsrc").Exit);
        var src = File.ReadAllText(Path.Combine(_dir, "fromsrc", "fromsrc.csproj"));
        Assert.Contains("ESharp.Target.Host.csproj", src);
        Assert.DoesNotContain("PackageReference", src);

        Assert.Equal(0, Cli(_dir, "new", "frompkg", "--package", "9.8.7").Exit);
        var pkg = File.ReadAllText(Path.Combine(_dir, "frompkg", "frompkg.csproj"));
        Assert.Contains("<PackageReference Include=\"AmigaESharp.Sdk\" Version=\"9.8.7\" />", pkg);
        Assert.DoesNotContain("ProjectReference", pkg);

        var (exit, err) = Cli(_dir, "new", "bad", "--source", _dir);
        Assert.Equal(1, exit);
        Assert.Contains("is not an E# checkout", err);
    }

    [Fact]
    public void Esharp_version_prints_the_tool_version()
    {
        var psi = new System.Diagnostics.ProcessStartInfo("dotnet") { RedirectStandardOutput = true };
        psi.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "esharp.dll"));
        psi.ArgumentList.Add("--version");
        using var p = System.Diagnostics.Process.Start(psi)!;
        var text = p.StandardOutput.ReadToEnd().Trim();
        p.WaitForExit();
        Assert.Equal(0, p.ExitCode);
        Assert.Equal(SourceTree.ToolVersion, text);
    }

    [Fact]
    public void Esharp_new_rejects_a_name_that_is_not_an_identifier()
    {
        var (exit, err) = Cli(_dir, "new", "my-game");
        Assert.Equal(1, exit);
        Assert.Contains("not a valid C# identifier", err);
        Assert.Contains("my_game", err);
        Assert.False(Directory.Exists(Path.Combine(_dir, "my-game")));
    }
}
