// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Evaluations.Corpus;
using Xunit;

namespace MailFathom.Evaluations.UnitTests.Corpus;

/// <summary>Covers reading the committed corpus the way a deployment reads the mail it stores.</summary>
public sealed class CorpusMessageTests
{
    /// <summary>A message the corpus carries as markup alone: Zofia Iversen's request for the corrected INV-4827.</summary>
    private const int MarkupOnlyPosition = 20;

    [Fact]
    public void At_AMessageCarryingMarkupAlone_IsCutFromTheTextItDisplays()
    {
        // Act
        var message = CorpusMessage.At(MarkupOnlyPosition);

        // Assert
        Assert.Contains("42 Lantern Way", message.GroundingText, StringComparison.Ordinal);
        Assert.DoesNotContain("<", string.Concat(message.Passages.Select(static passage => passage.Text)), StringComparison.Ordinal);
    }

    [Fact]
    public void All_EveryMessage_CarriesAnIdentifierNoOtherMessageCarries()
    {
        // Act
        var identifiers = CorpusMessage.All.Select(static message => message.Id).ToArray();

        // Assert
        Assert.Equal(identifiers.Length, identifiers.Distinct().Count());
    }
}
