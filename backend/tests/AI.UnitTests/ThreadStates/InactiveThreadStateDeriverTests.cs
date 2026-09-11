// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.AI.ThreadStates;
using MailFathom.Application.Emails.ThreadStates;
using MailFathom.Domain.Access;
using MailFathom.Domain.Emails;
using Xunit;

namespace MailFathom.AI.UnitTests.ThreadStates;

/// <summary>Covers what a deployment that has not turned a conversation's state on derives, which is nothing at all.</summary>
/// <remarks>
/// The absence is a renderable state rather than a failure: the pass reads a reason it can act on, and the conversation
/// stays outstanding for a deployment that later turns the switch on.
/// </remarks>
public sealed class InactiveThreadStateDeriverTests
{
    /// <summary>What the pass reads before it queries, so a deployment deriving nothing scans for nothing.</summary>
    [Fact]
    public void IsActive_ADeploymentThatHasNotTurnedItOn_SaysSoWithoutBeingAskedAboutAConversation()
    {
        // Act and assert
        Assert.False(InactiveThreadStateDeriver.Instance.IsActive);
    }

    [Fact]
    public async Task DeriveAsync_ADeploymentThatHasNotTurnedItOn_WithholdsRatherThanFailing()
    {
        // Act
        var derivation = await InactiveThreadStateDeriver.Instance.DeriveAsync(
            Derivable(),
            MailUserLanguage.English,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.False(derivation.IsSettled);
        Assert.Equal(ThreadStateWithholding.NotActivated, derivation.Withheld);
        Assert.Empty(derivation.Entries);
    }

    /// <summary>The messages are never touched, so an instance in this state cannot disclose an exchange.</summary>
    [Fact]
    public async Task DeriveAsync_AConversationPastTheBound_AnswersTheSameWay()
    {
        // Arrange
        var beyondTheBound = new DerivableThread(
            EmailThreadId.Create(Guid.CreateVersion7()),
            Subject: null,
            new ThreadStateRevision(400, null),
            [],
            ExceedsBound: true);

        // Act
        var derivation = await InactiveThreadStateDeriver.Instance.DeriveAsync(
            beyondTheBound,
            MailUserLanguage.English,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(ThreadStateWithholding.NotActivated, derivation.Withheld);
    }

    [Fact]
    public async Task DeriveAsync_ACancelledCaller_StopsRatherThanAnswering()
    {
        // Arrange
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        // Act and assert
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            InactiveThreadStateDeriver.Instance.DeriveAsync(Derivable(), MailUserLanguage.English, cancellation.Token));
    }

    private static DerivableThread Derivable() =>
        new(
            EmailThreadId.Create(Guid.CreateVersion7()),
            "The racking quotation",
            new ThreadStateRevision(1, new DateTimeOffset(2026, 9, 7, 9, 0, 0, TimeSpan.Zero)),
            [
                new DerivableThreadMessage(
                    StoredEmailId.Create(Guid.CreateVersion7()),
                    0,
                    "Karolina",
                    new DateTimeOffset(2026, 9, 7, 9, 0, 0, TimeSpan.Zero),
                    "the price holds"),
            ],
            ExceedsBound: false);
}
