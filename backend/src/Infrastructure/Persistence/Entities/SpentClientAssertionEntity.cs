// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.CodeCoverage;
using MailFathom.Common.ClientAssertions;
using MailFathom.Infrastructure.Secrets;

namespace MailFathom.Infrastructure.Persistence.Entities;

/// <summary>One client assertion this deployment has served, remembered for as long as that assertion could be presented again.</summary>
/// <remarks>
/// <para>
/// The key is the verifying credential and the identifier together, and the uniqueness of that pair is the whole
/// mechanism: an insert that conflicts is a replay, so whether a row was written is the answer rather than something
/// read back afterwards. Scoping to the credential is what stops one client from spending an identifier another was
/// going to choose, and the two vocabularies that can appear in the column cannot collide — a user's registered key is
/// identified by its 43-character fingerprint and a configured one by the name an operator gave it.
/// </para>
/// <para>
/// Nothing here is mail, a message, a header, a request, or key material. A credential's own name, a value the client
/// minted for one request, and an instant are the whole row, and only an assertion whose signature already verified
/// produces one — so nothing an unauthenticated caller sends can reach this table.
/// </para>
/// <para>
/// It carries no concurrency token and no generated key, because it is written once and never updated. The two writes
/// against it are an insert that either happens or conflicts, and a delete of what has expired.
/// </para>
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class SpentClientAssertionEntity
{
    /// <summary>The table these rows live in, named here because both statements against it are composed.</summary>
    internal const string TableName = "spent_client_assertions";

    /// <summary>The leading key column, named here for the same reason the table is.</summary>
    internal const string CredentialKeyColumnName = "CredentialKey";

    /// <summary>The second key column, named here for the same reason the table is.</summary>
    internal const string IdentifierColumnName = "Identifier";

    /// <summary>The expiry column, named here for the same reason the table is.</summary>
    internal const string ExpiresAtColumnName = "ExpiresAt";

    /// <summary>The longest credential key the column takes, which is the longer of the two vocabularies that reach it.</summary>
    /// <remarks>A configured key is a <see cref="SecretName" />, and a user's registered key is a base64url SHA-256 fingerprint of 43 characters, so the configured name's own bound covers both.</remarks>
    internal const int CredentialKeyLengthLimit = SecretName.MaximumLength;

    /// <summary>The longest identifier the column takes, which is the one both authenticators already refuse past.</summary>
    internal const int IdentifierLengthLimit = ClientAssertion.IdentifierLengthLimit;

    /// <summary>Gets or sets what identifies the credential that verified the assertion.</summary>
    public string CredentialKey { get; set; } = string.Empty;

    /// <summary>Gets or sets the assertion's own replay identifier, as the client minted it.</summary>
    public string Identifier { get; set; } = string.Empty;

    /// <summary>Gets or sets when the assertion stops being accepted, in UTC.</summary>
    /// <remarks>What the removal reads, and the reason the table cannot grow without bound: a row outlives no assertion, and an assertion lives at most <see cref="ClientAssertion.MaximumLifetime" />.</remarks>
    public DateTimeOffset ExpiresAt { get; set; }
}
