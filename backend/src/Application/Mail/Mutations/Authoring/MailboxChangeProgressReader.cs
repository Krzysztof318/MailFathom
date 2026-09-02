// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Domain.Access;
using MailFathom.Domain.Mutations;

namespace MailFathom.Application.Mail.Mutations.Authoring;

/// <summary>Answers where the changes one caller authored have got to.</summary>
/// <remarks>
/// <para>
/// It is what makes a change acknowledged before it has reached a mail server usable rather than a promise. The
/// authoring call answers with a record identity and the stage it starts at; this is the read a caller comes back to
/// until the change is done, and it is the only place a pending change against an unreachable account becomes something
/// a person can be shown rather than an absence they infer.
/// </para>
/// <para>
/// It reads the local copy and reaches no mail server, so asking how a change is getting on never costs a connection
/// against the account's own budget and never delays the pass that is carrying the change.
/// </para>
/// <para>
/// A record belonging to another owner is absent rather than refused, and so is one recorded in a folder this caller
/// may no longer read — the same answer a read of that folder's mail gives, reached the same way. What is left is a
/// caller reading about its own work, which is why the grant is the one that already lets it read the mail the work is
/// about.
/// </para>
/// </remarks>
public sealed class MailboxChangeProgressReader
{
    /// <summary>The greatest number of records one read may ask about.</summary>
    /// <remarks>
    /// It is the same bound the submitting routes put on a batch, which is what makes a page of it the same size as a
    /// unit of work a caller already thinks in. Bounding it at all is the rule every query on this surface follows: the
    /// caller supplies the identities, so without a ceiling the size of the answer is the caller's to choose. A
    /// transport may bound it further and one does — a route naming records in a request line rather than in a body is
    /// held to what a request line carries, which is its own to state.
    /// </remarks>
    public const int MaximumRecordsPerRead = 200;

    private readonly AccessAuthorization authorization;
    private readonly MailboxScopeResolver scopeResolver;
    private readonly IMailboxMutationRecordStore records;

    /// <summary>Initializes the use case over the grant it asks first and the records it reads.</summary>
    /// <param name="authorization">Answers which principal reached this use case.</param>
    /// <param name="scopeResolver">Answers whose records these are and which folders the caller may reach.</param>
    /// <param name="records">Reads the durable records.</param>
    /// <exception cref="ArgumentNullException">Thrown when a required collaborator is <see langword="null" />.</exception>
    public MailboxChangeProgressReader(
        AccessAuthorization authorization,
        MailboxScopeResolver scopeResolver,
        IMailboxMutationRecordStore records)
    {
        ArgumentNullException.ThrowIfNull(authorization);
        ArgumentNullException.ThrowIfNull(scopeResolver);
        ArgumentNullException.ThrowIfNull(records);

        this.authorization = authorization;
        this.scopeResolver = scopeResolver;
        this.records = records;
    }

    /// <summary>Reads where each of the named changes stands.</summary>
    /// <param name="recordIds">The records to ask about, as the authoring calls handed them back.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>One entry per record this caller holds, oldest first, and empty where it holds none of them.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="recordIds" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when more records are named than one read may ask about.</exception>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller does not hold the reading grant.</exception>
    /// <remarks>An identity this caller holds no record for is absent from the answer rather than reported as missing, so nothing here says whether somebody else's record exists.</remarks>
    public async Task<IReadOnlyList<MailboxChangeProgress>> ReadAsync(
        IReadOnlyList<MailboxMutationRecordId> recordIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(recordIds);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(recordIds.Count, MaximumRecordsPerRead);

        this.authorization.RequirePermission(MailFathomPermission.MailRead);

        var held = await this.records.ReadAsync(this.scopeResolver.Owner, recordIds, cancellationToken);

        return
        [
            .. held
                .Where(this.IsReadable)
                .Select(MailboxChangeProgress.Of),
        ];
    }

    /// <summary>Reports whether the caller may still reach the mailbox the change was recorded in.</summary>
    /// <remarks>
    /// Read from the occurrence's own folder binding rather than from where the change was going, because the question
    /// is which mail the caller may be told about and the answer is the folder the message was in when it was asked
    /// about.
    /// </remarks>
    private bool IsReadable(MailboxMutationRecord record) => this.scopeResolver.IsReadableByTools(
        record.Request.Occurrence.AccountId,
        record.Request.Occurrence.FolderResolutionId.Alias);
}
