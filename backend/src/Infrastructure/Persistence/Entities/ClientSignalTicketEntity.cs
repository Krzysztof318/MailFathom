// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.CodeCoverage;

namespace MailFathom.Infrastructure.Persistence.Entities;

/// <summary>One unspent ticket a client signal connection may be opened against, held until it is presented or expires.</summary>
/// <remarks>
/// <para>
/// The identifier alone is the key, because a ticket is looked up by the half a connection presents in the open and
/// proved by the half it presents beside it. The proof is held as a digest and never as the secret, so a row read out
/// of the database or out of a backup opens nothing.
/// </para>
/// <para>
/// Nothing here is mail, a message, a header, or key material. A generated user identity, a digest, and an instant are
/// the whole row, and only a request that already authenticated against a credential holding
/// <c>mailfathom.mail.read</c> produces one — so nothing an unauthenticated caller sends can reach this table.
/// </para>
/// <para>
/// It carries no concurrency token and no generated key, because it is written once and never updated. The three
/// statements against it are an insert that the deployment's own bound may refuse, a delete that returns the row it
/// removed, and a delete of what has expired.
/// </para>
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class ClientSignalTicketEntity
{
    /// <summary>The table these rows live in, named here because every statement against it is composed.</summary>
    internal const string TableName = "client_signal_tickets";

    /// <summary>The key column, named here for the same reason the table is.</summary>
    internal const string IdentifierColumnName = "Identifier";

    /// <summary>The column naming whose connection the ticket opens, named here for the same reason the table is.</summary>
    internal const string UserIdColumnName = "UserId";

    /// <summary>The column holding the proof, named here for the same reason the table is.</summary>
    internal const string SecretDigestColumnName = "SecretDigest";

    /// <summary>The expiry column, named here for the same reason the table is.</summary>
    internal const string ExpiresAtColumnName = "ExpiresAt";

    /// <summary>The longest identifier the column takes.</summary>
    /// <remarks>
    /// A bound on the row rather than a statement of the format, and it bounds what is written rather than what is
    /// looked up: only minting writes this column, and what a minting host writes is the base64url of sixteen random
    /// bytes, which is twenty-two characters. A presented value is bounded separately and more loosely — the host
    /// refuses one past a length of its own before any statement runs — so an identifier longer than this simply
    /// matches no row. The column is given room past twenty-two so the format stays the minting host's to decide, and
    /// stays short enough that nothing else could be stored in it.
    /// </remarks>
    internal const int IdentifierLengthLimit = 64;

    /// <summary>How many bytes of digest the proof column holds, which is the width of SHA-256.</summary>
    internal const int SecretDigestByteCount = 32;

    /// <summary>Gets or sets the public half of the ticket, as the minting host drew it.</summary>
    public string Identifier { get; set; } = string.Empty;

    /// <summary>Gets or sets the user whose connection this ticket opens.</summary>
    public Guid UserId { get; set; }

    /// <summary>Gets or sets the digest of the secret half, which a presented secret is compared against in constant time.</summary>
    public byte[] SecretDigest { get; set; } = [];

    /// <summary>Gets or sets when presenting the ticket stops working, in UTC.</summary>
    /// <remarks>What the removal reads, and one of the two reasons the table cannot grow without bound: a ticket lives thirty seconds, and the deployment holds only so many at once whatever their age.</remarks>
    public DateTimeOffset ExpiresAt { get; set; }
}
