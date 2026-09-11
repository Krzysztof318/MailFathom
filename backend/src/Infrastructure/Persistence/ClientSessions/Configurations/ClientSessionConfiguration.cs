// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MailFathom.Infrastructure.Persistence.ClientSessions.Configurations;

/// <summary>Declares the sessions the deployment's signed-in clients present in place of the credentials they signed in with.</summary>
/// <remarks>
/// <para>
/// The identifier is the key, because every statement here reaches one row by the half of a token a request presents in
/// the open. Beside it sits the index on the expiry, which the removal of what can no longer authenticate walks. The
/// column names are the entity's own constants because every statement is composed, so the statements and this mapping
/// name the same things by construction.
/// </para>
/// <para>
/// <b>Two cascading foreign keys, and the difference between them is the whole point.</b> The one to the user is what
/// makes an erasure mechanical and is the only thing that reaches a session an endpoint requiring no credential minted,
/// which is why the column is required. The one to the credential is what makes deleting a credential end the sessions
/// it minted without touching that person's others, and its column is optional for exactly the case the user key
/// covers alone.
/// </para>
/// </remarks>
internal sealed class ClientSessionConfiguration : IEntityTypeConfiguration<ClientSessionEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ClientSessionEntity> entity)
    {
        entity.ToTable(ClientSessionEntity.TableName);
        entity.HasKey(session => session.Identifier);
        entity.Property(session => session.Identifier)
            .HasColumnName(ClientSessionEntity.IdentifierColumnName)
            .HasMaxLength(ClientSessionEntity.IdentifierLengthLimit)
            .ValueGeneratedNever();
        entity.Property(session => session.UserId)
            .HasColumnName(ClientSessionEntity.UserIdColumnName);
        entity.Property(session => session.CredentialId)
            .HasColumnName(ClientSessionEntity.CredentialIdColumnName);
        entity.Property(session => session.Permissions)
            .HasColumnName(ClientSessionEntity.PermissionsColumnName)
            .IsRequired();
        entity.Property(session => session.SecretDigest)
            .HasColumnName(ClientSessionEntity.SecretDigestColumnName)
            .HasMaxLength(ClientSessionEntity.SecretDigestByteCount)
            .IsRequired();
        entity.Property(session => session.ExpiresAt)
            .HasColumnName(ClientSessionEntity.ExpiresAtColumnName);
        entity.HasIndex(session => session.ExpiresAt)
            .HasDatabaseName(PersistenceConstraintNames.ClientSessionExpiryIndexName);

        // The erasure guarantee itself rather than a convenience: a session names the user it acts for, so removing
        // that user removes it whether or not a credential stands behind it. Walking a store by user is what this
        // replaces, and the walk is what an erasure could forget to call.
        entity.HasOne<UserAccountEntity>()
            .WithMany()
            .HasForeignKey(session => session.UserId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);

        // Optional, because a client endpoint requiring no credential still answers the exchange and still mints a
        // session: there is no row behind it for an operator to revoke it by, which is what the page an operator reads
        // already tells them. Where there is one, deleting it ends the sessions it minted and leaves the same person's
        // other sessions alone, which is what a key onto the user alone could not express.
        entity.HasOne<UserCredentialEntity>()
            .WithMany()
            .HasForeignKey(session => session.CredentialId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
