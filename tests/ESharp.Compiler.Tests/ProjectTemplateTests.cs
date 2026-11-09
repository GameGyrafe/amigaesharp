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
    public void FindRoot_walks_up_to_the_solution()
    {
        File.WriteAllText(Path.Combine(_dir, "ESharp.sln"), "");
        var deep = Path.Combine(_dir, "a", "b");
        Directory.CreateDirectory(deep);
        var saved = Environment.GetEnvironmentVariable("ESHARP_ROOT");
        Environment.SetEnvironmentVariable("ESHARP_ROOT", null);
        try { Assert.Equal(Path.GetFullPath(_dir), ProjectTemplate.FindRoot(deep)); }
        finally { Environment.SetEnvironmentVariable("ESHARP_ROOT", saved); }
    }
}
