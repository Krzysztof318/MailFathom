// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.CodeCoverage;

namespace MailFathom.Infrastructure.Persistence.Entities;

/// <summary>One session a signed-in client presents in place of the credential it signed in with, held until it is renewed, revoked, or expires.</summary>
/// <remarks>
/// <para>
/// The identifier alone is the key, because a session is looked up by the half a request presents in the open and
/// proved by the half beside it. The proof is held as a digest and never as the secret, so a row read out of the
/// database, a dump, or a backup is not a session — which is the property that makes holding these in a table at all
/// safer than holding what was presented.
/// </para>
/// <para>
/// <b>What a session carries is what the exchange established and nothing else</b>: the user it acts for, the
/// credential that authenticated it, and what that credential grants. No address, no user agent, no device name, and
/// no record of which replica minted it. None of those has a purpose here, and keeping them would turn a session store
/// into a login history the deployment never promised.
/// </para>
/// <para>
/// <b>Two cascading foreign keys carry the revocation.</b> The user reference is never absent, so an erasure removes a
/// session mechanically rather than through a walk somebody has to remember to perform. The credential reference is
/// absent where a client endpoint requiring no credential answered the exchange, which is a configuration this
/// deployment supports and which a mandatory key would have broken outright; where it is present, deleting that one
/// credential removes the sessions it minted and leaves the same person's others alone.
/// </para>
/// <para>
/// It carries no concurrency token and no generated key, because it is written once and removed rather than updated. A
/// renewal is a removal and an insert in one transaction rather than an update, which is what leaves exactly one live
/// session where two requests present one token.
/// </para>
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class ClientSessionEntity
{
    /// <summary>The table these rows live in, named here because every statement against it is composed.</summary>
    internal const string TableName = "client_sessions";

    /// <summary>The key column, named here for the same reason the table is.</summary>
    internal const string IdentifierColumnName = "Identifier";

    /// <summary>The column naming whose session this is, named here for the same reason the table is.</summary>
    internal const string UserIdColumnName = "UserId";

    /// <summary>The column naming the credential the exchange authenticated, named here for the same reason the table is.</summary>
    internal const string CredentialIdColumnName = "CredentialId";

    /// <summary>The column holding what the session admits, named here for the same reason the table is.</summary>
    internal const string PermissionsColumnName = "Permissions";

    /// <summary>The column holding the proof, named here for the same reason the table is.</summary>
    internal const string SecretDigestColumnName = "SecretDigest";

    /// <summary>The expiry column, named here for the same reason the table is.</summary>
    internal const string ExpiresAtColumnName = "ExpiresAt";

    /// <summary>The longest identifier the column takes.</summary>
    /// <remarks>
    /// A bound on the row rather than a statement of the format, and it bounds what is written rather than what is
    /// looked up: only a mint and a renewal write this column, and what they write is the base64url of sixteen random
    /// bytes, which is twenty-two characters. A presented value is bounded separately and more loosely by the host
    /// before any statement runs, so a longer identifier simply matches no row. The column is given room past
    /// twenty-two so the format stays the minting host's to decide, and stays short enough that nothing else could be
    /// stored in it.
    /// </remarks>
    internal const int IdentifierLengthLimit = 64;

    /// <summary>How many bytes of digest the proof column holds, which is the width of SHA-256.</summary>
    internal const int SecretDigestByteCount = 32;

    /// <summary>Gets or sets the public half of the token, as the minting host drew it.</summary>
    public string Identifier { get; set; } = string.Empty;

    /// <summary>Gets or sets the user whose requests this session acts for.</summary>
    public Guid UserId { get; set; }

    /// <summary>Gets or sets the credential the exchange authenticated, or <see langword="null" /> where the endpoint required none.</summary>
    public Guid? CredentialId { get; set; }

    /// <summary>Gets or sets what the session's requests may do, as the published names of the permissions the credential granted.</summary>
    /// <remarks>Held on the session rather than read from the credential on every request, because what a session admits is what the exchange admitted: a grant narrowed afterwards reaches somebody at their next sign-in, which is the bound <c>ADR 0023</c> records and this table does not change.</remarks>
    public string[] Permissions { get; set; } = [];

    /// <summary>Gets or sets the digest of the secret half, which a presented secret is compared against in constant time.</summary>
    public byte[] SecretDigest { get; set; } = [];

    /// <summary>Gets or sets when presenting the token stops working, in UTC.</summary>
    /// <remarks>What the removal reads, and one of the two reasons the table cannot grow without bound: a session lives thirty days, and the deployment holds only so many at once whatever their age.</remarks>
    public DateTimeOffset ExpiresAt { get; set; }
}
