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
    public void Esharp_new_rejects_a_name_that_is_not_an_identifier()
    {
        var (exit, err) = Cli(_dir, "new", "my-game");
        Assert.Equal(1, exit);
        Assert.Contains("not a valid C# identifier", err);
        Assert.Contains("my_game", err);
        Assert.False(Directory.Exists(Path.Combine(_dir, "my-game")));
    }
}
