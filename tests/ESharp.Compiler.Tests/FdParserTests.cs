namespace ESharp.Compiler.Tests;

// The fd reader in tools/BindGen, fed an invented library (no SDK text): vectors, private slots, version markers, bias jumps.
public class FdParserTests
{
    private const string Invented = """
        ##base _WidgetBase
        ##bias 30
        ##public
        MakeWidget(name,size)(a0,d0)
        ##private
        widgetPrivate1()()
        ##public
        *--- functions in V39 or higher (made up) ---
        SpinWidget(w,turns)(a0/d1)
        *--- (2 function slots reserved here) ---
        ##bias 66
        *--- functions in V45 or higher ---
        FoldWidget(w)(a1)
        ##end
        """;

    [Fact]
    public void Fd_files_give_vectors_registers_and_first_versions()
    {
        var funcs = Fd.ParseText(Invented);
        Assert.Equal(new[] { "MakeWidget", "SpinWidget", "FoldWidget" }, funcs.Select(f => f.Name));
        Assert.Equal(new[] { -30, -42, -66 }, funcs.Select(f => -6 * f.Lvo));        // the private slot at -36 still counts
        Assert.Equal(new[] { 0, 39, 45 }, funcs.Select(f => f.Since));                // no marker yet means the library's first version
        Assert.Equal(new[] { "A0", "D0" }, funcs[0].Args.Select(a => a.Register));
        Assert.Equal(new[] { "w", "turns" }, funcs[1].Args.Select(a => a.Name));
        Assert.Equal("D1", funcs[1].Args[1].Register);
    }
}
