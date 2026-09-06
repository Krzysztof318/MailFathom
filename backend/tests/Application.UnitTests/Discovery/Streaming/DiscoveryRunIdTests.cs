// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Discovery.Streaming;
using Xunit;

namespace MailFathom.Application.UnitTests.Discovery.Streaming;

/// <summary>Covers what a run may be addressed by, which arrives off the wire and is therefore never trusted.</summary>
public sealed class DiscoveryRunIdTests
{
    /// <summary>The empty UUID is what a body that named no run parses as, so it names no run here either.</summary>
    [Fact]
    public void Create_TheEmptyIdentifier_IsRefused() =>
        Assert.Throws<ArgumentException>(() => DiscoveryRunId.Create(Guid.Empty));

    /// <summary>A client is handed this and presents it back, so an identifier has to read back as the one it named.</summary>
    [Fact]
    public void Create_AnIdentifierAClientPresentsBack_ReadsBackAsTheRunItNames()
    {
        // Arrange
        var opened = DiscoveryRunId.New();

        // Act
        var presented = DiscoveryRunId.Create(opened.Value);

        // Assert
        Assert.Equal(opened, presented);
        Assert.Equal(opened.Value.ToString(), presented.ToString());
    }

    /// <summary>Two runs are told apart by their identifiers, which is the whole of what addresses one.</summary>
    [Fact]
    public void New_TwoRuns_AreAddressedSeparately() =>
        Assert.NotEqual(DiscoveryRunId.New(), DiscoveryRunId.New());
}
