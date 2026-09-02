// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.CodeCoverage;

namespace MailFathom.Infrastructure.Persistence.Entities;

/// <summary>The sealed refresh token MailFathom holds for one mail account, and the key that sealed it.</summary>
/// <remarks>
/// <para>
/// Keyed by the account identifier, so one account has one token and storing a rotated one replaces the row rather than
/// appending to a history. It is deliberately its own table rather than columns on the mailbox account: that row is
/// created by whichever synchronization run first binds a folder, and a stored grant has to be able to exist before any
/// synchronization has ever run — which is what makes <c>mfctl mailbox authorize</c> possible against a fresh
/// deployment.
/// </para>
/// <para>
/// The key identifier is a column of its own rather than a header inside the ciphertext, because retiring a key means
/// finding what still references it, and that is a query. It is authenticated into the sealed value, so a row whose key
/// identifier was altered fails to open rather than being redirected at another key.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "EF Core materializes this entity through the DbSet and model metadata.")]
[RequiresIntegrationCoverage]
internal sealed class MailboxRefreshTokenEntity
{
    /// <summary>The greatest length a data-encryption key identifier may have, which bounds its column.</summary>
    /// <remarks>The configuration validator accepts the same bound, so a key identifier that binds always fits the column it is written into.</remarks>
    internal const int MaximumKeyIdLength = 64;

    public required string MailboxAccountId { get; set; }

    /// <summary>Gets or sets the owner whose account this credential admits.</summary>
    /// <remarks>
    /// No cascade reaches this row, so the owner column is what an erasure names it by — a predicate on the
    /// owner rather than a lookup of which accounts were theirs.
    /// </remarks>
    public required Guid OwnerId { get; set; }

    public required byte[] SealedRefreshToken { get; set; }

    public required string DataEncryptionKeyId { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
