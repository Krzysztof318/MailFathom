// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Cli.Output;
using Xunit;

namespace MailFathom.Cli.UnitTests.Output;

/// <summary>Covers what decides whether a stream is drawn on in colour, and how wide.</summary>
/// <remarks>
/// The decision is a function of what the platform reports rather than of the platform itself, which is what makes the
/// promise to a redirected run assertable at all: reading the process's own console would make every case here depend on
/// how the suite happened to be started.
/// </remarks>
public sealed class CliTerminalTests
{
    [Fact]
    public void Decide_RedirectedStream_PermitsNoColourAndIsNotWrapped()
    {
        // Act
        var terminal = CliTerminal.Decide(redirected: true, refusedColour: null, reportedWidth: 120);

        // Assert
        Assert.False(terminal.PermitsColour);
        Assert.Equal(CliTerminal.WidthWhenRedirected, terminal.Width);
    }

    [Fact]
    public void Decide_TerminalWithNoColourRefusal_PermitsColourAtTheReportedWidth()
    {
        // Act
        var terminal = CliTerminal.Decide(redirected: false, refusedColour: null, reportedWidth: 120);

        // Assert
        Assert.True(terminal.PermitsColour);
        Assert.Equal(120, terminal.Width);
    }

    /// <summary>Proves the convention as written: presence with any non-empty value refuses colour.</summary>
    /// <remarks>
    /// The zero case is the one worth naming. A run setting <c>NO_COLOR=0</c> is asking for no colour like any other,
    /// and reading the value as a switch would give that run exactly what it asked not to have.
    /// </remarks>
    [Theory]
    [InlineData("1")]
    [InlineData("0")]
    [InlineData("anything")]
    public void Decide_TerminalRefusingColour_PermitsNone(string refusedColour)
    {
        // Act
        var terminal = CliTerminal.Decide(redirected: false, refusedColour, reportedWidth: 120);

        // Assert
        Assert.False(terminal.PermitsColour);
        Assert.Equal(120, terminal.Width);
    }

    [Fact]
    public void Decide_EmptyColourRefusal_PermitsColour()
    {
        // Act
        var terminal = CliTerminal.Decide(redirected: false, refusedColour: string.Empty, reportedWidth: 120);

        // Assert
        Assert.True(terminal.PermitsColour);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Decide_TerminalReportingNoWidth_FallsBackToTheConventionalOne(int reportedWidth)
    {
        // Act
        var terminal = CliTerminal.Decide(redirected: false, refusedColour: null, reportedWidth);

        // Assert
        Assert.Equal(CliTerminal.WidthWhenUnreported, terminal.Width);
    }
}
