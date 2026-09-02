// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Contacts;
using MailFathom.Application.Persistence;
using MailFathom.Domain.Access;
using MailFathom.Domain.Contacts;
using MailFathom.Domain.Emails;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace MailFathom.Mcp.UnitTests.TestDoubles;

/// <summary>Composes the two contact use cases over substituted ports, for the tools that call them.</summary>
/// <remarks>
/// What a tool test is about is the conversion in both directions — a caller's arguments into the request a use case is
/// expressed in, and an answer into the published contract — so the ports underneath are substituted and the book's own
/// acts stay covered where the book is. The grant is the whole surface by default, because a tool test is not where the
/// refusal is proved either.
/// </remarks>
internal sealed class StubContactBook
{
    /// <summary>The instant the composed book stamps a write at.</summary>
    internal static readonly DateTimeOffset Now = new(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider timeProvider = new(Now);

    /// <summary>The one caller both use cases and the ownership beside them read.</summary>
    /// <remarks>
    /// One instance rather than one per site, for the reason <c>AuthoredSendGovernors.Governing</c> states: production
    /// composes one scoped authorization that the use case and the ownership both read. Two of them here would let the
    /// ownership answer from a caller the use cases never saw — this one carries no owner, so the resolution falls back
    /// to the deployment's, and a change that made it read the use case's own principal would land on the same answer
    /// and leave every tool suite green.
    /// </remarks>
    private readonly AccessAuthorization authorization = new(
        new StubAuthorizedPrincipalSource(StubAuthorizedPrincipalSource.CallerHolding(
            MailFathomPermission.PublishedFor(ProtectedSurface.Mail))));

    /// <summary>Initializes the ports with the answers a write needs before it can reach an outcome.</summary>
    public StubContactBook()
    {
        this.Directory
            .FindHoldersOfAsync(Arg.Any<MailOwnerId>(), Arg.Any<IReadOnlyCollection<EmailAddress>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<EmailAddress, ContactId>());

        this.Store
            .ReplaceAsync(
                Arg.Any<IPersistenceSession>(),
                Arg.Any<MailOwnerId>(),
                Arg.Any<Contact>(),
                Arg.Any<CancellationToken>())
            .Returns(true);
    }

    /// <summary>Gets the reading port every read is answered from.</summary>
    public IContactDirectory Directory { get; } = Substitute.For<IContactDirectory>();

    /// <summary>Gets the writing port every write is staged against.</summary>
    public IContactStore Store { get; } = Substitute.For<IContactStore>();

    /// <summary>Gets the use case the read tools call.</summary>
    public ContactBookReader Reader =>
        new(this.Directory, ContactBookOwnerships.For(this.authorization), this.authorization);

    /// <summary>Gets the use case the write tools call.</summary>
    public ContactBookWriter Writer => new(
        new ContactBook(
            this.Store,
            this.Directory,
            ContactBookOwnerships.For(this.authorization),
            this.CommitPolicy(),
            this.timeProvider,
            this.authorization),
        this.authorization);

    /// <summary>Builds a contact the book could be holding.</summary>
    /// <param name="displayName">The name to record.</param>
    /// <param name="address">The one address the person uses.</param>
    /// <param name="origin">How the contact came to be in the book.</param>
    /// <returns>The contact.</returns>
    public static Contact ContactOf(
        string displayName,
        string address,
        ContactOrigin origin = ContactOrigin.Asserted) => Contact.Create(
        ContactId.Create(Guid.CreateVersion7(Now)),
        ContactDisplayName.Create(displayName),
        [Address(address)],
        Address(address),
        note: null,
        origin,
        Now,
        Now);

    /// <summary>Reads one address the way every writer of the book reads one.</summary>
    /// <param name="address">The address as written.</param>
    /// <returns>The address value.</returns>
    public static EmailAddress Address(string address)
    {
        if (!EmailAddress.TryCreate(displayName: null, address, out var emailAddress))
        {
            throw new InvalidOperationException($"The test address '{address}' names no mailbox.");
        }

        return emailAddress;
    }

    private OptimisticConcurrencyRetryPolicy CommitPolicy()
    {
        var sessionFactory = Substitute.For<IPersistenceSessionFactory>();
        sessionFactory.BeginSessionAsync(Arg.Any<CancellationToken>()).Returns(_ => new CommittingSession());

        return new OptimisticConcurrencyRetryPolicy(
            sessionFactory,
            new PersistenceConcurrencyOptions(),
            this.timeProvider);
    }

    /// <summary>A session that commits, because nothing here is about a conflict the policy has to retry.</summary>
    private sealed class CommittingSession : IPersistenceSession
    {
        public Task<PersistenceCommitResult> CommitAsync(CancellationToken cancellationToken) =>
            Task.FromResult(PersistenceCommitResult.Committed);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
