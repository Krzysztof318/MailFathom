// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.CodeCoverage;

namespace MailFathom.Infrastructure.Persistence.Entities;

/// <summary>Who is holding one unit of work that must not run twice, and until when.</summary>
/// <remarks>
/// <para>
/// One row per <em>held</em> scope and none for a scope nothing holds, because a release deletes the row rather than
/// clearing it: the table is then the set of holds the deployment currently has, which is what an operator asking who
/// holds what reads, and what a claim's insert can create without anything having seeded it.
/// </para>
/// <para>
/// There is no foreign key onto anything. The scope is a composed key rather than a reference — an account, a named
/// walk, or the deployment — so what it names is resolved by reading it rather than by a constraint, and a lease
/// outliving its subject is a row an expiry frees rather than one a cascade has to reach.
/// </para>
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class WorkLeaseEntity
{
    public required string Scope { get; set; }

    public required string Holder { get; set; }

    /// <summary>Gets or sets the replica the current holder belongs to, which is what an operator reads to find the process the work is happening in.</summary>
    /// <remarks>
    /// Descriptive and never a predicate. Every conditional write here is refused against <see cref="Holder" />, which
    /// names one hold; this names the process that took it, so an operator asking who holds an account is told
    /// something they can find a log for rather than a generated identity that names nothing. A takeover replaces it
    /// with the new holder's, because the row describes the hold it currently records.
    /// </remarks>
    public required string Replica { get; set; }

    /// <summary>Gets or sets when the current holder took the scope, which a renewal leaves where it is.</summary>
    public DateTimeOffset HeldSince { get; set; }

    /// <summary>Gets or sets the instant after which the scope is takeable again whatever its holder is doing.</summary>
    /// <remarks>
    /// It is the only column a renewal moves, so how recently a holder renewed is read back from how far ahead of now
    /// this instant sits rather than from a second column recording the write.
    /// </remarks>
    public DateTimeOffset ExpiresAt { get; set; }
}
