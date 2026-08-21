// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics;
using MailFathom.Cli.Credentials;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace MailFathom.Cli.UnitTests;

/// <summary>Covers the one rule every command that does something irreversible asks its question under.</summary>
/// <remarks>
/// Asserted here as well as through each command, because this is now the only place the rule is written: a command
/// asking without the flag, an unattended invocation refused rather than guessed at, and an answered question honoured
/// either way. What each command still owns is the sentence it refuses with and the question it puts.
/// </remarks>
public sealed class CliConfirmationTests
{
    private const string UnattendedRefusal =
        "There is nobody at the terminal to agree to this, and erasing a contact cannot be undone. Pass --yes to erase without being asked.";

    private const string Question = "Erase that contact and everything derived from it? [y/N] ";

    private readonly RecordingCliConsole console = new();

    /// <summary>The flag is an operator stating the agreement in the command, so nothing is asked and nobody is needed.</summary>
    [Fact]
    public void Agreed_TheAgreementStatedInTheCommand_AsksNothing()
    {
        // Arrange
        this.console.AnswersQuestions = false;

        // Act
        var agreed = CliConfirmation.Agreed(this.Context(), confirmedUpFront: true, UnattendedRefusal, Question);

        // Assert
        Assert.True(agreed);
        Assert.Empty(this.console.Questions);
    }

    /// <summary>
    /// A redirected input has nobody to ask, and reading the answer out of whatever was piped in would turn a stray
    /// line into an agreement to erase somebody. Such an invocation is told to pass the flag instead.
    /// </summary>
    [Fact]
    public void Agreed_NobodyAtTheTerminalAndNoFlag_RefusesWithTheCommandsOwnSentence()
    {
        // Arrange
        this.console.AnswersQuestions = false;
        this.console.AnswerToGive = true;

        // Act
        var failure = Assert.Throws<CliFailure>(
            () => CliConfirmation.Agreed(this.Context(), confirmedUpFront: false, UnattendedRefusal, Question));

        // Assert
        Assert.Equal(UnattendedRefusal, failure.Message);
        Assert.Empty(this.console.Questions);
    }

    /// <summary>Somebody is at the terminal, so the answer is theirs — including the one that stops the command.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Agreed_SomebodyAtTheTerminal_IsWhateverTheyAnswered(bool answer)
    {
        // Arrange
        this.console.AnswerToGive = answer;

        // Act
        var agreed = CliConfirmation.Agreed(this.Context(), confirmedUpFront: false, UnattendedRefusal, Question);

        // Assert
        Assert.Equal(answer, agreed);
        Assert.Equal([Question], this.console.Questions);
    }

    /// <summary>The terminal is the only thing the rule reaches, and nothing here opens a store, a transport, or a browser.</summary>
    private CliContext Context() => new(
        this.console,
        new CredentialStore("credentials.json", new TokenProtector("credentials.key")),
        (_, _) => throw new UnreachableException("A confirmation reaches no deployment."),
        _ => throw new UnreachableException("A confirmation awaits no redirect."),
        _ => throw new UnreachableException("A confirmation opens no browser."),
        new FakeTimeProvider(new DateTimeOffset(2026, 8, 20, 12, 0, 0, TimeSpan.Zero)));
}
