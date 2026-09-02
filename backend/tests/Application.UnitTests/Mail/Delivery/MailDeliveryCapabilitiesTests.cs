// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Mail.Delivery;
using Xunit;

namespace MailFathom.Application.UnitTests.Mail.Delivery;

/// <summary>Covers what a caller may conclude from what a submission server declared.</summary>
public sealed class MailDeliveryCapabilitiesTests
{
    /// <summary>The declared maximum states the largest message that is accepted rather than the first one that is not.</summary>
    [Theory]
    [InlineData(0, true)]
    [InlineData(999, true)]
    [InlineData(1000, true)]
    [InlineData(1001, false)]
    public void PermitsMessageOfSize_ServerDeclaringAMaximum_IsInclusiveOfIt(long messageBytes, bool expectedPermitted)
    {
        // Arrange
        var capabilities = new MailDeliveryCapabilities(
            1000L,
            AcceptsEightBitContent: true,
            AcceptsInternationalizedAddresses: false);

        // Act
        var permitted = capabilities.PermitsMessageOfSize(messageBytes);

        // Assert
        Assert.Equal(expectedPermitted, permitted);
    }

    /// <summary>A server that declared no bound permits every size here, whatever it later decides for itself.</summary>
    [Fact]
    public void PermitsMessageOfSize_ServerDeclaringNoMaximum_PermitsEverySize()
    {
        // Arrange
        var capabilities = new MailDeliveryCapabilities(
            MaxMessageBytes: null,
            AcceptsEightBitContent: false,
            AcceptsInternationalizedAddresses: false);

        // Act, Assert
        Assert.True(capabilities.PermitsMessageOfSize(long.MaxValue));
    }

    /// <summary>A negative size is a caller's arithmetic gone wrong rather than a message the server would refuse.</summary>
    [Fact]
    public void PermitsMessageOfSize_NegativeSize_IsRefusedAsAnArgument()
    {
        // Arrange
        var capabilities = new MailDeliveryCapabilities(
            1000L,
            AcceptsEightBitContent: false,
            AcceptsInternationalizedAddresses: false);

        // Act, Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => capabilities.PermitsMessageOfSize(-1));
    }

    /// <summary>
    /// A message written down before any submission server has been opened is held to what stays correct whatever one
    /// turns out to say: no declared size, seven-bit content, and ASCII addresses.
    /// </summary>
    /// <remarks>
    /// The two negatives are the safe direction rather than a pessimistic guess. Content encoded to seven bits is
    /// accepted by a server that would also have taken eight, while the reverse produces a message that can never be
    /// delivered; and an address outside ASCII is refused while the caller is still there rather than queued to fail
    /// against a server hours later. The absent size is the one that costs nothing at all, because the deployment's own
    /// bound is applied at composition and the server's is checked against the stored length before the message is
    /// offered.
    /// </remarks>
    [Fact]
    public void BeforeAnyServerHasSpoken_DeclaresNoSizeAndAssumesNeitherCapability()
    {
        // Arrange, Act
        var capabilities = MailDeliveryCapabilities.BeforeAnyServerHasSpoken;

        // Assert
        Assert.Null(capabilities.MaxMessageBytes);
        Assert.False(capabilities.AcceptsEightBitContent);
        Assert.False(capabilities.AcceptsInternationalizedAddresses);
        Assert.True(capabilities.PermitsMessageOfSize(long.MaxValue));
    }
}
