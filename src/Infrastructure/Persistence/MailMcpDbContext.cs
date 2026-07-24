// Copyright © 2026 Krzysztof Kasprowicz

using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;

namespace MailMcp.Infrastructure.Persistence;

/// <summary>EF Core context for local MailMcp persistence.</summary>
// TODO: Remove this exclusion when the planned PostgreSQL integration tests are enabled.
[ExcludeFromCodeCoverage(Justification = "Will be covered later by PostgreSQL integration tests.")]
public sealed class MailMcpDbContext : DbContext
{
    /// <summary>Initializes a new MailMcp EF Core context.</summary>
    public MailMcpDbContext(DbContextOptions<MailMcpDbContext> options)
        : base(options)
    {
    }

    internal DbSet<MailAccountEntity> MailAccounts => this.Set<MailAccountEntity>();

    internal DbSet<MailFolderEntity> MailFolders => this.Set<MailFolderEntity>();

    internal DbSet<MessageMetadataEntity> MessageMetadata => this.Set<MessageMetadataEntity>();

    internal DbSet<MessageContentEntity> MessageContents => this.Set<MessageContentEntity>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<MailAccountEntity>(entity =>
        {
            entity.ToTable("mail_accounts");
            entity.HasKey(x => x.AccountId);
            entity.Property(x => x.AccountId).HasMaxLength(128);
        });

        modelBuilder.Entity<MailFolderEntity>(entity =>
        {
            entity.ToTable("mail_folders");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.AccountId).HasMaxLength(128);
            entity.Property(x => x.FolderName).HasMaxLength(512);
            entity.HasIndex(x => new { x.AccountId, x.FolderName }).IsUnique();
        });

        modelBuilder.Entity<MessageMetadataEntity>(entity =>
        {
            entity.ToTable("message_metadata");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.AccountId).HasMaxLength(128);
            entity.Property(x => x.FolderName).HasMaxLength(512);
            entity.Property(x => x.InternetMessageId).HasMaxLength(998);
            entity.Property(x => x.Subject).HasMaxLength(998);
            entity.HasIndex(x => new { x.AccountId, x.FolderName, x.UidValidity, x.Uid }).IsUnique();
        });

        modelBuilder.Entity<MessageContentEntity>(entity =>
        {
            entity.ToTable("message_contents");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.AccountId).HasMaxLength(128);
            entity.Property(x => x.FolderName).HasMaxLength(512);
            entity.Property(x => x.RawMime).IsRequired();
            entity.HasIndex(x => new { x.AccountId, x.FolderName, x.UidValidity, x.Uid }).IsUnique();
        });
    }
}
