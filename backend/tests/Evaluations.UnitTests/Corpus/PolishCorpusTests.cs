// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Evaluations.Corpus;
using Xunit;

namespace MailFathom.Evaluations.UnitTests.Corpus;

/// <summary>Proves the Polish corpus is safe to publish and never collides with the English mail it is read beside.</summary>
public sealed class PolishCorpusTests
{
    [Fact]
    public void All_EveryPolishMessage_CarriesAnIdentifierNoOtherMessageCarries()
    {
        // Act
        var identifiers = PolishCorpus.All
            .Concat(CorpusMessage.All)
            .Concat(WrittenCorpus.Exchanges.SelectMany(static exchange => exchange))
            .Concat(HostileMail.All)
            .Select(static message => message.Id)
            .ToArray();

        // Assert
        Assert.Equal(identifiers.Length, identifiers.Distinct().Count());
    }

    [Fact]
    public void All_EveryCorrespondentAPolishMessageNames_SitsUnderAReservedDomainNoEnglishCorrespondentShares()
    {
        // Arrange
        var english = HostileMail.Mailbox
            .Concat(WrittenCorpus.Exchanges.SelectMany(static exchange => exchange))
            .SelectMany(static message => message.Recipients.Append(message.Sender))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Act
        string[] correspondents =
        [
            .. PolishCorpus.All
                .SelectMany(static message => message.Recipients.Append(message.Sender))
                .Where(static address => !address.Equals(CorpusMailbox.OwnerAddress, StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase),
        ];

        // Assert
        Assert.NotEmpty(correspondents);
        Assert.All(correspondents, address =>
        {
            Assert.EndsWith(".test", address, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(address, english);
        });
    }
}
