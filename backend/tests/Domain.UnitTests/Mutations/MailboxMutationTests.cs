// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json;
using MailFathom.Domain.Mutations;
using Xunit;

namespace MailFathom.Domain.UnitTests.Mutations;

public sealed class MailboxMutationTests
{
    /// <summary>
    /// The set is the answer to a decision rather than a list that grows with call sites: sending, the
    /// <c>\Answered</c> and <c>\Draft</c> flags, and renaming, deleting, or unsubscribing a folder are refused, and
    /// permitting one is a decision to reopen rather than a member to append. Creating a folder the operator configured
    /// was reopened and permitted, and is a capability of its own rather than a member here; <c>\Flagged</c> and the
    /// keywords were reopened and permitted, and are members because each is a change to one message. This test is what
    /// makes appending one a deliberate act.
    /// </summary>
    [Fact]
    public void All_HoldsExactlyThePermittedMutations()
    {
        // Arrange
        MailboxMutation[] expected = [
            MailboxMutation.Relocate,
            MailboxMutation.Delete,
            MailboxMutation.SetSeen,
            MailboxMutation.Copy,
            MailboxMutation.SetFlagged,
            MailboxMutation.AddKeywords,
            MailboxMutation.RemoveKeywords,
            MailboxMutation.SetKeywords,
        ];

        // Act
        var permitted = MailboxMutation.All;

        // Assert
        Assert.Equal(expected, permitted);
    }

    /// <summary>Which mutations a FLAGS response reports back is what reconciliation attributes a value by.</summary>
    /// <remarks>
    /// The three left out move a message rather than write a value on one, so a run recognizes them by where the
    /// message turned up. Admitting one of them here would make the flag-change read ask for records that can explain
    /// nothing it is looking at, and leaving one of the five out would leave that value unexplained and credited to the
    /// mailbox owner.
    /// </remarks>
    [Fact]
    public void FlagWriting_IsEveryMutationWhoseWholeEffectAFlagsResponseReports()
    {
        // Arrange
        MailboxMutation[] expected =
        [
            MailboxMutation.SetSeen,
            MailboxMutation.SetFlagged,
            MailboxMutation.AddKeywords,
            MailboxMutation.RemoveKeywords,
            MailboxMutation.SetKeywords,
        ];

        // Act
        var flagWriting = MailboxMutation.FlagWriting;

        // Assert
        Assert.Equal(expected, flagWriting);
        Assert.DoesNotContain(MailboxMutation.Relocate, flagWriting);
        Assert.DoesNotContain(MailboxMutation.Copy, flagWriting);
        Assert.DoesNotContain(MailboxMutation.Delete, flagWriting);
    }

    /// <summary>The name is what a log line, a span, and a counter dimension all show, so it is the published identity.</summary>
    [Theory]
    [InlineData("relocate")]
    [InlineData("delete")]
    [InlineData("set-seen")]
    [InlineData("copy")]
    [InlineData("set-flagged")]
    [InlineData("add-keywords")]
    [InlineData("remove-keywords")]
    [InlineData("set-keywords")]
    public void TryParseName_AnAllocatedName_ReturnsTheMutationItNames(string name)
    {
        // Act
        var parsed = MailboxMutation.TryParseName(name, out var mutation);

        // Assert
        Assert.True(parsed);
        Assert.Equal(name, mutation.Name);
    }

    [Theory]
    [InlineData("send")]
    [InlineData("create-folder")]
    [InlineData("set-answered")]
    [InlineData("rename-folder")]
    [InlineData("")]
    [InlineData(null)]
    public void TryParseName_ANameNothingPermits_IsUnknownRatherThanReconstructed(string? name)
    {
        // Act
        var parsed = MailboxMutation.TryParseName(name, out var mutation);

        // Assert
        Assert.False(parsed);
        Assert.False(mutation.IsSpecified);
    }

    /// <summary>Being a struct, the default is reachable and names nothing; reading it as a name is a bug, not a value.</summary>
    [Fact]
    public void Name_OnTheStructDefault_ThrowsRatherThanNamingAMutation()
    {
        // Arrange
        var unspecified = default(MailboxMutation);

        // Act & Assert
        Assert.False(unspecified.IsSpecified);
        Assert.Throws<InvalidOperationException>(() => unspecified.Name);
        Assert.Equal("(unspecified)", unspecified.ToString());
    }

    [Fact]
    public void Serialization_OfAPermittedMutation_RoundTripsThroughItsName()
    {
        // Act
        var json = JsonSerializer.Serialize(MailboxMutation.SetSeen);
        var restored = JsonSerializer.Deserialize<MailboxMutation>(json);

        // Assert
        Assert.Equal("\"set-seen\"", json);
        Assert.Equal(MailboxMutation.SetSeen, restored);
    }

    [Fact]
    public void Deserialization_OfANameNothingPermits_IsRefused()
    {
        // Act & Assert
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<MailboxMutation>("\"send\""));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<MailboxMutation>("3"));
    }

    [Fact]
    public void Serialization_OfTheStructDefault_IsRefused()
    {
        // Act & Assert
        Assert.Throws<JsonException>(() => JsonSerializer.Serialize(default(MailboxMutation)));
    }
}
