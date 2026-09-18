// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Accounts;
using MailFathom.Application.Contacts.Correspondence;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.SensitiveContent.Detection;
using MailFathom.Application.SensitiveContent.Egress;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Contacts;

namespace MailFathom.Application.Contacts.Relationship;

/// <summary>Reads where the correspondence with one person stands, when somebody opens that person.</summary>
/// <remarks>
/// <para>
/// <b>Nothing here runs unless a person opened that contact.</b> There is no sweep over the book, no schedule, and no
/// background pass: one run, scoped to one contact, when a reader asks for it. A product that derived a card for every
/// correspondent nobody looked at would spend an operator's allowance on a book rather than on a page, and would put
/// every contact's mail in front of a provider to do it.
/// </para>
/// <para>
/// It is the correlation and one provider call, in that order. The conversations and the documents are read by
/// <see cref="ContactCorrespondenceReader" /> — under the scope, the bounds, and the scan that read already applies —
/// and that answer is the whole of what the derivation may see. A correspondence the caller may not read is therefore
/// a correspondence this cannot derive from, rather than one it filters afterwards.
/// </para>
/// <para>
/// A person the readable mail says nothing about is answered before any provider is reached. There is nothing to
/// ground a card in, and asking a model anyway would produce a fluent paragraph about a correspondence that does not
/// exist.
/// </para>
/// <para>
/// Both halves of the privacy posture are here. What goes out to a provider was already guarded by the correlation
/// that read it, and what comes back is guarded again before it reaches a client — because a card is composed out of
/// somebody's mail and carries whatever that mail carried.
/// </para>
/// </remarks>
public sealed class ContactRelationshipReader
{
    private readonly ContactCorrespondenceReader correspondenceReader;
    private readonly IContactRelationshipDeriver deriver;
    private readonly MailboxScopeResolver scopeResolver;
    private readonly SensitiveContentEgressGuard egressGuard;
    private readonly AccessAuthorization authorization;
    private readonly IMailAccountLanguages languages;

    /// <summary>Initializes the use case.</summary>
    /// <param name="correspondenceReader">Correlates the contact with the mail this caller may read, which is what the derivation is scoped to.</param>
    /// <param name="deriver">Derives the card, which is the one part of this that reaches a provider.</param>
    /// <param name="scopeResolver">Answers whose mail is being read, and which mailbox's language the card is written in.</param>
    /// <param name="egressGuard">Scans what the card publishes to a client, where this deployment scans anything.</param>
    /// <param name="authorization">Enforces the grant this derivation is behind.</param>
    /// <param name="languages">Answers which language a mailbox is read in, which is the language the card comes out in.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    public ContactRelationshipReader(
        ContactCorrespondenceReader correspondenceReader,
        IContactRelationshipDeriver deriver,
        MailboxScopeResolver scopeResolver,
        SensitiveContentEgressGuard egressGuard,
        AccessAuthorization authorization,
        IMailAccountLanguages languages)
    {
        ArgumentNullException.ThrowIfNull(correspondenceReader);
        ArgumentNullException.ThrowIfNull(deriver);
        ArgumentNullException.ThrowIfNull(scopeResolver);
        ArgumentNullException.ThrowIfNull(egressGuard);
        ArgumentNullException.ThrowIfNull(authorization);
        ArgumentNullException.ThrowIfNull(languages);

        this.correspondenceReader = correspondenceReader;
        this.deriver = deriver;
        this.scopeResolver = scopeResolver;
        this.egressGuard = egressGuard;
        this.authorization = authorization;
        this.languages = languages;
    }

    /// <summary>Derives where the correspondence with one of the acting user's own contacts stands.</summary>
    /// <param name="contact">The contact, as the caller's own book answered it.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>The card, or <see cref="ContactRelationship.Nothing" /> where none was derived.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="contact" /> is <see langword="null" />.</exception>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the use case was reached by anything but a caller granted both <see cref="MailFathomPermission.MailAsk" /> and <see cref="MailFathomPermission.MailRead" />.</exception>
    /// <exception cref="SensitiveContentScannerUnavailableException">Thrown when a switched-on scanner could not establish what the correspondence or the card carries, which withholds it rather than publishing it unscanned.</exception>
    /// <remarks>
    /// The grant asked here is the one that puts mail in front of a chat provider rather than the one that reads it,
    /// because that is what this does. The read grant is asked by the correlation below, which is the use case that
    /// publishes mail, so a caller granted one and not the other is refused naming the grant they are missing.
    /// </remarks>
    public async Task<ContactRelationship> ReadAsync(Contact contact, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(contact);

        this.authorization.RequirePermission(MailFathomPermission.MailAsk);

        using var actingFor = this.egressGuard.ActingFor(this.scopeResolver.User);

        var correspondence = await this.correspondenceReader.ReadAsync(contact, cancellationToken);

        if (correspondence.Threads.Count is 0 && correspondence.Documents.Count is 0)
        {
            return ContactRelationship.Nothing;
        }

        var brief = new ContactRelationshipBrief(correspondence, this.languages.LanguageOf(this.WrittenFor()));
        var card = await this.deriver.DeriveAsync(brief, cancellationToken);

        return card.WasDerived ? await this.GuardedAsync(card, cancellationToken) : card;
    }

    /// <summary>Finds the mailbox the card is written for, which is what decides the language it comes out in.</summary>
    /// <remarks>
    /// A correlation spans every account this caller is assigned, so no one mailbox is the contact's; the first
    /// assigned account is taken, which is deterministic rather than arbitrary because the catalog answers in a fixed
    /// order. A caller assigned none never reaches here — the correlation above answered with nothing — and the unset
    /// identity the expression would then produce is read by the port as an account this deployment does not serve,
    /// which is English.
    /// </remarks>
    private MailAccountId WrittenFor() =>
        this.scopeResolver.AssignedAccounts is [var first, ..] ? first : default;

    /// <summary>Scans everything the card would publish, under the point an opened contact's card is read on.</summary>
    /// <remarks>
    /// One report for the card rather than one per statement, because the card is what a screen waits for. The
    /// citations are not offered: they are identifiers and a position this deployment resolved out of its own store,
    /// and a redacted one would lead a reader nowhere.
    /// </remarks>
    private async Task<ContactRelationship> GuardedAsync(
        ContactRelationship card,
        CancellationToken cancellationToken)
    {
        if (!this.egressGuard.IsActive)
        {
            return card;
        }

        using var scan = this.egressGuard.BeginGuardedOperation(
            SensitiveContentEgressPoint.ClientContactRelationship,
            cancellationToken);

        var note = await this.GuardedStatementAsync(card.Note, cancellationToken);
        var nextAction = await this.GuardedStatementAsync(card.NextAction, cancellationToken);
        var observations = new List<ContactRelationshipObservation>(card.Observations.Count);

        foreach (var observation in card.Observations)
        {
            if (await this.GuardedStatementAsync(observation.Statement, cancellationToken) is { } guarded)
            {
                observations.Add(observation with { Statement = guarded });
            }
        }

        scan.Completed();

        return ContactRelationship.Derived(note, nextAction, observations);
    }

    private async Task<ContactRelationshipStatement?> GuardedStatementAsync(
        ContactRelationshipStatement? statement,
        CancellationToken cancellationToken) =>
        statement is null
            ? null
            : ContactRelationshipStatement.Create(
                await this.egressGuard.GuardAsync(
                    SensitiveContentEgressPoint.ClientContactRelationship,
                    statement.Text,
                    cancellationToken),
                statement.Sources);
}
