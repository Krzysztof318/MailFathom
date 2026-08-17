// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Contacts;
using MailFathom.Application.Persistence;
using MailFathom.Domain.Contacts;
using MailFathom.Domain.Emails;
using MailFathom.Host.Api;
using MailFathom.Host.UnitTests.TestDoubles;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Host.UnitTests.Api;

/// <summary>Covers what the contact routes admit, what they refuse, and what a refusal is allowed to say.</summary>
/// <remarks>
/// <para>
/// The book's own rules are covered against <c>ContactBook</c> and the domain, and are not repeated here. What these
/// routes decide is the part above it: which request is a request at all, which origin a write from this surface acts
/// under, and how an answer is shaped for a caller — including the two cases where the honest answer is that the book
/// holds nobody rather than that the resource is missing.
/// </para>
/// <para>
/// Every refusal is asserted for what it does <em>not</em> carry as much as for what it says. A name, an address, and a
/// note are personal data about a third party, and a problem document is the one part of an answer that a proxy log, a
/// trace, and a scripted caller's captured error all keep.
/// </para>
/// </remarks>
public sealed class ContactEndpointsTests
{
    private static readonly Guid Identity = new("11111111-2222-3333-4444-555555555555");

    private readonly IContactStore store = Substitute.For<IContactStore>();
    private readonly IContactDirectory directory = Substitute.For<IContactDirectory>();
    private readonly FakeTimeProvider clock = new(new DateTimeOffset(2026, 8, 16, 9, 0, 0, TimeSpan.Zero));

    /// <summary>
    /// The deployment's half of an agreement with a command it cannot reference. <c>mfctl</c> composes these paths from
    /// constants of its own, and a rename on either side compiles cleanly while every contact command reaches a 404 that
    /// reads exactly like an endpoint nobody enabled.
    /// </summary>
    [Fact]
    public void ContactRoutes_AreThePathsTheCommandComposes()
    {
        Assert.Equal("/contacts", ContactEndpoints.ContactsRoute);
        Assert.Equal("/contacts/by-address", ContactEndpoints.ContactByAddressRoute);
        Assert.Equal("/contacts/{contactId:guid}", ContactEndpoints.ContactRoute);
        Assert.Equal("/contacts/{contactId:guid}/promotion", ContactEndpoints.ContactPromotionRoute);
        Assert.Equal("/contacts/{contactId:guid}/export", ContactEndpoints.ContactExportRoute);
    }

    /// <summary>A write from this surface is the owner writing somebody down, which is what makes it amendable here.</summary>
    [Fact]
    public async Task RecordAsync_ARecordTheBookAdmits_WritesItAsAContactTheOwnerAsserted()
    {
        // Arrange
        this.HoldsNoAddresses();

        // Act
        var result = await ContactEndpoints.RecordAsync(
            new ContactRecordRequest("Anna Kowalska", ["anna@example.test"], "anna@example.test", Note: null),
            this.Book(),
            TestContext.Current.CancellationToken);

        // Assert
        var written = Assert.IsType<Ok<ContactWriteResponse>>(result.Result);
        Assert.Equal(nameof(ContactWriteOutcome.Written), written.Value!.Outcome);
        Assert.Equal(nameof(ContactOrigin.Asserted), written.Value.Contact!.Origin);

        await this.store.Received(1).AddAsync(
            Arg.Any<IPersistenceSession>(),
            Arg.Is<Contact>(contact => contact != null && contact.Origin == ContactOrigin.Asserted),
            Arg.Any<CancellationToken>());
    }

