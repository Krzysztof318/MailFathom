// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.CodeCoverage;

namespace MailFathom.Infrastructure.Persistence.Entities;

/// <summary>Room one replica has reserved in local content storage for a payload it is about to fetch and store.</summary>
/// <remarks>
/// <para>
/// One row per payload in flight, and never more than the deployment's replicas are fetching at once. What the rows say
/// together is the only thing a replica cannot measure for itself: what the others have reserved since the occupancy
/// each of them would otherwise have read. A stored payload leaves no row behind, because what it occupies is then part
/// of what the next claim measures.
/// </para>
/// <para>
/// Every row expires, which is the release of last resort. The ordinary release is the holder giving the claim back
/// once the payload has reached storage or been abandoned; the expiry is what stops a replica that stopped answering
/// from reserving room until an operator notices, and it is why a claim's sums are always taken over the unexpired rows
/// rather than over the whole table.
/// </para>
/// <para>
/// The user is a plain column with no foreign key onto the user record, for a reason narrower than the spend ledger's:
/// a claim outlives nothing at all, so a cascade would have nothing to erase that the expiry does not. What the column
/// carries is which per-user ceiling the reservation counts against while it binds.
/// </para>
/// <para>
/// Nothing here is mail or derived from it. A byte count, two instants, and a generated identity say how much room is
/// reserved and until when; nothing names a message, a folder, or an account.
/// </para>
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class StoredContentClaimEntity
{
    /// <summary>The table these rows live in, named here because the claim is a composed statement.</summary>
    internal const string TableName = "stored_content_claims";

    /// <summary>The key column, named here for the same reason the table is.</summary>
    internal const string IdColumnName = "Id";

    /// <summary>The column naming whose per-user ceiling the reservation counts against.</summary>
    internal const string UserIdColumnName = "UserId";

    /// <summary>The reserved column, named here for the same reason the table is.</summary>
    internal const string ClaimedByteCountColumnName = "ClaimedByteCount";

    /// <summary>The column deciding whether a row still binds.</summary>
    internal const string ExpiresAtColumnName = "ExpiresAt";

    /// <summary>Gets or sets the claim's identity, which is what a holder releases it by.</summary>
    public Guid Id { get; set; }

    /// <summary>Gets or sets the user whose mail the reserved payload is.</summary>
    public Guid UserId { get; set; }

    /// <summary>Gets or sets how much room this claim reserves.</summary>
    /// <remarks>
    /// What the payload is expected to occupy, which is the size the mail server advertised, or the size limit where it
    /// advertised none. Sixty-four bits because it is charged against a ceiling of the same width.
    /// </remarks>
    public long ClaimedByteCount { get; set; }

    /// <summary>Gets or sets when this claim stops binding whether or not its holder released it, in UTC.</summary>
    public DateTimeOffset ExpiresAt { get; set; }
}
