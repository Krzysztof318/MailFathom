// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MailFathom.Infrastructure.Persistence.Accounts.Configurations;

/// <summary>Declares the account row every folder binding, message, and durable job hangs on.</summary>
/// <remarks>
/// The row carries the configured alias and nothing else: what is known about an account beyond its identity is
/// configuration, which is read-only, so the table exists to give the rows that reference an account something to
/// reference rather than to hold state of its own.
/// </remarks>
internal sealed class MailboxAccountConfiguration : IEntityTypeConfiguration<MailboxAccountEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<MailboxAccountEntity> entity)
    {
        entity.ToTable("mailbox_accounts");

        // The account row is created by whichever run first binds one of the account's folders, so two overlapping
        // first runs insert it together and one of them loses. The key is therefore named for the same reason the
        // alias binding index below is: the loser is recognized by the constraint it violated and reported as a
        // race to resolve rather than as a failure.
        entity.HasKey(account => account.Id).HasName(PersistenceConstraintNames.MailboxAccountPrimaryKeyConstraintName);
        entity.Property(account => account.Id).HasMaxLength(128);
    }
}
