// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json;
using MailFathom.Domain.Delivery.Filing;
using MailFathom.Domain.Folders;
using Xunit;

namespace MailFathom.Domain.UnitTests.Delivery.Filing;

public sealed class OutgoingMailFilingTests
{
    /// <summary>
    /// The set is the answer to a decision rather than a list that grows with call sites: three stages of an outgoing
    /// message have a place in the mailbox, and a fourth is a member to append deliberately rather than a second
    /// filing mechanism to build somewhere else. This test is what makes appending one a deliberate act.
    /// </summary>
    [Fact]
    public void All_HoldsExactlyThePlacesAnOutgoingMessageIsFiled()
    {
        // Arrange
        OutgoingMailFiling[] expected = [
            OutgoingMailFiling.Draft,
            OutgoingMailFiling.Held,
            OutgoingMailFiling.Sent,
        ];

        // Act
        var filings = OutgoingMailFiling.All;

        // Assert
        Assert.Equal(expected, filings);
    }

    /// <summary>The role and the flags are one answer, because what a folder means is what its copy looks like.</summary>
    [Theory]
    [InlineData("draft", MailFolderSpecialUse.Drafts, true, false)]
    [InlineData("held", MailFolderSpecialUse.Outbox, true, false)]
    [InlineData("sent", MailFolderSpecialUse.Sent, false, true)]
    public void Members_NameTheRoleAndTheFlagsThatFolderMeans(
        string name,
        MailFolderSpecialUse role,
        bool isDraft,
        bool isSeen)
    {
        // Arrange
        Assert.True(OutgoingMailFiling.TryParseName(name, out var filing));

        // Act
        var flags = filing.Flags;

        // Assert
        Assert.Equal(role, filing.Role);
        Assert.Equal(isDraft, flags.IsDraft);
        Assert.Equal(isSeen, flags.IsSeen);
    }

    /// <summary>Only the mirror of a waiting message goes away; a draft and a sent copy are what the owner keeps.</summary>
    [Fact]
    public void IsWithdrawnWhenTheMessageLeaves_IsTrueOfTheOutboxMirrorAlone()
    {
        // Act
        var withdrawn = OutgoingMailFiling.All.Where(filing => filing.IsWithdrawnWhenTheMessageLeaves);

        // Assert
        Assert.Equal([OutgoingMailFiling.Held], withdrawn);
    }

    /// <summary>The name is what a durable row, a log line, and a counter dimension all show, so it is the identity.</summary>
    [Theory]
    [InlineData("draft")]
    [InlineData("held")]
    [InlineData("sent")]
    public void TryParseName_AnAllocatedName_ReturnsTheFilingItNames(string name)
    {
        // Act
        var parsed = OutgoingMailFiling.TryParseName(name, out var filing);

        // Assert
        Assert.True(parsed);
        Assert.Equal(name, filing.Name);
    }

    [Theory]
    [InlineData("outbox")]
    [InlineData("archived")]
    [InlineData("junk")]
    [InlineData("")]
    [InlineData(null)]
    public void TryParseName_ANameNothingFilesInto_IsUnknownRatherThanReconstructed(string? name)
    {
        // Act
        var parsed = OutgoingMailFiling.TryParseName(name, out var filing);

        // Assert
        Assert.False(parsed);
        Assert.False(filing.IsSpecified);
    }

    /// <summary>Being a struct, the default is reachable and names nothing; reading it as a name is a bug, not a value.</summary>
    [Fact]
    public void Name_OnTheStructDefault_ThrowsRatherThanNamingAFiling()
    {
        // Arrange
        var unspecified = default(OutgoingMailFiling);

        // Act & Assert
        Assert.False(unspecified.IsSpecified);
        Assert.Throws<InvalidOperationException>(() => unspecified.Name);
        Assert.Equal("(unspecified)", unspecified.ToString());
    }

    [Fact]
    public void Serialization_OfAFiling_RoundTripsThroughItsName()
    {
        // Act
        var json = JsonSerializer.Serialize(OutgoingMailFiling.Sent);
        var restored = JsonSerializer.Deserialize<OutgoingMailFiling>(json);

        // Assert
        Assert.Equal("\"sent\"", json);
        Assert.Equal(OutgoingMailFiling.Sent, restored);
    }

    [Fact]
    public void Deserialization_OfANameNothingFilesInto_IsRefused()
    {
        // Act & Assert
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<OutgoingMailFiling>("\"outbox\""));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<OutgoingMailFiling>("2"));
    }

    [Fact]
    public void Serialization_OfTheStructDefault_IsRefused()
    {
        // Act & Assert
        Assert.Throws<JsonException>(() => JsonSerializer.Serialize(default(OutgoingMailFiling)));
    }
}
