// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using MailFathom.Application.Contacts;
using MailFathom.Application.Persistence;
using MailFathom.Domain.Access;
using MailFathom.Domain.Contacts;
using MailFathom.Domain.Emails;
using MailFathom.Host.Api;
using MailFathom.TestSupport;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Host.UnitTests.Api;

/// <summary>Covers what the signed-in user's contact routes decide about a request, and what an answer of theirs may carry.</summary>
/// <remarks>
/// <para>
/// The book's own rules, the grant each act asks for, and which books a caller reaches are
/// <c>ContactBookReader</c>'s and <c>ContactBookWriter</c>'s, covered there and not repeated here. What these routes
/// decide is the part above them: which half of the book each listing reads, which request is a request at all, and
/// how an answer is shaped for a client — including where the honest answer is that the person is not there rather
/// than that the request was wrong.
/// </para>
/// <para>
/// Every refusal is asserted for what it does <em>not</em> carry as much as for what it says. A name, an address, and
/// a note are personal data about a third party, and a problem document is the one part of an answer that a proxy log,
/// a trace, and a client's captured error all keep.
/// </para>
/// </remarks>
public sealed class ClientContactEndpointsTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 16, 9, 0, 0, TimeSpan.Zero);

    private readonly IContactStore store = Substitute.For<IContactStore>();
    private readonly IContactDirectory directory = Substitute.For<IContactDirectory>();
    private readonly FakeTimeProvider clock = new(Now);

    /// <summary>
    /// The deployment's half of an agreement with a client it cannot reference. The client composes these paths from
    /// constants of its own, and a rename on either side compiles cleanly while every contact screen reaches a 404.
    /// </summary>
    [Fact]
    public void ContactRoutes_ArePathsAClientComposes()
    {
        // Arrange
        // Act
        // Assert
        Assert.Equal("/contacts", ClientContactEndpoints.ContactsRoute);
        Assert.Equal("/contacts/collected", ClientContactEndpoints.CollectedContactsRoute);
        Assert.Equal("/contacts/{contactId:guid}", ClientContactEndpoints.ContactRoute);
        Assert.Equal("/contacts/{contactId:guid}/promotion", ClientContactEndpoints.ContactPromotionRoute);
    }

    /// <summary>The regression this exists for: a listing that narrowed to nothing would serve the address book and everybody ever written by as one.</summary>
    [Fact]
    public async Task ReadOwnBookAsync_ARequestForTheBook_ReadsThePeopleTheUserWroteDown()
    {
        // Arrange
        this.AnswersWith(new ContactPage([ContactOf("Anna Kowalska", "anna@example.test")], NextCursor: null));

        // Act
        var result = await ClientContactEndpoints.ReadOwnBookAsync(
            pageSize: null,
            cursor: null,
            this.Reader(),
            TestContext.Current.CancellationToken);

        // Assert
        var page = Assert.IsType<Ok<ContactPageResponse>>(result.Result);
        Assert.Equal("Anna Kowalska", Assert.Single(page.Value!.Contacts).DisplayName);
        Assert.Equal(ContactOrigin.Asserted, this.QueryRead().Origin);
    }

    /// <summary>The other half is its own read, because what a mailbox picked up is a different thing to somebody looking at a screen.</summary>
    [Fact]
    public async Task ReadCollectedAsync_ARequestForTheCollectedBook_ReadsWhatTheMailboxesPickedUp()
    {
        // Arrange
        this.AnswersWith(new ContactPage([], NextCursor: null));

        // Act
        var result = await ClientContactEndpoints.ReadCollectedAsync(
            pageSize: null,
            cursor: null,
            this.Reader(),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.IsType<Ok<ContactPageResponse>>(result.Result);
        Assert.Equal(ContactOrigin.Collected, this.QueryRead().Origin);
    }

    /// <summary>A page above the ceiling is refused rather than quietly served the ceiling, so a short page never reads as the end of the book.</summary>
    [Fact]
    public async Task ReadOwnBookAsync_APageSizeAboveTheCeiling_IsRefusedWithoutReading()
    {
        // Arrange
        // Act
        var result = await ClientContactEndpoints.ReadOwnBookAsync(
            ContactQuery.MaximumPageSize + 1,
            cursor: null,
            this.Reader(),
            TestContext.Current.CancellationToken);

        // Assert
        var refusal = Assert.IsType<ProblemHttpResult>(result.Result);
        Assert.Equal(StatusCodes.Status400BadRequest, refusal.StatusCode);
        Assert.Empty(this.directory.ReceivedCalls());
    }

    /// <summary>A cursor this deployment did not issue is refused rather than read as an offset somebody chose.</summary>
    [Fact]
    public async Task ReadOwnBookAsync_ACursorThisDeploymentDidNotIssue_IsRefusedWithoutReading()
    {
        // Arrange
        // Act
        var result = await ClientContactEndpoints.ReadOwnBookAsync(
            pageSize: null,
            "not-a-cursor",
            this.Reader(),
            TestContext.Current.CancellationToken);

        // Assert
        var refusal = Assert.IsType<ProblemHttpResult>(result.Result);
        Assert.Equal(StatusCodes.Status400BadRequest, refusal.StatusCode);
        Assert.Empty(this.directory.ReceivedCalls());
    }

    /// <summary>A client that sent an empty argument asked for the first page rather than presented a boundary nobody issued.</summary>
    [Fact]
    public async Task ReadOwnBookAsync_ABlankCursor_ReadsTheFirstPage()
    {
        // Arrange
        this.AnswersWith(new ContactPage([], NextCursor: null));

        // Act
        var result = await ClientContactEndpoints.ReadOwnBookAsync(
            pageSize: null,
            "   ",
            this.Reader(),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.IsType<Ok<ContactPageResponse>>(result.Result);
        Assert.Null(this.QueryRead().Cursor);
    }

    /// <summary>A person these books do not hold is the absence of the thing addressed, which is what a client draws a missing screen from.</summary>
    [Fact]
    public async Task FindAsync_AContactTheseBooksDoNotHold_AnswersThatThereIsNoSuchPerson()
    {
        // Arrange
        this.directory
            .FindAsync(Arg.Any<ContactBookScope>(), Arg.Any<ContactId>(), Arg.Any<CancellationToken>())
            .Returns((Contact?)null);

        // Act
        var result = await ClientContactEndpoints.FindAsync(
            Guid.CreateVersion7(Now),
            this.Reader(),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.IsType<NotFound>(result.Result);
    }

    /// <summary>The route constraint admits the all-zero identifier no contact can carry, and reading it must not reach the domain guard.</summary>
    [Fact]
    public async Task FindAsync_TheIdentifierNoContactCanCarry_AnswersThatThereIsNoSuchPersonWithoutReading()
    {
        // Arrange
        // Act
        var result = await ClientContactEndpoints.FindAsync(
            Guid.Empty,
            this.Reader(),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.IsType<NotFound>(result.Result);
        Assert.Empty(this.directory.ReceivedCalls());
    }

    /// <summary>What a person types into their own client is somebody they wrote down, which is what makes it amendable afterwards.</summary>
    [Fact]
    public async Task RecordAsync_ARecordTheBookAdmits_WritesItAsAContactTheUserAsserted()
    {
        // Arrange
        this.HoldsNoAddresses();

        // Act
        var result = await ClientContactEndpoints.RecordAsync(
            new ContactRecordRequest("Anna Kowalska", ["anna@example.test"], "anna@example.test", Note: null),
            this.Writer(),
            TestContext.Current.CancellationToken);

        // Assert
        var written = Assert.IsType<Ok<ContactWriteResponse>>(result.Result);
        Assert.Equal(nameof(ContactWriteOutcome.Written), written.Value!.Outcome);
        Assert.Equal(nameof(ContactOrigin.Asserted), written.Value.Contact!.Origin);
    }

    /// <summary>The regression this exists for: a refusal is the one part of an answer a proxy log keeps, so a mistyped address must not travel in it.</summary>
    [Fact]
    public async Task RecordAsync_AnAddressNobodyCanBeReachedAt_IsRefusedWithoutEchoingIt()
    {
        // Arrange
        this.HoldsNoAddresses();

        // Act
        var result = await ClientContactEndpoints.RecordAsync(
            new ContactRecordRequest("Anna Kowalska", ["not-an-address"], "not-an-address", Note: null),
            this.Writer(),
            TestContext.Current.CancellationToken);

        // Assert
        var refusal = Assert.IsType<ProblemHttpResult>(result.Result);
        Assert.Equal(StatusCodes.Status400BadRequest, refusal.StatusCode);
        Assert.DoesNotContain("not-an-address", refusal.ProblemDetails.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain("Anna Kowalska", refusal.ProblemDetails.Detail, StringComparison.Ordinal);
    }

    /// <summary>A name over the bound the book holds is refused with the rule rather than with the name.</summary>
    [Fact]
    public async Task RecordAsync_ANameOverTheBoundTheBookHolds_IsRefusedWithTheRule()
    {
        // Arrange
        this.HoldsNoAddresses();

        // Act
        var result = await ClientContactEndpoints.RecordAsync(
            new ContactRecordRequest(
                new string('A', ContactDisplayName.MaximumLength + 1),
                ["anna@example.test"],
                "anna@example.test",
                Note: null),
            this.Writer(),
            TestContext.Current.CancellationToken);

        // Assert
        var refusal = Assert.IsType<ProblemHttpResult>(result.Result);
        Assert.Equal(StatusCodes.Status400BadRequest, refusal.StatusCode);
        Assert.Contains(
            ContactDisplayName.MaximumLength.ToString(CultureInfo.InvariantCulture),
            refusal.ProblemDetails.Detail,
            StringComparison.Ordinal);
    }

    /// <summary>A write whose body never arrived is a request to repair rather than an empty record to write.</summary>
    [Fact]
    public async Task RecordAsync_ARequestCarryingNoRecord_IsRefusedWithoutWriting()
    {
        // Arrange
        // Act
        var result = await ClientContactEndpoints.RecordAsync(
            request: null,
            this.Writer(),
            TestContext.Current.CancellationToken);

        // Assert
        var refusal = Assert.IsType<ProblemHttpResult>(result.Result);
        Assert.Equal(StatusCodes.Status400BadRequest, refusal.StatusCode);
        Assert.Empty(this.store.ReceivedCalls());
    }

    /// <summary>An amendment names the person it amends, and the one identifier no contact carries is a request to repair.</summary>
    [Fact]
    public async Task AmendAsync_TheIdentifierNoContactCanCarry_IsRefusedWithoutWriting()
    {
        // Arrange
        // Act
        var result = await ClientContactEndpoints.AmendAsync(
            Guid.Empty,
            new ContactRecordRequest("Anna Kowalska", ["anna@example.test"], "anna@example.test", Note: null),
            this.Writer(),
            TestContext.Current.CancellationToken);

        // Assert
        var refusal = Assert.IsType<ProblemHttpResult>(result.Result);
        Assert.Equal(StatusCodes.Status400BadRequest, refusal.StatusCode);
        Assert.Empty(this.store.ReceivedCalls());
    }

    /// <summary>A person these books do not hold is an outcome a screen reports rather than a request that was wrong.</summary>
    [Fact]
    public async Task AmendAsync_AContactTheseBooksDoNotHold_AnswersTheOutcome()
    {
        // Arrange
        this.HoldsNoAddresses();
        this.HoldsNobody();

        // Act
        var result = await ClientContactEndpoints.AmendAsync(
            Guid.CreateVersion7(Now),
            new ContactRecordRequest("Anna Kowalska", ["anna@example.test"], "anna@example.test", Note: null),
            this.Writer(),
            TestContext.Current.CancellationToken);

        // Assert
        var answered = Assert.IsType<Ok<ContactWriteResponse>>(result.Result);
        Assert.Equal(nameof(ContactWriteOutcome.NotFound), answered.Value!.Outcome);
        Assert.Null(answered.Value.Contact);
    }

    /// <summary>A promotion states an identity and nothing else, so its answer must not be the book's own contents under the writing grant.</summary>
    [Fact]
    public async Task PromoteAsync_AContactTheseBooksDoNotHold_AnswersTheOutcomeAndNoRecord()
    {
        // Arrange
        this.HoldsNobody();

        // Act
        var result = await ClientContactEndpoints.PromoteAsync(
            Guid.CreateVersion7(Now),
            this.Writer(),
            TestContext.Current.CancellationToken);

        // Assert
        var answered = Assert.IsType<Ok<ContactWriteResponse>>(result.Result);
        Assert.Equal(nameof(ContactWriteOutcome.NotFound), answered.Value!.Outcome);
        Assert.Null(answered.Value.Contact);
    }

    /// <summary>What an erasure reports about a person is that they are gone and how much went, never the record it removed.</summary>
    [Fact]
    public async Task EraseAsync_AContactTheseBooksDoNotHold_ReportsThatNothingWasHeld()
    {
        // Arrange
        var contactId = Guid.CreateVersion7(Now);
        this.store
            .EraseAsync(
                Arg.Any<IPersistenceSession>(),
                Arg.Any<ContactBookScope>(),
                Arg.Any<ContactId>(),
                Arg.Any<CancellationToken>())
            .Returns(call => new ContactErasure(call.Arg<ContactId>(), WasHeld: false, AddressesErased: 0));

        // Act
        var result = await ClientContactEndpoints.EraseAsync(
            contactId,
            this.Writer(),
            TestContext.Current.CancellationToken);

        // Assert
        var erasure = Assert.IsType<Ok<ContactErasureResponse>>(result.Result);
        Assert.Equal(contactId, erasure.Value!.Contact);
        Assert.False(erasure.Value.WasHeld);
        Assert.Equal(0, erasure.Value.AddressesErased);
    }

    /// <summary>An erasure names the person it erases, and the one identifier no contact carries is a request to repair.</summary>
    [Fact]
    public async Task EraseAsync_TheIdentifierNoContactCanCarry_IsRefusedWithoutErasing()
    {
        // Arrange
        // Act
        var result = await ClientContactEndpoints.EraseAsync(
            Guid.Empty,
            this.Writer(),
            TestContext.Current.CancellationToken);

        // Assert
        var refusal = Assert.IsType<ProblemHttpResult>(result.Result);
        Assert.Equal(StatusCodes.Status400BadRequest, refusal.StatusCode);
        Assert.Empty(this.store.ReceivedCalls());
    }

    private static Contact ContactOf(string displayName, string address)
    {
        var mailbox = Mailbox(address);

        return Contact.Create(
            ContactId.Create(Guid.CreateVersion7(Now)),
            ContactDisplayName.Create(displayName),
            [mailbox],
            mailbox,
            note: null,
            ContactOrigin.Asserted,
            Now,
            Now);
    }

    private static EmailAddress Mailbox(string address)
    {
        Assert.True(EmailAddress.TryCreate(displayName: null, address, out var mailbox));

        return mailbox;
    }

    private ContactBookReader Reader() => new(
        this.directory,
        ContactBookOwnerships.ForTheServedUser(),
        AccessAuthorizations.ForCallerGranted(MailFathomPermission.MailContactsRead));

    private ContactBookWriter Writer()
    {
        var sessionFactory = Substitute.For<IPersistenceSessionFactory>();
        sessionFactory.BeginSessionAsync(Arg.Any<CancellationToken>()).Returns(_ => new CommittingSession());

        var authorization = AccessAuthorizations.ForCallerGranted(MailFathomPermission.MailContactsWrite);

        return new ContactBookWriter(
            new ContactBook(
                this.store,
                this.directory,
                new OptimisticConcurrencyRetryPolicy(sessionFactory, new PersistenceConcurrencyOptions(), this.clock),
                this.clock,
                authorization),
            ContactBookOwnerships.ForTheServedUser(),
            authorization);
    }

    /// <summary>Reads the query the routes composed, which is what each listing's narrowing is asserted against.</summary>
    private ContactQuery QueryRead() =>
        (ContactQuery)this.directory
            .ReceivedCalls()
            .Single(call => call.GetMethodInfo().Name == nameof(IContactDirectory.ReadPageAsync))
            .GetArguments()[1]!;

    private void AnswersWith(ContactPage page) =>
        this.directory
            .ReadPageAsync(Arg.Any<ContactBookScope>(), Arg.Any<ContactQuery>(), Arg.Any<CancellationToken>())
            .Returns(page);

    private void HoldsNoAddresses() =>
        this.directory
            .FindHoldersOfAsync(Arg.Any<ContactBookHolder>(), Arg.Any<IReadOnlyCollection<EmailAddress>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<EmailAddress, ContactId>());

    private void HoldsNobody() =>
        this.directory
            .FindAsync(Arg.Any<ContactBookScope>(), Arg.Any<ContactId>(), Arg.Any<CancellationToken>())
            .Returns((Contact?)null);

    /// <summary>A session that commits whatever was staged in it, which is what a write's ordinary path needs.</summary>
    private sealed class CommittingSession : IPersistenceSession
    {
        public Task<PersistenceCommitResult> CommitAsync(CancellationToken cancellationToken) =>
            Task.FromResult(PersistenceCommitResult.Committed);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
