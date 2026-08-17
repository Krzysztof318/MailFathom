// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Delivery;
using MailFathom.Domain.Emails;
using Xunit;

namespace MailFathom.Domain.UnitTests.Delivery;

/// <summary>
/// Covers where an answer is placed in a conversation: the parent it names, the path it inherits, what a bounded path
/// keeps, and which identifiers are dropped rather than written into a header.
/// </summary>
public sealed class OutgoingThreadPlacementTests
{
    /// <summary>The answered message's own identity is the parent, and the path it carried is inherited with that identity appended.</summary>
    [Fact]
    public void Answering_MessageCarryingAPath_NamesItAsTheParentAndAppendsItLast()
    {
        // Arrange
        var answered = EmailThreadReferences.Create(
            "parent@example.test",
            "root@example.test",
            ["root@example.test", "middle@example.test"]);

        // Act
        var placement = OutgoingThreadPlacement.Answering(answered);

        // Assert
        Assert.Equal("parent@example.test", placement.InReplyTo);
        Assert.Equal(
            ["root@example.test", "middle@example.test", "parent@example.test"],
            placement.References);
    }

    /// <summary>A message that started a conversation carries no path, so the answer's path is that message alone.</summary>
    [Fact]
    public void Answering_MessageWithNoReferences_WritesAPathOfTheAnsweredMessageAlone()
    {
        // Arrange
        var answered = EmailThreadReferences.Create("root@example.test", inReplyTo: null, references: null);

        // Act
        var placement = OutgoingThreadPlacement.Answering(answered);

        // Assert
        Assert.Equal("root@example.test", placement.InReplyTo);
        Assert.Equal(["root@example.test"], placement.References);
    }

    /// <summary>
    /// A message that carried no identity can be answered and cannot be pointed at, so the answer inherits the path and
    /// names no parent. Naming an ancestor instead would attach the reply to the wrong message in the same thread.
    /// </summary>
    [Fact]
    public void Answering_MessageWithoutAnIdentity_InheritsThePathAndNamesNoParent()
    {
        // Arrange
        var answered = EmailThreadReferences.Create(
            messageId: null,
            inReplyTo: null,
            ["root@example.test"]);

        // Act
        var placement = OutgoingThreadPlacement.Answering(answered);

        // Assert
        Assert.Null(placement.InReplyTo);
        Assert.Equal(["root@example.test"], placement.References);
        Assert.True(placement.IsThreaded);
    }

    /// <summary>A message with no threading headers at all leaves the answer threaded to nothing.</summary>
    [Fact]
    public void Answering_MessageWithNoThreadingHeaders_PlacesTheAnswerInNoConversation()
    {
        // Act
        var placement = OutgoingThreadPlacement.Answering(EmailThreadReferences.None);

        // Assert
        Assert.Same(OutgoingThreadPlacement.None, placement);
        Assert.False(placement.IsThreaded);
        Assert.Null(placement.InReplyTo);
        Assert.Empty(placement.References);
    }

    /// <summary>The path is bounded so a long exchange does not grow a header without limit, and it gives up its middle.</summary>
    [Fact]
    public void Answering_MessageWithALongPath_KeepsTheRootAndTheRecentEnd()
    {
        // Arrange
        var ancestors = Enumerable
            .Range(0, OutgoingThreadPlacement.MaximumReferences * 2)
            .Select(position => FormattableString.Invariant($"ancestor-{position}@example.test"))
            .ToArray();
        var answered = EmailThreadReferences.Create("parent@example.test", inReplyTo: null, ancestors);

        // Act
        var placement = OutgoingThreadPlacement.Answering(answered);

        // Assert
        Assert.Equal(OutgoingThreadPlacement.MaximumReferences, placement.References.Count);
        Assert.Equal(ancestors[0], placement.References[0]);
        Assert.Equal("parent@example.test", placement.References[^1]);
    }

    /// <summary>An identifier a composed header could not carry is dropped rather than written through or repaired.</summary>
    [Theory]
    [InlineData("no-at-sign")]
    [InlineData("angle<bracket>@example.test")]
    [InlineData("two@at@example.test")]
    [InlineData("comma,separated@example.test")]
    [InlineData("quoted\"value\"@example.test")]
    public void Answering_MessageWhoseIdentityCannotBeWritten_NamesNoParent(string unwritableIdentifier)
    {
        // Arrange
        var answered = EmailThreadReferences.Create(
            unwritableIdentifier,
            inReplyTo: null,
            ["root@example.test"]);

        // Act
        var placement = OutgoingThreadPlacement.Answering(answered);

        // Assert
        Assert.Null(placement.InReplyTo);
        Assert.Equal(["root@example.test"], placement.References);
    }

    /// <summary>The answered identity is written once, so a message that already listed itself does not list it twice.</summary>
    [Fact]
    public void Answering_MessageListingItsOwnIdentity_WritesItOnceAndLast()
    {
        // Arrange
        var answered = EmailThreadReferences.Create(
            "parent@example.test",
            inReplyTo: null,
            ["parent@example.test", "root@example.test"]);

        // Act
        var placement = OutgoingThreadPlacement.Answering(answered);

        // Assert
        Assert.Equal(["root@example.test", "parent@example.test"], placement.References);
    }

    /// <summary>The argument is a required value rather than something to compose an empty placement from.</summary>
    [Fact]
    public void Answering_NoAnsweredReferences_Throws()
    {
        // Act and assert
        Assert.Throws<ArgumentNullException>(() => OutgoingThreadPlacement.Answering(null!));
    }
}
