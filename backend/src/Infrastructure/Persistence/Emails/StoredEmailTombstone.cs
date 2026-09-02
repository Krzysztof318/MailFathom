// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Linq.Expressions;
using MailFathom.Domain.Emails;
using MailFathom.Infrastructure.Persistence.Entities;

namespace MailFathom.Infrastructure.Persistence.Emails;

/// <summary>States which stored rows a tombstone hides, once for every query that has to exclude one.</summary>
/// <remarks>
/// <para>
/// A row whose remote occurrence has gone is hidden by default and readable only where an authored delete said to keep
/// it, so the rule is two columns rather than one and no longer reads as an obvious null check at its call sites. Every
/// query that narrows to mail a reader may see composes this expression instead of restating it: a listing, a search, a
/// content read, and both backfills, which must not spend extraction or a provider's embeddings on mail nothing may
/// retrieve.
/// </para>
/// <para>
/// Written once because the two ways to get it wrong are opposite and both are silent. Repeating only the timestamp
/// check hides mail an operator configured
/// <see cref="AuthoredDeleteEmailDisposition.RetainLocalCopy" /> to keep; forgetting the check entirely serves mail the
/// server no longer holds and nobody agreed to keep.
/// </para>
/// </remarks>
internal static class StoredEmailTombstone
{
    /// <summary>Admits the rows that are still part of the local mailbox, which PostgreSQL evaluates in full.</summary>
    /// <remarks>
    /// It is an expression rather than a method so it composes into a query as a predicate the provider translates. A
    /// helper called inside a lambda would either fail to translate or drag the rest of the pipeline into the process.
    /// </remarks>
    internal static Expression<Func<StoredEmailEntity, bool>> IsNotTombstoned { get; } =
        email => email.RemoteExpungeObservedAt == null || email.IsRetainedAfterAuthoredDelete;
}
