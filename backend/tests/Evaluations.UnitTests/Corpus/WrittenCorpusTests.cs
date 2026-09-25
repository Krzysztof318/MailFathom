// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Evaluations.Corpus;
using Xunit;

namespace MailFathom.Evaluations.UnitTests.Corpus;

/// <summary>Proves the written conversations are safe to publish and never collide with the corpus they stand beside.</summary>
public sealed class WrittenCorpusTests
{
    [Fact]
    public void Exchanges_EveryWrittenMessage_CarriesAnIdentifierNoOtherMessageCarries()
    {
        // Arrange
        var written = WrittenCorpus.Exchanges.SelectMany(static exchange => exchange).ToArray();

        // Act
        var identifiers = written.Concat(CorpusMessage.All).Concat(HostileMail.All).Select(static message => message.Id).ToArray();

        // Assert
        Assert.Equal(identifiers.Length, identifiers.Distinct().Count());
    }

    [Fact]
    public void Exchanges_EveryAddressAnyWrittenMessageNames_SitsUnderAReservedDomain()
    {
        // Act
        string[] addresses =
        [
            .. WrittenCorpus.Exchanges
                .SelectMany(static exchange => exchange)
                .SelectMany(static message => message.Recipients.Append(message.Sender))
                .Distinct(StringComparer.OrdinalIgnoreCase),
        ];

        // Assert
        Assert.NotEmpty(addresses);
        Assert.All(addresses, static address =>
            Assert.True(
                address.EndsWith(".test", StringComparison.OrdinalIgnoreCase),
                $"{address} sits outside the reserved .test domain."));
    }
}
