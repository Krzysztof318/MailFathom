// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
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
}
