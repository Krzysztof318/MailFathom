// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Cli.Output;
using Xunit;

namespace MailFathom.Cli.UnitTests.Output;

/// <summary>Covers what decides whether a stream is drawn on in colour.</summary>
/// <remarks>
/// The decision is a function of what the platform reports rather than of the platform itself, which is what makes the
/// promise to a redirected run assertable at all: reading the process's own console would make every case here depend on
/// how the suite happened to be started.
/// </remarks>
public sealed class CliTerminalTests
{
    [Fact]
    public void Decide_RedirectedStream_PermitsNoColour()
    {
        // Act
        var terminal = CliTerminal.Decide(redirected: true, refusedColour: null);

        // Assert
        Assert.False(terminal.PermitsColour);
    }

    [Fact]
    public void Decide_TerminalWithNoColourRefusal_PermitsColour()
    {
        // Act
        var terminal = CliTerminal.Decide(redirected: false, refusedColour: null);

        // Assert
        Assert.True(terminal.PermitsColour);
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
        var terminal = CliTerminal.Decide(redirected: false, refusedColour);

        // Assert
        Assert.False(terminal.PermitsColour);
    }

    [Fact]
    public void Decide_EmptyColourRefusal_PermitsColour()
    {
        // Act
        var terminal = CliTerminal.Decide(redirected: false, refusedColour: string.Empty);

        // Assert
        Assert.True(terminal.PermitsColour);
    }

    /// <summary>Proves a redirected stream refuses colour whatever the environment says.</summary>
    /// <remarks>
    /// A run that both redirects and sets <c>NO_COLOR</c> has asked for the same thing twice, and the value it set must
    /// not be read as permission: an empty <c>NO_COLOR</c> permits colour on a terminal and permits none here.
    /// </remarks>
    [Fact]
    public void Decide_RedirectedStreamWithEmptyColourRefusal_StillPermitsNone()
    {
        // Act
        var terminal = CliTerminal.Decide(redirected: true, refusedColour: string.Empty);

        // Assert
        Assert.False(terminal.PermitsColour);
    }
}
