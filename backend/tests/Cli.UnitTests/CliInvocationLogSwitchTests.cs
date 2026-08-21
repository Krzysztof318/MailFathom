// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.CommandLine;
using Xunit;

namespace MailFathom.Cli.UnitTests;

/// <summary>Covers the order that decides whether an invocation is recorded at all.</summary>
/// <remarks>
/// The same order every other input to this command follows — what the operator typed beats what their shell was told
/// — asserted against a stated value of the variable rather than against the process environment, which every other
/// test in this assembly shares and any of them could be reading while these run.
/// </remarks>
public sealed class CliInvocationLogSwitchTests
{
    /// <summary>An invocation that said nothing about the log is recorded.</summary>
    [Fact]
    public void RecordsInvocation_AnInvocationThatSaidNothing_IsRecorded()
    {
        // Arrange

        // Act
        var records = CliOptions.RecordsInvocation(Parse([]), logVariable: null);

        // Assert
        Assert.True(records);
    }

    /// <summary>The option turns the record off wherever in the command tree it was written.</summary>
    [Theory]
    [InlineData("--no-log")]
    [InlineData("profiles", "--no-log")]
    public void RecordsInvocation_AnInvocationThatPassedTheOption_IsNotRecorded(params string[] args)
    {
        // Arrange

        // Act
        var records = CliOptions.RecordsInvocation(Parse(args), logVariable: null);

        // Assert
        Assert.False(records);
    }

    /// <summary>The variable turns the record off for a shell session, whatever case it is spelled in.</summary>
    [Theory]
    [InlineData("off")]
    [InlineData("OFF")]
    [InlineData("Off")]
    public void RecordsInvocation_AShellThatTurnedTheLogOff_IsNotRecorded(string variable)
    {
        // Arrange

        // Act
        var records = CliOptions.RecordsInvocation(Parse([]), variable);

        // Assert
        Assert.False(records);
    }

    /// <summary>Every other value of the variable leaves the log on rather than failing the command over a typo.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("on")]
    [InlineData("0")]
    [InlineData("false")]
    [InlineData("no")]
    public void RecordsInvocation_AVariableSpelledSomeOtherWay_IsStillRecorded(string variable)
    {
        // Arrange

        // Act
        var records = CliOptions.RecordsInvocation(Parse([]), variable);

        // Assert
        Assert.True(records);
    }

    private static ParseResult Parse(string[] args) => new RootCommand("test")
    {
        CliOptions.NoLog(),
        new Command("profiles", "A subcommand, so the option's reach past the root is what is asserted."),
    }.Parse(args);
}
