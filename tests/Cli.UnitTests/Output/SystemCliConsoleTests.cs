// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Cli.Output;
using Xunit;

namespace MailFathom.Cli.UnitTests.Output;

/// <summary>Covers which stream each thing a command writes lands on, and what is marked on the way.</summary>
/// <remarks>
/// The split is the contract a redirected invocation depends on: what it captures is the command's result and nothing
/// else, while the person who started it still reads the guidance, the cautions, and the failures. Colour is decided per
/// stream for the same reason, so a piped result and a watched terminal can disagree about it.
/// </remarks>
public sealed class SystemCliConsoleTests
{
    [Fact]
    public void WriteLine_AResult_ReachesStandardOutputAlone()
    {
        // Arrange
        StringWriter output = new();
        StringWriter error = new();
        var console = Console(output, error);

        // Act
        console.WriteLine("a-refresh-token");

        // Assert
        Assert.Equal(["a-refresh-token"], Lines(output));
        Assert.Empty(Lines(error));
    }

    [Fact]
    public void WriteNotice_Guidance_ReachesStandardErrorUnmarked()
    {
        // Arrange
        StringWriter output = new();
        StringWriter error = new();
        var console = Console(output, error, colouredError: true);

        // Act
        console.WriteNotice("Open this address on any device with a browser:");

        // Assert
        Assert.Empty(Lines(output));
        Assert.Equal(["Open this address on any device with a browser:"], Lines(error));
    }

    [Fact]
    public void WriteWarning_ACaution_ReachesStandardErrorMarked()
    {
        // Arrange
        StringWriter output = new();
        StringWriter error = new();
        var console = Console(output, error, colouredError: true);

        // Act
        console.WriteWarning("This deployment is from a newer release line.");

        // Assert
        Assert.Empty(Lines(output));
        Assert.Contains(EscapeSequence, error.ToString(), StringComparison.Ordinal);
        Assert.Contains("This deployment is from a newer release line.", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void WriteError_AFailure_ReachesStandardErrorMarked()
    {
        // Arrange
        StringWriter output = new();
        StringWriter error = new();
        var console = Console(output, error, colouredError: true);

        // Act
        console.WriteError("Nothing was erased.");

        // Assert
        Assert.Empty(Lines(output));
        Assert.Contains(EscapeSequence, error.ToString(), StringComparison.Ordinal);
        Assert.Contains("Nothing was erased.", error.ToString(), StringComparison.Ordinal);
    }

    /// <summary>Proves that the two streams decide colour separately rather than sharing one answer.</summary>
    /// <remarks>
    /// The case is ordinary rather than contrived: an operator pipes a listing into a file and reads the diagnostics on
    /// the terminal the command was started from, so the result must carry no escape sequence while the failure beside
    /// it still does.
    /// </remarks>
    [Fact]
    public void Write_ResultPipedWhileDiagnosticsAreWatched_MarksOnlyTheDiagnostics()
    {
        // Arrange
        StringWriter output = new();
        StringWriter error = new();
        var console = Console(output, error, colouredError: true);
        CliTable listing = new("Profile", "Endpoint");
        listing.AddRow("production", "https://deployment.example.test");

        // Act
        console.Write(listing);
        console.WriteError("Nothing was erased.");

        // Assert
        Assert.DoesNotContain(EscapeSequence, output.ToString(), StringComparison.Ordinal);
        Assert.Equal(
            ["Profile     Endpoint", "production  https://deployment.example.test"],
            Lines(output));
        Assert.Contains(EscapeSequence, error.ToString(), StringComparison.Ordinal);
    }

    /// <summary>What a marked line opens with, and the whole of what a stream permitting no colour must never carry.</summary>
    private const string EscapeSequence = "\u001b";

    private static SystemCliConsole Console(StringWriter output, StringWriter error, bool colouredError = false) =>
        new(
            output,
            new CliTerminal(PermitsColour: false),
            error,
            new CliTerminal(colouredError));

    private static IReadOnlyList<string> Lines(StringWriter writer) =>
        [.. writer.ToString().Split('\n').SkipLast(1).Select(line => line.TrimEnd('\r'))];
}
