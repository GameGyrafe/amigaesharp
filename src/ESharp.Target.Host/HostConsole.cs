using System;
using System.IO;
using ESharp.Platform;

namespace ESharp.Target.Host;

/// <summary>Headless fakes for running E# game logic under dotnet and xUnit.</summary>
public sealed class HostConsole : IDisposable
{
    private readonly TextWriter _previous;
    private readonly StringWriter _captured = new();

    private HostConsole()
    {
        _previous = Dos.Out;
        Dos.Out = _captured;
    }

    /// <summary>Redirects <see cref="Dos"/> output into <see cref="Output"/> until disposed.</summary>
    public static HostConsole Capture() => new();

    public string Output => _captured.ToString();

    public void Dispose() => Dos.Out = _previous;
}
