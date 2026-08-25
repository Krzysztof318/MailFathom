// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using MailFathom.Application.Access;
using MailFathom.Application.Mail.Delivery.Composition;
using MailFathom.Application.Mail.Delivery.Governance;
using MailFathom.Application.UnitTests.TestDoubles;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Contacts;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Emails;
using MailFathom.TestSupport;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Mail.Delivery.Governance;

public sealed class RecipientVouchingTests
{
    private static readonly MailAccountId Account = MailAccountId.Create("work");

    private static readonly DateTimeOffset Recorded =
        DateTimeOffset.Parse("2026-08-19T09:00:00Z", CultureInfo.InvariantCulture);

    /// <summary>An address the mailbox has no trace of is the shape an injected instruction takes, and it is counted.</summary>
    [Fact]
    public async Task CountUnvouchedAsync_AddressNobodyHolds_IsCounted()
    {
        // Arrange
        var vouching = Vouching(new InMemoryContactBookStore());

        // Act
        var unvouched = await vouching.CountUnvouchedAsync(
            [NamedByCaller("accomplice@elsewhere.test")],
            CancellationToken.None);

        // Assert
        Assert.Equal(1, unvouched);
    }

    /// <summary>Somebody the book holds is vouched for, whether the owner wrote them down or collection recorded them.</summary>
    [Fact]
    public async Task CountUnvouchedAsync_AddressTheBookHolds_IsVouchedFor()
    {
        // Arrange
        var book = new InMemoryContactBookStore();
        book.Hold(ContactOf("Anna", "anna@example.test"));
        var vouching = Vouching(book);

        // Act
        var unvouched = await vouching.CountUnvouchedAsync(
            [NamedByCaller("anna@example.test")],
            CancellationToken.None);

        // Assert
        Assert.Equal(0, unvouched);
    }

    /// <summary>A mailbox this deployment sends as is the owner's own, so writing to it is never an injected recipient.</summary>
    [Fact]
    public async Task CountUnvouchedAsync_AddressThisDeploymentSendsAs_IsVouchedFor()
    {
        // Arrange
        var vouching = Vouching(new InMemoryContactBookStore(), ownAddress: "owner@example.test");

        // Act
        var unvouched = await vouching.CountUnvouchedAsync(
            [NamedByCaller("owner@example.test")],
            CancellationToken.None);

        // Assert
        Assert.Equal(0, unvouched);
    }

    /// <summary>
    /// A mailbox another owner sends as vouches for nothing here, because what vouches for an address is the caller's
    /// own accounts rather than every account this deployment happens to serve.
    /// </summary>
    [Fact]
    public async Task CountUnvouchedAsync_AddressAnotherOwnersAccountSendsAs_IsCounted()
    {
        // Arrange
        var vouching = Vouching(
            new InMemoryContactBookStore(),
            ownAddress: "owner@example.test",
            AccessAuthorizations.ForOwnerGranted(SyntheticMailOwner.Another, MailFathomPermission.MailSend));

        // Act
        var unvouched = await vouching.CountUnvouchedAsync(
            [NamedByCaller("owner@example.test")],
            CancellationToken.None);

        // Assert
        Assert.Equal(1, unvouched);
    }

    /// <summary>An address this system derived itself is not the caller's word, so a plain reply is never judged by this.</summary>
    [Theory]
    [InlineData(AuthoredRecipientProvenance.DerivedFromAnsweredEmail)]
    [InlineData(AuthoredRecipientProvenance.ResolvedFromContactBook)]
    public async Task CountUnvouchedAsync_RecipientThisSystemDerived_IsNotJudged(
        AuthoredRecipientProvenance provenance)
    {
        // Arrange
        var vouching = Vouching(new InMemoryContactBookStore());

        // Act
        var unvouched = await vouching.CountUnvouchedAsync(
            [new AuthoredEmailRecipient(
                OutgoingRecipientRole.To,
                "stranger@elsewhere.test",
                DisplayName: null,
                Contact: null,
                provenance)],
            CancellationToken.None);

        // Assert
        Assert.Equal(0, unvouched);
    }

