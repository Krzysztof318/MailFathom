// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.EmailContent.Storage;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Access;
using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace MailFathom.Infrastructure.Persistence.Emails;

/// <summary>EF Core counter of what each owner's stored mail content holds, and the recomputation behind it.</summary>
/// <remarks>
/// <para>
/// Every movement is one composed statement rather than a tracked entity, for the reason the spend ledger's is: a
/// read-modify-write would let two runs storing at once overwrite each other's movement with a total that was already
/// stale when it was read. The identifiers come from the model, so the statements and the mapping cannot drift apart,
/// and every value in them is a parameter.
/// </para>
/// <para>
/// Each statement resolves the owner from an account rather than taking one from a caller. That is what keeps whose
/// mail a payload is a fact of the mail graph instead of something a caller could pass wrongly, and it costs no round
/// trip: the resolution runs inside the statement that was going to be issued anyway.
/// </para>
/// <para>
/// The recomputation is the one thing here that is not a movement, and it is therefore the one thing that needs more
/// than a statement. Writing a total is not something PostgreSQL can make safe the way it makes an increment safe, so
/// the recomputation claims the owner's row before it measures and holds it until it has written — which is why it is
/// a repair rather than something any run reaches for.
/// </para>
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class OwnerStoredContentLedger(MailFathomDbContext dbContext) : IOwnerStoredContentLedger
{
    /// <inheritdoc />
    public async Task<long> ReadStoredContentBytesAsync(MailOwnerId owner, CancellationToken cancellationToken)
    {
        var ownerId = RequireNamedOwner(owner);

        var storedBytes = await dbContext.OwnerStoredContent
            .AsNoTracking()
            .Where(total => total.OwnerId == ownerId)
            .Select(total => (long?)total.StoredContentByteCount)
            .SingleOrDefaultAsync(cancellationToken);

        // An owner with no counter row has never had one written — a deployment upgraded before their first message, or
        // an owner provisioned since. Deriving once and adopting it is what keeps the ceiling from admitting a mailbox
        // twice over on the strength of a figure that was only ever zero because nobody had written it.
        return storedBytes ?? await this.RederiveStoredContentBytesAsync(owner, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<long> RederiveStoredContentBytesAsync(MailOwnerId owner, CancellationToken cancellationToken)
    {
        var ownerId = RequireNamedOwner(owner);

        // The one operation here that cannot be a single statement. Every movement adds a difference, so PostgreSQL
        // re-reads the row it is adding to after waiting for whoever held it; this one writes a total instead, and the
        // sum it writes was taken at its own statement's snapshot. A store that committed between that sum and this
        // write would have its bytes overwritten rather than counted — which is precisely the undercount the ceiling
        // above this would then never notice. So the row is claimed first, which creates it when it is absent and locks
        // it either way, and the sum is taken by a later statement: every movement that had committed is in it, and
        // every movement that had not waits for this transaction and lands on top of the total it wrote.
        await using var ownTransaction = dbContext.Database.CurrentTransaction is null
            ? await dbContext.Database.BeginTransactionAsync(cancellationToken)
            : null;

        await dbContext.Database.ExecuteSqlRawAsync(
            ClaimStatement(dbContext.Model),
            [ownerId],
            cancellationToken);

        await dbContext.Database.ExecuteSqlRawAsync(
            RederiveStatement(dbContext.Model),
            [ownerId],
            cancellationToken);

        var storedBytes = await dbContext.OwnerStoredContent
            .AsNoTracking()
            .Where(total => total.OwnerId == ownerId)
            .Select(total => total.StoredContentByteCount)
            .SingleAsync(cancellationToken);

        if (ownTransaction is not null)
        {
            await ownTransaction.CommitAsync(cancellationToken);
        }

        return storedBytes;
    }

    /// <summary>Moves one account's owner's figure by a difference the caller already knows.</summary>
    /// <param name="writeContext">The context of the transaction the payload itself is written in.</param>
    /// <param name="ownerId">The owner the account belongs to, which is the half of its identity the identifier alone leaves open.</param>
    /// <param name="mailboxAccountId">The account the message belongs to, which is where the owner is recorded.</param>
    /// <param name="byteDelta">What to add, which is negative where a payload shrank.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>A task that completes when the movement has been issued inside the caller's transaction.</returns>
    /// <remarks>
    /// Used where the previous length is staged in the caller's own session rather than in the database, which is the
    /// one case a statement reading the stored row would get wrong: it would measure against a length this transaction
    /// has already replaced and count the same payload twice.
    /// </remarks>
    internal static Task MoveAsync(
        MailFathomDbContext writeContext,
        Guid ownerId,
        string mailboxAccountId,
        long byteDelta,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(writeContext);
        ArgumentException.ThrowIfNullOrEmpty(mailboxAccountId);

        return byteDelta == 0
            ? Task.CompletedTask
            : writeContext.Database.ExecuteSqlRawAsync(
                MoveStatement(writeContext.Model),
                [ownerId, mailboxAccountId, byteDelta],
                cancellationToken);
    }

    /// <summary>Moves one account's owner's figure to account for a payload about to replace what is stored.</summary>
    /// <param name="writeContext">The context of the transaction the payload itself is written in.</param>
    /// <param name="ownerId">The owner the account belongs to, which is the half of its identity the identifier alone leaves open.</param>
    /// <param name="mailboxAccountId">The account the message belongs to, which is where the owner is recorded.</param>
    /// <param name="storedEmailId">The message whose payload is being written.</param>
    /// <param name="byteLength">What the payload about to be written holds.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>A task that completes when the movement has been issued inside the caller's transaction.</returns>
    /// <remarks>
    /// Issued before the payload is written, because the difference it adds is measured against what the database still
    /// holds. A message with no payload stored contributes its whole length, which is what makes a first store and a
    /// re-synchronization the same call.
    /// </remarks>
    internal static Task AdoptLengthAsync(
        MailFathomDbContext writeContext,
        Guid ownerId,
        string mailboxAccountId,
        Guid storedEmailId,
        long byteLength,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(writeContext);
        ArgumentException.ThrowIfNullOrEmpty(mailboxAccountId);

        return writeContext.Database.ExecuteSqlRawAsync(
            AdoptLengthStatement(writeContext.Model),
            [ownerId, mailboxAccountId, storedEmailId, byteLength],
            cancellationToken);
    }

    /// <summary>Takes what a set of messages currently holds back out of their owners' figures.</summary>
    /// <param name="writeContext">The context of the transaction the messages are removed in.</param>
    /// <param name="storedEmailIds">The messages whose local copies are being erased.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>A task that completes when the movements have been issued inside the caller's transaction.</returns>
    /// <remarks>
    /// Issued before the rows are removed, because what it subtracts is read from the payloads themselves. One statement
    /// covers a batch spanning several owners, since it groups by the owner each message's account names — which is what
    /// keeps a reconciliation from having to know whose mail it was reconciling.
    /// </remarks>
    internal static Task RemoveAsync(
        MailFathomDbContext writeContext,
        IReadOnlyCollection<Guid> storedEmailIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(writeContext);
        ArgumentNullException.ThrowIfNull(storedEmailIds);

        return storedEmailIds.Count == 0
            ? Task.CompletedTask
            : writeContext.Database.ExecuteSqlRawAsync(
                RemoveStatement(writeContext.Model),
                [storedEmailIds.ToArray()],
                cancellationToken);
    }

    /// <summary>The statement that adds a movement to the figure of whichever owner one account belongs to.</summary>
    /// <remarks>
    /// Reached through the account rather than through the message, for the reason
    /// <see cref="AdoptLengthStatement" /> is: the message may not be in the database yet.
    /// </remarks>
    private static string MoveStatement(IModel model)
    {
        var names = StoredContentTableNames.Of(model);

        return $$"""
            INSERT INTO {{names.Totals}} ({{names.TotalsOwnerColumn}}, {{names.TotalsCountColumn}})
            SELECT account.{{names.AccountOwnerColumn}}, {2}
            FROM {{names.Accounts}} AS account
            WHERE account.{{names.AccountOwnerColumn}} = {0}
              AND account.{{names.AccountIdColumn}} = {1}
            ON CONFLICT ({{names.TotalsOwnerColumn}}) DO UPDATE
            SET {{names.TotalsCountColumn}} = {{names.Totals}}.{{names.TotalsCountColumn}} + EXCLUDED.{{names.TotalsCountColumn}}
            """;
    }

    /// <summary>The statement that replaces one message's contribution with the payload about to be written.</summary>
    /// <remarks>
    /// The owner is reached from the account rather than by joining the message to it, because a message arriving for
    /// the first time is stored in the same transaction as its own metadata: the row naming its account is still
    /// pending in the caller's session, so a statement joining through it would select nothing and the payload would
    /// never reach the figure. The account is configured long before any of its mail arrives, so it is always there.
    /// </remarks>
    private static string AdoptLengthStatement(IModel model)
    {
        var names = StoredContentTableNames.Of(model);

        return $$"""
            INSERT INTO {{names.Totals}} ({{names.TotalsOwnerColumn}}, {{names.TotalsCountColumn}})
            SELECT account.{{names.AccountOwnerColumn}}, {3} - COALESCE(content.{{names.ContentLengthColumn}}, 0)
            FROM {{names.Accounts}} AS account
            LEFT JOIN {{names.Contents}} AS content
                ON content.{{names.ContentEmailColumn}} = {2}
            WHERE account.{{names.AccountOwnerColumn}} = {0}
              AND account.{{names.AccountIdColumn}} = {1}
            ON CONFLICT ({{names.TotalsOwnerColumn}}) DO UPDATE
            SET {{names.TotalsCountColumn}} = {{names.Totals}}.{{names.TotalsCountColumn}} + EXCLUDED.{{names.TotalsCountColumn}}
            """;
    }

    /// <summary>The statement that takes a batch of messages' payloads back out of their owners' figures.</summary>
    private static string RemoveStatement(IModel model)
    {
        var names = StoredContentTableNames.Of(model);

        return $$"""
            INSERT INTO {{names.Totals}} ({{names.TotalsOwnerColumn}}, {{names.TotalsCountColumn}})
            SELECT email.{{names.EmailOwnerColumn}}, -SUM(content.{{names.ContentLengthColumn}})
            FROM {{names.Contents}} AS content
            JOIN {{names.Emails}} AS email
                ON email.{{names.EmailIdColumn}} = content.{{names.ContentEmailColumn}}
            WHERE content.{{names.ContentEmailColumn}} = ANY({0})
            GROUP BY email.{{names.EmailOwnerColumn}}
            ON CONFLICT ({{names.TotalsOwnerColumn}}) DO UPDATE
            SET {{names.TotalsCountColumn}} = {{names.Totals}}.{{names.TotalsCountColumn}} + EXCLUDED.{{names.TotalsCountColumn}}
            """;
    }

    /// <summary>Resolves the identifier one owner's counter is keyed by, refusing an owner naming nobody.</summary>
    /// <remarks>
    /// This port carries no gate above it — <c>MailboxSynchronizer</c> calls it directly, unlike the spend ledger,
    /// which only <c>EmbeddingSpendGate</c> reaches. So the refusal belongs here rather than at a caller: without it an
    /// unnamed owner would be given a counter row of its own keyed by an empty identifier, and bytes would be counted,
    /// maintained, and re-derived for "nobody" — which reads as a working figure until somebody asks whose it was.
    /// <c>StoredContentCeiling.LevelOf</c> refuses the same argument for the same reason.
    /// </remarks>
    private static Guid RequireNamedOwner(MailOwnerId owner)
    {
        if (!owner.IsSpecified)
        {
            throw new ArgumentException(
                "A stored-content counter is kept for a named owner, so an owner naming nobody has none to read or re-derive.",
                nameof(owner));
        }

        return owner.Value;
    }

    /// <summary>The statement that gives one owner a row to be recomputed into, and locks it either way.</summary>
    /// <remarks>
    /// The conflicting branch writes the value already there, which changes nothing and is not what it is for: what it
    /// is for is the row lock, which every movement of this figure also takes and which therefore serializes them
    /// against the recomputation that follows. A total is written back rather than a constant because PostgreSQL
    /// re-reads the row after waiting for whoever held it, so this cannot lose what that writer had just added.
    /// </remarks>
    private static string ClaimStatement(IModel model)
    {
        var names = StoredContentTableNames.Of(model);

        return $$"""
            INSERT INTO {{names.Totals}} ({{names.TotalsOwnerColumn}}, {{names.TotalsCountColumn}})
            VALUES ({0}, 0)
            ON CONFLICT ({{names.TotalsOwnerColumn}}) DO UPDATE
            SET {{names.TotalsCountColumn}} = {{names.Totals}}.{{names.TotalsCountColumn}}
            """;
    }

    /// <summary>The statement that replaces one owner's figure with what their payloads actually hold.</summary>
    /// <remarks>
    /// Issued only against a row the statement above has already claimed, which is what makes the sum it takes safe to
    /// write as a total. The one join walks content to its message, and the message carries the owner beside the
    /// account it names, so nothing here reads the account table to find out whose the mail is.
    /// </remarks>
    private static string RederiveStatement(IModel model)
    {
        var names = StoredContentTableNames.Of(model);

        return $$"""
            UPDATE {{names.Totals}}
            SET {{names.TotalsCountColumn}} = (
                SELECT COALESCE(SUM(content.{{names.ContentLengthColumn}}), 0)
                FROM {{names.Contents}} AS content
                JOIN {{names.Emails}} AS email
                    ON email.{{names.EmailIdColumn}} = content.{{names.ContentEmailColumn}}
                WHERE email.{{names.EmailOwnerColumn}} = {0})
            WHERE {{names.TotalsOwnerColumn}} = {0}
            """;
    }

    /// <summary>The identifiers every statement here is composed from, taken from the model once.</summary>
    private sealed record StoredContentTableNames(
        string Totals,
        string TotalsOwnerColumn,
        string TotalsCountColumn,
        string Contents,
        string ContentEmailColumn,
        string ContentLengthColumn,
        string Emails,
        string EmailIdColumn,
        string EmailAccountColumn,
        string EmailOwnerColumn,
        string Accounts,
        string AccountIdColumn,
        string AccountOwnerColumn)
    {
        public static StoredContentTableNames Of(IModel model)
        {
            var totals = PersistedSchemaNames.EntityTypeOf<OwnerStoredContentEntity>(model);
            var contents = PersistedSchemaNames.EntityTypeOf<EmailMessageContentEntity>(model);
            var emails = PersistedSchemaNames.EntityTypeOf<StoredEmailEntity>(model);
            var accounts = PersistedSchemaNames.EntityTypeOf<MailboxAccountEntity>(model);

            return new StoredContentTableNames(
                PersistedSchemaNames.QuotedTable(totals),
                PersistedSchemaNames.QuotedColumn(totals, nameof(OwnerStoredContentEntity.OwnerId)),
                PersistedSchemaNames.QuotedColumn(totals, nameof(OwnerStoredContentEntity.StoredContentByteCount)),
                PersistedSchemaNames.QuotedTable(contents),
                PersistedSchemaNames.QuotedColumn(contents, nameof(EmailMessageContentEntity.StoredEmailId)),
                PersistedSchemaNames.QuotedColumn(contents, nameof(EmailMessageContentEntity.MimeByteLength)),
                PersistedSchemaNames.QuotedTable(emails),
                PersistedSchemaNames.QuotedColumn(emails, nameof(StoredEmailEntity.Id)),
                PersistedSchemaNames.QuotedColumn(emails, nameof(StoredEmailEntity.MailboxAccountId)),
                PersistedSchemaNames.QuotedColumn(emails, nameof(StoredEmailEntity.OwnerId)),
                PersistedSchemaNames.QuotedTable(accounts),
                PersistedSchemaNames.QuotedColumn(accounts, nameof(MailboxAccountEntity.Id)),
                PersistedSchemaNames.QuotedColumn(accounts, nameof(MailboxAccountEntity.OwnerId)));
        }
    }
}
