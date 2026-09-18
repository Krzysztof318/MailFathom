// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Retrieval;
using MailFathom.Evaluations.Corpus;
using Xunit;

namespace MailFathom.Evaluations.Answering;

/// <summary>Covers the corpus search honouring the filters the answering tool publishes, which is what a scenario about a person or a period rests on.</summary>
public sealed class CorpusKnowledgeSearchTests
{
    private const string HalinaPettersen = "halina.pettersen@latticeworks.test";

    private readonly CorpusKnowledgeSearch search = new(CorpusMessage.All);

    [Fact]
    public async Task FindPassagesAsync_ASenderFilter_ReturnsThatSendersMailAlone()
    {
        // Arrange
        var query = new EmailKnowledgeQuery { QueryText = "pickup", SenderAddress = HalinaPettersen.ToUpperInvariant() };

        // Act
        var found = await this.FindAsync(query);

        // Assert
        Assert.NotEmpty(found);
        Assert.All(found, static message => Assert.Equal(HalinaPettersen, message.Sender));
    }

    [Fact]
    public async Task FindPassagesAsync_AReceivedRange_ReturnsMailInsideItAlone()
    {
        // Arrange
        var after = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);
        var before = new DateTimeOffset(2026, 9, 8, 0, 0, 0, TimeSpan.Zero);
        var query = new EmailKnowledgeQuery { QueryText = "itinerary", ReceivedOnOrAfter = after, ReceivedBefore = before };

        // Act
        var found = await this.FindAsync(query);

        // Assert
        Assert.NotEmpty(found);
        Assert.All(found, message => Assert.InRange(message.ReceivedAt, after, before.AddTicks(-1)));
    }

    [Fact]
    public async Task FindPassagesAsync_AQueryItsWordsNarrow_RanksTheMessageCarryingMoreOfThemFirst()
    {
        // Arrange
        var query = EmailKnowledgeQuery.ForText("Lumenfield INV-4827");

        // Act
        var found = await this.FindAsync(query);

        // Assert
        Assert.Contains("Lumenfield", found[0].GroundingText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FindPassagesAsync_AnExcludedWord_LeavesEveryMessageCarryingItOut()
    {
        // Arrange
        var query = EmailKnowledgeQuery.ForText("INV-4827 -Lumenfield");

        // Act
        var found = await this.FindAsync(query);

        // Assert
        Assert.NotEmpty(found);
        Assert.DoesNotContain(found, static message => message.GroundingText.Contains("Lumenfield", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task FindPassagesAsync_AFilterOnServerStateTheCorpusDoesNotCarry_MatchesNothing()
    {
        // Arrange
        var query = new EmailKnowledgeQuery { QueryText = "invoice", IsRemotelyFlagged = true };

        // Act
        var found = await this.FindAsync(query);

        // Assert
        Assert.Empty(found);
    }

    private async Task<IReadOnlyList<CorpusMessage>> FindAsync(EmailKnowledgeQuery query)
    {
        var lookup = await this.search.FindPassagesAsync(CorpusKnowledgeSearch.Scope, query, TestContext.Current.CancellationToken);

        return [.. lookup.Passages.Select(static passage => CorpusMessage.All.Single(message => message.Id == passage.StoredEmailId))];
    }
}
