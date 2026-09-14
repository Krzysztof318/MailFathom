// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Accounts;
using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MailFathom.Infrastructure.Persistence.Accounts.Configurations;

/// <summary>Declares the account row every folder binding, message, and durable job hangs on.</summary>
/// <remarks>
/// <para>
/// The row carries two pieces of state configuration cannot hold because configuration is read-only: the custody
/// phase, which says whether MailFathom mirrors, holds, or is restoring the mailbox, and the local folders revision,
/// which every write to a held account's folder hierarchy bumps so that write commits only over the hierarchy it read.
/// </para>
/// <para>
/// It is keyed by the generated identifier alone, which is what ADR 0014 decided an account is identified by. That
/// identifier is unique across the deployment, so this row is the mailbox rather than one user's view of it: a second
/// user assigned the account adds an assignment and no second row, and the mail beneath this one is the one copy both
/// of them read.
/// </para>
/// <para>
/// No foreign key points out of it. The row is created lazily by whichever run first binds one of the account's
/// folders, so it cannot cascade from the account record a person provisioned, and the user it is served to is a
/// relation rather than a column — which is why removing an account's mail is the erasure seam's statement against
/// this table rather than a cascade from somewhere else.
/// </para>
/// </remarks>
internal sealed class MailboxAccountConfiguration : IEntityTypeConfiguration<MailboxAccountEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<MailboxAccountEntity> entity)
    {
        entity.ToTable("mailbox_accounts");

        // The account row is created by whichever run first binds one of the account's folders, so two overlapping
        // first runs insert it together and one of them loses. The key is therefore named for the same reason the
        // alias binding index is: the loser is recognized by the constraint it violated and reported as a race to
        // resolve rather than as a failure.
        entity.HasKey(account => account.Id)
            .HasName(PersistenceConstraintNames.MailboxAccountPrimaryKeyConstraintName);
        entity.Property(account => account.Id).HasMaxLength(128);

        // Stored by name like every other stage and phase, and defaulted in the database so every account that existed
        // before the column is mirrored, which is exactly what it was.
        entity.Property(account => account.CustodyPhase)
            .HasConversion<string>()
            .HasMaxLength(64)
            .HasDefaultValueSql($"'{nameof(MailAccountCustodyPhase.Mirrored)}'");
        entity.Property(account => account.LocalMailFoldersRevision).IsConcurrencyToken();
    }
}