    /// <summary>An amendment from this surface asks under the same origin, so a collected record is refused rather than taken.</summary>
    [Fact]
    public async Task AmendAsync_AContactTheDeploymentCollected_IsRefusedByItsOriginRatherThanWritten()
    {
        // Arrange
        this.Holds(Collected("Anna Kowalska", "anna@example.test"));

        // Act
        var result = await ContactEndpoints.AmendAsync(
            Identity,
            new ContactRecordRequest("Anna Nowak", ["anna@example.test"], "anna@example.test", Note: null),
            this.Book(),
            TestContext.Current.CancellationToken);

        // Assert
        var amended = Assert.IsType<Ok<ContactWriteResponse>>(result.Result);
        Assert.Equal(nameof(ContactWriteOutcome.OriginRefusesWriter), amended.Value!.Outcome);

        await this.store.DidNotReceive().ReplaceAsync(
            Arg.Any<IPersistenceSession>(),
            Arg.Any<Contact>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>Each rule a record can break is named, and none of the answers repeats what broke it.</summary>
    /// <remarks>
    /// The cases are written as the wire's own values rather than as request objects, because the request type is
    /// internal to the composition root and a public theory signature cannot carry it. Lengths are named as multiples of
    /// the bounds the domain publishes, so a bound that moves moves these with it.
    /// </remarks>
    [Theory]
    [InlineData("", "anna@example.test", "anna@example.test", null, "names no display name")]
    [InlineData("Anna Kowalska", "", "", null, "at least one address")]
    [InlineData("Anna Kowalska", "not an address", "not an address", null, "not a usable address")]
    [InlineData("Anna Kowalska", "anna@example.test", "other@example.test", null, "one of the addresses the contact holds")]
    [InlineData("Anna Kowalska", "anna@example.test", "", null, "names no usable preferred address")]
    [InlineData("Anna\u200BKowalska", "anna@example.test", "anna@example.test", null, "carry no glyph of their own")]
    public async Task RecordAsync_ARecordBreakingOneOfTheBooksRules_RefusesWithoutEchoingThePerson(
        string displayName,
        string address,
        string preferredAddress,
        string? note,
        string expectedFragment)
    {
        // Arrange
        this.HoldsNoAddresses();

        // Act
        var result = await ContactEndpoints.RecordAsync(
            new ContactRecordRequest(
                displayName,
                address.Length == 0 ? [] : [address],
                preferredAddress,
                note),
            this.Book(),
            TestContext.Current.CancellationToken);

        // Assert
        await this.AssertRefusedWithoutWriting(result, expectedFragment);
    }

    /// <summary>The two bounds a record can exceed, named by the constants the domain publishes rather than by a literal.</summary>
    [Fact]
    public async Task RecordAsync_ANameOrANoteBeyondItsBound_RefusesNamingTheBound()
    {
        // Arrange
        this.HoldsNoAddresses();

        // Act
        var longName = await ContactEndpoints.RecordAsync(
            new ContactRecordRequest(
                new string('A', ContactDisplayName.MaximumLength + 1),
                ["anna@example.test"],
                "anna@example.test",
                Note: null),
            this.Book(),
            TestContext.Current.CancellationToken);

        var longNote = await ContactEndpoints.RecordAsync(
            new ContactRecordRequest(
                "Anna Kowalska",
                ["anna@example.test"],
                "anna@example.test",
                new string('n', ContactNote.MaximumLength + 1)),
            this.Book(),
            TestContext.Current.CancellationToken);

        // Assert
        await this.AssertRefusedWithoutWriting(longName, "A contact name cannot be longer than");
        await this.AssertRefusedWithoutWriting(longNote, "A contact note cannot be longer than");
    }

    /// <summary>A body the caller sent nothing in is refused rather than read as a record of blanks.</summary>
    [Fact]
    public async Task RecordAsync_ARequestCarryingNoRecord_RefusesWithoutWriting()
    {
        // Act
        var result = await ContactEndpoints.RecordAsync(
            request: null,
            this.Book(),
            TestContext.Current.CancellationToken);

        // Assert
        await this.AssertRefusedWithoutWriting(result, "carries no contact record");
    }

    /// <summary>More addresses than one person may be recorded as using is refused before the domain sees them.</summary>
    [Fact]
    public async Task RecordAsync_MoreAddressesThanOneContactMayHold_RefusesNamingTheCeiling()
    {
        // Arrange
        this.HoldsNoAddresses();

        var addresses = Enumerable
            .Range(0, Contact.MaximumAddressCount + 1)
            .Select(position => $"person{position}@example.test")
            .ToArray();

        // Act
        var result = await ContactEndpoints.RecordAsync(
            new ContactRecordRequest("Anna Kowalska", addresses, addresses[0], Note: null),
            this.Book(),
            TestContext.Current.CancellationToken);

        // Assert
        var refusal = Assert.IsType<ProblemHttpResult>(result.Result);
        Assert.Contains(
            Contact.MaximumAddressCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
            refusal.ProblemDetails.Detail,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// A book holding nobody is an outcome rather than a missing resource. Answering <c>404</c> would collide with what
    /// every client already reads that as here — a port serving no administrative endpoint at all.
    /// </summary>
    [Fact]
    public async Task FindAsync_AContactTheBookDoesNotHold_AnswersWithNoContactRatherThanNotFound()
    {
        // Arrange
        this.directory.FindAsync(Arg.Any<ContactId>(), Arg.Any<CancellationToken>()).Returns((Contact?)null);

        // Act
        var result = await ContactEndpoints.FindAsync(
            Identity,
            this.Book(),
            TestContext.Current.CancellationToken);

        // Assert
        var lookup = Assert.IsType<Ok<ContactLookupResponse>>(result.Result);
        Assert.Null(lookup.Value!.Contact);
    }

    /// <summary>The one identifier a UUID route constraint still admits is refused rather than reaching a domain guard.</summary>
    [Fact]
    public async Task FindAsync_TheEmptyIdentifier_IsRefusedRatherThanReportedAsAFault()
    {
        // Act
        var result = await ContactEndpoints.FindAsync(
            Guid.Empty,
            this.Book(),
            TestContext.Current.CancellationToken);

        // Assert
        var refusal = Assert.IsType<ProblemHttpResult>(result.Result);
        Assert.Equal(StatusCodes.Status400BadRequest, refusal.StatusCode);

        await this.directory.DidNotReceive().FindAsync(Arg.Any<ContactId>(), Arg.Any<CancellationToken>());
    }

    /// <summary>An address that is not one is refused without being written into the answer.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not an address")]
    public async Task FindByAddressAsync_AValueThatIsNotAnAddress_RefusesWithoutRepeatingIt(string? address)
    {
        // Act
        var result = await ContactEndpoints.FindByAddressAsync(
            address,
            this.Book(),
            TestContext.Current.CancellationToken);

        // Assert
        var refusal = Assert.IsType<ProblemHttpResult>(result.Result);
        Assert.Equal(StatusCodes.Status400BadRequest, refusal.StatusCode);
        Assert.DoesNotContain("not an address", refusal.ProblemDetails.Detail!, StringComparison.Ordinal);
    }

    /// <summary>The listing is served the page the book read, with the cursor written as the opaque string a caller presents.</summary>
    [Fact]
    public async Task ListAsync_APageTheBookContinuesAfter_AnswersWithTheEncodedCursor()
    {
        // Arrange
        var held = Asserted("Anna Kowalska", "anna@example.test");
        var cursor = ContactCursor.After(held.DisplayName, held.Id);
        this.directory.ReadPageAsync(Arg.Any<ContactQuery>(), Arg.Any<CancellationToken>())
            .Returns(new ContactPage([held], cursor));

        // Act
        var result = await ContactEndpoints.ListAsync(
            origin: null,
            pageSize: null,
            cursor: null,
            this.Book(),
            TestContext.Current.CancellationToken);

        // Assert
        var page = Assert.IsType<Ok<ContactPageResponse>>(result.Result);
        Assert.Equal(cursor.Encode(), page.Value!.NextCursor);
        Assert.Equal("Anna Kowalska", Assert.Single(page.Value.Contacts).DisplayName);
    }

    /// <summary>The narrowing is read as the origin it names, and the query the book reads under carries it.</summary>
    [Fact]
    public async Task ListAsync_AnOriginNamedInAnyCasing_NarrowsTheQueryToIt()
    {
        // Arrange
        this.directory.ReadPageAsync(Arg.Any<ContactQuery>(), Arg.Any<CancellationToken>())
            .Returns(new ContactPage([], NextCursor: null));

        // Act
        await ContactEndpoints.ListAsync(
            "collected",
            pageSize: null,
            cursor: null,
            this.Book(),
            TestContext.Current.CancellationToken);

        // Assert
        await this.directory.Received(1).ReadPageAsync(
            Arg.Is<ContactQuery>(query => query != null && query.Origin == ContactOrigin.Collected),
            Arg.Any<CancellationToken>());
    }

    /// <summary>Each thing a listing can get wrong is refused rather than resolved into a page the caller did not ask for.</summary>
    [Theory]
    [InlineData("neither", null, null, "Asserted")]
    [InlineData(null, 0, null, "between 1 and")]
    [InlineData(null, 100_000, null, "between 1 and")]
    [InlineData(null, null, "not-a-cursor-this-issued", "not one this deployment issued")]
    public async Task ListAsync_ARequestTheBookCannotRead_RefusesNamingWhatToChange(
        string? origin,
        int? pageSize,
        string? cursor,
        string expectedFragment)
    {
        // Act
        var result = await ContactEndpoints.ListAsync(
            origin,
            pageSize,
            cursor,
            this.Book(),
            TestContext.Current.CancellationToken);

        // Assert
        var refusal = Assert.IsType<ProblemHttpResult>(result.Result);
        Assert.Equal(StatusCodes.Status400BadRequest, refusal.StatusCode);
        Assert.Contains(expectedFragment, refusal.ProblemDetails.Detail, StringComparison.Ordinal);

        await this.directory.DidNotReceive().ReadPageAsync(Arg.Any<ContactQuery>(), Arg.Any<CancellationToken>());
    }

    /// <summary>An erasure says what it removed, and says nothing about who it was.</summary>
    [Fact]
    public async Task EraseAsync_AContactTheBookHolds_AnswersWithTheCountsAndNothingAboutThePerson()
    {
        // Arrange
        this.store.EraseAsync(Arg.Any<IPersistenceSession>(), Arg.Any<ContactId>(), Arg.Any<CancellationToken>())
            .Returns(new ContactErasure(ContactId.Create(Identity), WasHeld: true, AddressesErased: 3));

        // Act
        var result = await ContactEndpoints.EraseAsync(
            Identity,
            this.Book(),
            TestContext.Current.CancellationToken);

        // Assert
        var erasure = Assert.IsType<Ok<ContactErasureResponse>>(result.Result);
        Assert.Equal((Identity, true, 3), (
            erasure.Value!.Contact,
            erasure.Value.WasHeld,
            erasure.Value.AddressesErased));
    }

    /// <summary>Exporting somebody the book does not hold produces no document rather than an empty one.</summary>
    [Fact]
    public async Task ExportAsync_AContactTheBookDoesNotHold_AnswersWithNeitherRecordNorInstant()
    {
        // Arrange
        this.directory.FindAsync(Arg.Any<ContactId>(), Arg.Any<CancellationToken>()).Returns((Contact?)null);

        // Act
        var result = await ContactEndpoints.ExportAsync(
            Identity,
            this.Book(),
            TestContext.Current.CancellationToken);

        // Assert
        var export = Assert.IsType<Ok<ContactExportResponse>>(result.Result);
        Assert.Null(export.Value!.Contact);
        Assert.Null(export.Value.ProducedAt);
    }

    /// <summary>The export carries the whole record and the instant it was taken, which is what dates the answer.</summary>
    [Fact]
    public async Task ExportAsync_AContactTheBookHolds_AnswersWithTheCompleteRecordAndWhenItWasTaken()
    {
        // Arrange
        this.Holds(Asserted("Anna Kowalska", "anna@example.test", "Met at the conference."));

        // Act
        var result = await ContactEndpoints.ExportAsync(
            Identity,
            this.Book(),
            TestContext.Current.CancellationToken);

        // Assert
        var export = Assert.IsType<Ok<ContactExportResponse>>(result.Result);
        Assert.Equal("Anna Kowalska", export.Value!.Contact!.DisplayName);
        Assert.Equal("Met at the conference.", export.Value.Contact.Note);
        Assert.Equal(this.clock.GetUtcNow(), export.Value.ProducedAt);
    }

    private static Contact Asserted(string displayName, string address, string? note = null) =>
        Build(displayName, address, ContactOrigin.Asserted, note);

    private static Contact Collected(string displayName, string address) =>
        Build(displayName, address, ContactOrigin.Collected, note: null);

    private static Contact Build(string displayName, string address, ContactOrigin origin, string? note)
    {
        Assert.True(EmailAddress.TryCreate(displayName: null, address, out var mailbox));

        return Contact.Create(
            ContactId.Create(Identity),
            ContactDisplayName.Create(displayName),
            [mailbox],
            mailbox,
            note is null ? null : ContactNote.Create(note),
            origin,
            new DateTimeOffset(2026, 8, 1, 10, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 8, 2, 11, 0, 0, TimeSpan.Zero));
    }

    /// <summary>Asserts that a write was refused for the rule named, wrote nothing, and echoed nobody.</summary>
    /// <remarks>
    /// The two absences are the point as much as the sentence. A problem document is the part of an answer a proxy log,
    /// a trace, and a scripted caller's captured error all keep, so a refusal naming the person it refused would put a
    /// contact's own data everywhere a request went.
    /// </remarks>
    private async Task AssertRefusedWithoutWriting(
        Results<Ok<ContactWriteResponse>, ProblemHttpResult> result,
        string expectedFragment)
    {
        var refusal = Assert.IsType<ProblemHttpResult>(result.Result);

        Assert.Equal(StatusCodes.Status400BadRequest, refusal.StatusCode);
        Assert.Contains(expectedFragment, refusal.ProblemDetails.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain("Anna", refusal.ProblemDetails.Detail!, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("example.test", refusal.ProblemDetails.Detail!, StringComparison.OrdinalIgnoreCase);

        await this.store.DidNotReceive().AddAsync(
            Arg.Any<IPersistenceSession>(),
            Arg.Any<Contact>(),
            Arg.Any<CancellationToken>());
    }

    private void Holds(Contact contact)
    {
        this.directory.FindAsync(Arg.Any<ContactId>(), Arg.Any<CancellationToken>()).Returns(contact);
        this.HoldsNoAddresses();
    }

    /// <summary>States that no other contact claims the addresses a write is about, which is the ordinary case.</summary>
    private void HoldsNoAddresses() =>
        this.directory
            .FindHoldersOfAsync(Arg.Any<IReadOnlyCollection<EmailAddress>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<EmailAddress, ContactId>());

    /// <summary>Builds the book the handlers write through, over substituted ports and a session that commits.</summary>
    private ContactBook Book()
    {
        var sessionFactory = Substitute.For<IPersistenceSessionFactory>();
        sessionFactory.BeginSessionAsync(Arg.Any<CancellationToken>()).Returns(_ => new CommittingSession());

        return new ContactBook(
            this.store,
            this.directory,
            new OptimisticConcurrencyRetryPolicy(sessionFactory, new PersistenceConcurrencyOptions(), this.clock),
            this.clock,
            AdministrativeGrant.WholeSurface);
    }

    /// <summary>A session that commits whatever was staged in it, which is what a write's ordinary path needs.</summary>
    private sealed class CommittingSession : IPersistenceSession
    {
        public Task<PersistenceCommitResult> CommitAsync(CancellationToken cancellationToken) =>
            Task.FromResult(PersistenceCommitResult.Committed);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
