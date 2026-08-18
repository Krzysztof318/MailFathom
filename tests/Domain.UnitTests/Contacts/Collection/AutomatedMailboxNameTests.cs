// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Contacts.Collection;
using MailFathom.Domain.Emails;
using Xunit;

namespace MailFathom.Domain.UnitTests.Contacts.Collection;

/// <summary>Covers the structural half of collection's bounds: what nobody corresponds with, whoever the owner is.</summary>
public sealed class AutomatedMailboxNameTests
{
    /// <summary>These are the names RFC 2142 publishes for a function rather than a person, plus the transport's own.</summary>
    [Theory]
    [InlineData("postmaster@example.test")]
    [InlineData("abuse@example.test")]
    [InlineData("MAILER-DAEMON@example.test")]
    [InlineData("support@example.test")]
    [InlineData("Webmaster@Example.test")]
    public void Names_ARoleMailbox_IsRefused(string address) => Assert.True(AutomatedMailboxName.Names(AddressOf(address)));

    /// <summary>An address that says in its own name that a reply reaches nobody is stating that nobody corresponds with it.</summary>
    [Theory]
    [InlineData("noreply@example.test")]
    [InlineData("no-reply@example.test")]
    [InlineData("do-not-reply@example.test")]
    [InlineData("noreply-billing@example.test")]
    [InlineData("NoReply@Example.test")]
    public void Names_ANoReplyMailbox_IsRefused(string address) => Assert.True(AutomatedMailboxName.Names(AddressOf(address)));

    /// <summary>The suffixes reach a list's machinery rather than its readers, which is RFC 2142 § 5's own convention.</summary>
    [Theory]
    [InlineData("developers-request@lists.example.test")]
    [InlineData("developers-bounces@lists.example.test")]
    [InlineData("developers-owner@lists.example.test")]
    [InlineData("developers-unsubscribe@lists.example.test")]
    public void Names_AListAdministrationMailbox_IsRefused(string address) =>
        Assert.True(AutomatedMailboxName.Names(AddressOf(address)));

    /// <summary>The rule has to leave ordinary people alone, including ones whose names contain a refused word.</summary>
    [Theory]
    [InlineData("anna@example.test")]
    [InlineData("anna.kowalska@example.test")]
    [InlineData("newsletter.editor@example.test")]
    [InlineData("support.anna@example.test")]
    [InlineData("requests@example.test")]
    public void Names_APerson_IsAdmitted(string address) => Assert.False(AutomatedMailboxName.Names(AddressOf(address)));

    private static EmailAddress AddressOf(string value)
    {
        Assert.True(EmailAddress.TryCreate(displayName: null, value, out var address));

        return address;
    }
}
