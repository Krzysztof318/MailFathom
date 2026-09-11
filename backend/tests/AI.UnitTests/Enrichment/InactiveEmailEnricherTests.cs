// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.AI.Enrichment;
using MailFathom.Application.Emails.Chunking;
using MailFathom.Application.Emails.Enrichment;
using MailFathom.Domain.Access;
using MailFathom.Domain.Emails;
using Xunit;

namespace MailFathom.AI.UnitTests.Enrichment;

/// <summary>Covers what a deployment that has not turned enrichment on derives, which is nothing at all.</summary>
/// <remarks>
/// The absence is a renderable state rather than a failure: the pass reads a reason it can act on and reports it, and
/// the message stays outstanding for a deployment that later turns the switch on.
/// </remarks>
public sealed class InactiveEmailEnricherTests
{
    /// <summary>What the pass reads before it queries, so a deployment deriving nothing scans for nothing.</summary>
    [Fact]
    public void IsActive_ADeploymentThatHasNotTurnedItOn_SaysSoWithoutBeingAskedAboutAMessage()
    {
        // Act & Assert
        Assert.False(InactiveEmailEnricher.Instance.IsActive);
    }

    [Fact]
    public async Task DeriveAsync_ADeploymentThatHasNotTurnedItOn_WithholdsRatherThanFailing()
    {
        // Act
        var derivation = await InactiveEmailEnricher.Instance.DeriveAsync(
            Enrichable(),
            MailUserLanguage.English,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.False(derivation.IsSettled);
        Assert.Equal(EmailEnrichmentWithholding.NotActivated, derivation.Withheld);
        Assert.Empty(derivation.Marks);
    }

    /// <summary>The passages are never touched, so an instance in this state cannot disclose a message.</summary>
    [Fact]
    public async Task DeriveAsync_AMessageWithNoPassagesAtAll_AnswersTheSameWay()
    {
        // Arrange
        var withoutPassages = new EnrichableEmail(
            StoredEmailId.Create(Guid.CreateVersion7()),
            Subject: null,
            ReceivedAt: null,
            []);

        // Act
        var derivation = await InactiveEmailEnricher.Instance.DeriveAsync(
            withoutPassages,
            MailUserLanguage.English,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(EmailEnrichmentWithholding.NotActivated, derivation.Withheld);
    }

    [Fact]
    public async Task DeriveAsync_ACancelledCaller_StopsRatherThanAnswering()
    {
        // Arrange
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            InactiveEmailEnricher.Instance.DeriveAsync(Enrichable(), MailUserLanguage.English, cancellation.Token));
    }

    private static EnrichableEmail Enrichable() =>
        new(
            StoredEmailId.Create(Guid.CreateVersion7()),
            "The racking quotation",
            new DateTimeOffset(2026, 9, 5, 12, 0, 0, TimeSpan.Zero),
            [new EnrichablePassage(EmailChunkId.Create(Guid.CreateVersion7()), 0, "the quotation is attached")]);
}
