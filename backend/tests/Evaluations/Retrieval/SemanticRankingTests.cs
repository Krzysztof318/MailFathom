// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Embeddings;
using Xunit;

namespace MailFathom.Evaluations.Retrieval;

public sealed class SemanticRankingTests
{
    [Fact]
    public void Rank_TwoMessagesTiedOnSimilarityAndDate_PutsTheGreaterIdentifierFirstAsTheIndexReaderDoes()
    {
        // Arrange
        var first = RetrievalCases.Mailbox[0];
        var second = RetrievalCases.Mailbox[1] with { ReceivedAt = first.ReceivedAt };
        var vector = EmbeddingVector.Create([1f, 0f]);
        IReadOnlyList<EmbeddingVector> passages = [vector];

        // Act
        var ranking = SemanticRanking.Rank([(first, passages), (second, passages)], vector);

        // Assert
        var greaterIdentifierFirst = new[] { first, second }
            .OrderByDescending(static message => message.Id.Value)
            .Select(static message => message.Id);

        Assert.Equal(greaterIdentifierFirst, ranking);
    }
}
