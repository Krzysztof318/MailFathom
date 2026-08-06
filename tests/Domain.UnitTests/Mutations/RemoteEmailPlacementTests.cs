// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Emails;
using MailFathom.Domain.Mutations;
using Xunit;

namespace MailFathom.Domain.UnitTests.Mutations;

public sealed class RemoteEmailPlacementTests
{
    [Fact]
    public void Reported_WithACopyUidResponse_CarriesBothHalvesOfTheIdentity()
    {
        // Act
        var placement = RemoteEmailPlacement.Reported(ImapUidValidity.Create(11U), ImapUid.Create(7U));

        // Assert
        Assert.True(placement.IsReported);
        Assert.Equal(7U, placement.Uid?.Value);
        Assert.Equal(11U, placement.UidValidity?.Value);
    }

    /// <summary>
    /// A server without UIDPLUS completes the change and says nothing about where the email landed. Searching the
    /// destination folder for it afterwards would replace a fact with a guess, so the absence is a value rather than
    /// something a caller has to interpret.
    /// </summary>
    [Fact]
    public void NotReported_Always_NamesNoOccurrenceAtAll()
    {
        // Act
        var placement = RemoteEmailPlacement.NotReported();

        // Assert
        Assert.False(placement.IsReported);
        Assert.Null(placement.Uid);
        Assert.Null(placement.UidValidity);
    }

    /// <summary>A UID is stable only inside one UIDVALIDITY, so the two never travel apart.</summary>
    [Fact]
    public void Reported_AndNotReported_AreNeverEqual()
    {
        // Arrange
        var reported = RemoteEmailPlacement.Reported(ImapUidValidity.Create(11U), ImapUid.Create(7U));
        var unreported = RemoteEmailPlacement.NotReported();

        // Act & Assert
        Assert.NotEqual(reported, unreported);
        Assert.Equal(reported, RemoteEmailPlacement.Reported(ImapUidValidity.Create(11U), ImapUid.Create(7U)));
    }
}
