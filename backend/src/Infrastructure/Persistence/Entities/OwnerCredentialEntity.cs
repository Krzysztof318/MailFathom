// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.CodeCoverage;

namespace MailFathom.Infrastructure.Persistence.Entities;

/// <summary>One credential an owner is admitted by, whichever method presents it.</summary>
/// <remarks>
/// <para>
/// A row of its own rather than a field of the owner's settings document or a block of the deployment's configuration,
/// and every column here is why. The lookup is resolved by an index on every authenticated request, so it cannot live
/// inside a <c>jsonb</c> value nothing can index without a functional index over a document whose shape the
/// configuration layer owns. The enabled state is flipped without rewriting anything else, which a document write
/// cannot promise, and without a restart, which a configuration file cannot. And the material must never sit in a value
/// that something projects, exports, or renders as configuration — which is precisely what the owner's document and the
/// endpoint's section are for.
/// </para>
/// <para>
/// One table for all four methods, because the columns are the same columns: an owner, a value the credential is
/// resolved by, whatever material the method keeps, what the credential grants, and whether it still works. What
/// differs is what the lookup holds and what the material is judged against, and neither is a fact about where a row
/// lives. Four tables would be four indexes, four ceilings, and four administrative vocabularies for one concept.
/// </para>
/// <para>
/// One owner may hold several of these and rotate them apart, which is what makes replacing a credential something an
/// operator can do without an outage: provision the second, move the client, delete the first. No two rows may carry
/// one lookup for one method, because a lookup resolves one owner and a second row under it would make which owner a
/// request acts for depend on which row the database returned.
/// </para>
/// <para>
/// Nothing here is a mail artifact, and the row is still personal data: it says that a particular person has a way to
/// reach this deployment, and for two of the methods it is the material for doing so. It carries no address, no display
/// name, and no plaintext, and it is removed with its owner by the cascade the foreign key declares.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "EF Core materializes this entity through the DbSet and model metadata.")]
[RequiresIntegrationCoverage]
internal sealed class OwnerCredentialEntity
{
    /// <summary>The longest stored material this column holds.</summary>
    /// <remarks>
    /// Bounded by the largest of the two methods that keep any: a password's record is under a hundred characters and
    /// would be comfortably under this even for a memory-hard construction adopted later, while a client's public key
    /// is a PEM document whose largest accepted form is an RSA key of the strongest size this deployment reads. It is
    /// bounded rather than unbounded text because a column nothing bounds is one an administrative surface could be
    /// persuaded to store a page into.
    /// </remarks>
    public const int MaximumMaterialLength = 4096;

    /// <summary>The longest published method name this column holds.</summary>
    /// <remarks>Wider than every name <see cref="Domain.Access.OwnerCredentialMethod" /> publishes, so a method added later needs no migration to be storable, and narrow enough that the column is a name rather than a field.</remarks>
    public const int MaximumMethodLength = 32;

    /// <summary>The credential's stable identity, which every administrative act on it names.</summary>
    /// <remarks>
    /// A version 7 identifier like the rest of persistence mints, rather than the version 4 the owner row carries. The
    /// reasoning that made an owner's identity time-free does not reach here: what a time-ordered value would disclose
    /// is when a credential was provisioned relative to others, and every reader entitled to see this identifier is
    /// already reading the provisioning instant beside it.
    /// </remarks>
    public Guid Id { get; set; }

    /// <summary>The owner a request authenticated by this credential acts for.</summary>
    /// <remarks>The whole of what the credential grants access to. It is a foreign key rather than a copied identifier, so removing an owner removes their means of being reached with them rather than leaving a credential resolving nobody.</remarks>
    public Guid OwnerId { get; set; }

    /// <summary>How the credential is presented, as the published name of the method.</summary>
    /// <remarks>The name rather than an ordinal, because it is what an operator reads in a listing and writes on a command line, and because a column holding an ordinal would tie a stored row to the order members happened to be declared in.</remarks>
    public required string Method { get; set; }

    /// <summary>The value a presented credential is resolved by, unique within its method.</summary>
    /// <remarks>Already canonical when it is stored — a folded username, a computed digest, an issuer and subject composed in one order — so the index enforces uniqueness over the same form a request is resolved by rather than over whichever spelling reached it.</remarks>
    public required string Lookup { get; set; }

    /// <summary>The stored material the presented credential is judged against, or <see langword="null" /> for a method that keeps none.</summary>
    /// <remarks>
    /// A password's own record, which is never a plaintext and never reversible, or a client's public key, which is not
    /// secret at all. The two methods that keep nothing here keep nothing anywhere: a minted key is reduced to the
    /// digest in <see cref="Lookup" /> and never stored, and a validated subject is a claim an authorization server
    /// signed rather than anything this deployment holds.
    /// </remarks>
    public string? Material { get; set; }

    /// <summary>The published permission names a request this credential admits may hold.</summary>
    /// <remarks>
    /// <para>
    /// The grant lives on the credential because the credential is what resolves an owner: what a caller may do and
    /// whose mail they may do it to are one decision, taken when the credential is provisioned, and splitting them
    /// across a row and a configuration entry would leave an operator narrowing one while the other stayed where it
    /// was.
    /// </para>
    /// <para>
    /// An empty array is a credential that authenticates and may do nothing, which is how one is retired without being
    /// deleted. It is not the same as an unwritten grant, which the administrative surface resolves to the whole mail
    /// surface before the row is written — so nothing stored here is ever a question the reader has to answer.
    /// </para>
    /// </remarks>
    public required string[] Permissions { get; set; }

    /// <summary>Whether the credential currently authenticates requests.</summary>
    /// <remarks>
    /// The reversible half of revoking. A disabled credential keeps its lookup and its material, so nothing else can be
    /// provisioned under a name somebody is still configured with, and turning it back on is one write rather than a
    /// re-issue.
    /// </remarks>
    public bool Enabled { get; set; }

    /// <summary>How many times this row has been written, counting the insert.</summary>
    /// <remarks>
    /// Reported to an administrator beside the rest of the record, which is what makes two readings of one credential
    /// distinguishable: a listing taken before a rotation and one taken after carry the same identifier, the same
    /// lookup where the rotation did not move it, and the same instants where it did not. It is a concurrency token as
    /// well, so a writer that ever goes through change tracking is refused by number rather than committing over a row
    /// it did not read.
    /// </remarks>
    public long Version { get; set; }

    /// <summary>When the credential was provisioned.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>When what the credential is presented as was last replaced, which is the provisioning instant until it is.</summary>
    /// <remarks>
    /// Its own column rather than a general update instant, because the two answer different questions the moment a
    /// credential is disabled and enabled again: an operator asking how long a password has been in use is not asking
    /// when the row was last touched. A rehash performed on a successful sign-in deliberately leaves it alone, because
    /// what that writes is a stronger record of the material already in use — moving it would report every owner who
    /// signed in after the work parameters rose as having just chosen a new password.
    /// </remarks>
    public DateTimeOffset MaterialChangedAt { get; set; }
}
