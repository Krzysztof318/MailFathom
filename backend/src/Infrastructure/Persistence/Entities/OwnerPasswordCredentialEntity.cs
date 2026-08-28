// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.CodeCoverage;

namespace MailFathom.Infrastructure.Persistence.Entities;

/// <summary>One username-and-password credential an owner signs in with.</summary>
/// <remarks>
/// <para>
/// A row of its own rather than a field of the owner's settings document, and every column here is why. The username is
/// resolved by an index on every authenticated request, so it cannot live inside a <c>jsonb</c> value nothing can index
/// without a functional index over a document whose shape the configuration layer owns. The enabled state is flipped
/// without rewriting anything else, which a document write cannot promise. And the hash must never sit in a value that
/// something projects, exports, or renders as configuration — which is precisely what the owner's document is for.
/// </para>
/// <para>
/// One owner may hold several of these and rotate them apart, which is what makes replacing a credential something an
/// operator can do without an outage: provision the second, move the client, delete the first. No two rows may carry one
/// username, because a username resolves one owner and a second row under it would make which owner a request acts for
/// depend on which row the database returned.
/// </para>
/// <para>
/// Nothing here is a mail artifact, and the row is still personal data: it says that a particular person has a way to
/// sign in to this deployment, and it is the credential material for doing so. It carries no address, no display name,
/// and no plaintext, and it is removed with its owner by the cascade the foreign key declares.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "EF Core materializes this entity through the DbSet and model metadata.")]
[RequiresIntegrationCoverage]
internal sealed class OwnerPasswordCredentialEntity
{
    /// <summary>The longest stored password representation this column holds.</summary>
    /// <remarks>
    /// Comfortably past what the construction in use writes, which is under a hundred characters, and past what a
    /// memory-hard construction adopted later would write. It is bounded rather than unbounded text because a column
    /// nothing bounds is one an administrative surface could be persuaded to store a page into.
    /// </remarks>
    public const int MaximumPasswordHashLength = 512;

    /// <summary>The credential's stable identity, which every administrative act on it names.</summary>
    /// <remarks>
    /// A version 7 identifier like the rest of persistence mints, rather than the version 4 the owner row carries. The
    /// reasoning that made an owner's identity time-free does not reach here: what a time-ordered value would disclose
    /// is when a credential was provisioned relative to others, and every reader entitled to see this identifier is
    /// already reading the provisioning instant beside it.
    /// </remarks>
    public Guid Id { get; set; }

    /// <summary>The owner a request authenticated by this credential acts for.</summary>
    /// <remarks>The whole of what the credential grants access to. It is a foreign key rather than a copied identifier, so removing an owner removes their means of signing in with them rather than leaving a credential resolving nobody.</remarks>
    public Guid OwnerId { get; set; }

    /// <summary>The canonical username the credential is resolved by, unique across the deployment.</summary>
    /// <remarks>Already folded when it is stored — trimmed and lowercased — so the index enforces uniqueness over the same form a request is resolved by rather than over the spelling somebody happened to provision it with.</remarks>
    public required string Username { get; set; }

    /// <summary>The stored representation of the password, carrying its own algorithm, version, salt, and work parameters.</summary>
    /// <remarks>Never a plaintext and never reversible. It is read on exactly one path — judging a presented password — and is excluded from every projection an administrative answer is composed from.</remarks>
    public required string PasswordHash { get; set; }

    /// <summary>Whether the credential currently authenticates requests.</summary>
    /// <remarks>
    /// The reversible half of revoking. A disabled credential keeps its username and its password, so nothing else can
    /// be provisioned under a name somebody is still configured with, and turning it back on is one write rather than
    /// a re-issue.
    /// </remarks>
    public bool Enabled { get; set; }

    /// <summary>How many times this row has been written, counting the insert.</summary>
    /// <remarks>
    /// Reported to an administrator beside the rest of the record, which is what makes two readings of one credential
    /// distinguishable: a listing taken before a rotation and one taken after carry the same identifier, the same
    /// username, and the same instants where the rotation did not move them. It is a concurrency token as well, so a
    /// writer that ever goes through change tracking is refused by number rather than committing over a row it did not
    /// read.
    /// </remarks>
    public long Version { get; set; }

    /// <summary>When the credential was provisioned.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>When the stored password was last replaced, which is the provisioning instant until it is rotated.</summary>
    /// <remarks>
    /// Its own column rather than a general update instant, because the two answer different questions the moment a
    /// credential is disabled and enabled again: an operator asking how long a password has been in use is not asking
    /// when the row was last touched. A rehash performed on a successful sign-in deliberately leaves it alone, because
    /// what that writes is a stronger record of the password already in use — moving it would report every owner who
    /// signed in after the work parameters rose as having just chosen a new password.
    /// </remarks>
    public DateTimeOffset PasswordChangedAt { get; set; }
}