    /// <summary>Text naming no mailbox is the composition's to refuse, so it is not counted as somebody unvouched for.</summary>
    [Fact]
    public async Task CountUnvouchedAsync_TextNamingNoMailbox_IsLeftToTheComposition()
    {
        // Arrange
        var vouching = Vouching(new InMemoryContactBookStore());

        // Act
        var unvouched = await vouching.CountUnvouchedAsync(
            [NamedByCaller("not an address")],
            CancellationToken.None);

        // Assert
        Assert.Equal(0, unvouched);
    }

    /// <summary>The book is read once for the whole message rather than once per person it is addressed to.</summary>
    [Fact]
    public async Task CountUnvouchedAsync_SeveralRecipients_ReadsTheBookInOneLookup()
    {
        // Arrange
        var book = new InMemoryContactBookStore();
        var vouching = Vouching(book);
        var recipients = Enumerable
            .Range(0, 8)
            .Select(position => NamedByCaller(
                string.Create(CultureInfo.InvariantCulture, $"person{position}@elsewhere.test")))
            .ToArray();

        // Act
        var unvouched = await vouching.CountUnvouchedAsync(recipients, CancellationToken.None);

        // Assert
        Assert.Equal(8, unvouched);
        Assert.Equal(1, book.BatchedLookupCount);
    }

    /// <summary>Another owner's book vouches for nobody here, because the vouching reads the book the send is authored for.</summary>
    /// <remarks>
    /// This is the control that keeps every other assertion in the class honest. All of them arrange one book and one
    /// caller under the same owner, so a scope lost anywhere between here and the directory would let one owner's
    /// correspondents vouch for a send authored for another — and the refusal an operator switched on would stop
    /// refusing without a single test failing.
    /// </remarks>
    [Fact]
    public async Task CountUnvouchedAsync_AddressOnlyAnotherOwnersBookHolds_IsCounted()
    {
        // Arrange
        var book = new InMemoryContactBookStore();
        book.Hold(SyntheticMailOwner.Deployment, ContactOf("Anna", "anna@example.test"));

        var vouching = Vouching(
            book,
            authorization: AccessAuthorizations.ForOwnerGranted(
                SyntheticMailOwner.Another,
                MailFathomPermission.MailSend));

        // Act
        var unvouched = await vouching.CountUnvouchedAsync(
            [NamedByCaller("anna@example.test")],
            CancellationToken.None);

        // Assert
        Assert.Equal(1, unvouched);
    }

    private static RecipientVouching Vouching(
        InMemoryContactBookStore book,
        string? ownAddress = null,
        AccessAuthorization? authorization = null)
    {
        // One authorization, because the catalog the vouching reads its own addresses from and the book it reads
        // correspondents from have to answer for the same owner. Resolving the default twice would let the two axes
        // drift apart, and an empty book vouches for nobody — so a suite arranging a refusing posture would change
        // verdict with nothing able to say why.
        var caller = authorization ?? AccessAuthorizations.ForCallerGranted(MailFathomPermission.MailSend);
        var accounts = OwnedMailAccountCatalogs.For(caller, SyntheticServedAccount.Of(Account));

        var senderIdentities = Substitute.For<IOutgoingSenderIdentityReader>();
        senderIdentities.FindSenderIdentity(Account).Returns(
            ownAddress is null ? null : OutgoingSenderIdentity.Create(Account, Address(ownAddress)));

        return new RecipientVouching(book, ContactBookOwnerships.For(caller), accounts, senderIdentities);
    }

    private static AuthoredEmailRecipient NamedByCaller(string address) =>
        new(OutgoingRecipientRole.To, address);

    private static Contact ContactOf(string displayName, params string[] addresses) => Contact.Create(
        ContactId.Create(Guid.CreateVersion7(Recorded)),
        ContactDisplayName.Create(displayName),
        [.. addresses.Select(Address)],
        Address(addresses[0]),
        note: null,
        ContactOrigin.Collected,
        Recorded,
        Recorded);

    private static EmailAddress Address(string address)
    {
        if (!EmailAddress.TryCreate(displayName: null, address, out var emailAddress))
        {
            throw new InvalidOperationException($"The test address '{address}' names no mailbox.");
        }

        return emailAddress;
    }
}
