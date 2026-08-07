// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Mutations;
using Xunit;

namespace MailFathom.Domain.UnitTests.Mutations;

public sealed class MailboxMutationRequesterTests
{
    /// <summary>The revision is part of the identity, which is what makes an edited rule ask again and an unchanged one ask once.</summary>
    [Fact]
    public void Rule_TwoRevisionsOfOneRule_AreDifferentRequesters()
    {
        // Arrange
        var third = MailboxMutationRequester.Rule("file-newsletters", 3);

        // Act
        var fourth = MailboxMutationRequester.Rule("file-newsletters", 4);

        // Assert
        Assert.NotEqual(third, fourth);
        Assert.Equal(MailboxMutationRequester.Rule("file-newsletters", 3), third);
    }

    /// <summary>Two requesters that differ only in kind are different, so an invocation named like a rule is never the same request.</summary>
    [Fact]
    public void Create_SameIdentityUnderDifferentOrigins_AreDifferentRequesters()
    {
        // Arrange
        var rule = MailboxMutationRequester.Create(MailboxMutationOrigin.Rule, "archive-old");

        // Act
        var command = MailboxMutationRequester.Command("archive-old");

        // Assert
        Assert.NotEqual(rule, command);
    }

    /// <summary>The identity is stored in a bounded column, so a value that would not fit is refused where it is made.</summary>
    [Fact]
    public void Command_IdentityLongerThanTheColumnHolds_IsRefused()
    {
        // Arrange
        var tooLong = new string('a', MailboxMutationRequester.MaximumIdentityLength + 1);

        // Act
        var refusal = Assert.Throws<ArgumentException>(() => MailboxMutationRequester.Command(tooLong));

        // Assert
        Assert.Equal("invocationIdentity", refusal.ParamName);
    }

    /// <summary>A control character would make the record unreadable in the audit query it exists to serve.</summary>
    [Fact]
    public void Command_IdentityCarryingAControlCharacter_IsRefused()
    {
        // Act
        var refusal = Assert.Throws<ArgumentException>(() => MailboxMutationRequester.Command("tool\ncall"));

        // Assert
        Assert.Equal("invocationIdentity", refusal.ParamName);
    }

    [Fact]
    public void Rule_NegativeRevision_IsRefused() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => MailboxMutationRequester.Rule("file-newsletters", -1));

    /// <summary>A record hands back what it stored, so restoring has to produce the requester that was written.</summary>
    [Fact]
    public void Create_FromTheStoredOriginAndIdentity_RestoresTheRequesterThatWasWritten()
    {
        // Arrange
        var original = MailboxMutationRequester.Rule("file-newsletters", 3);

        // Act
        var restored = MailboxMutationRequester.Create(original.Origin, original.Identity);

        // Assert
        Assert.Equal(original, restored);
    }
}
