// Copyright © 2026 Krzysztof Kasprowicz

using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;

namespace MailMcp.Infrastructure.Persistence.PostgreSql;

/// <summary>EF Core context for MailMcp PostgreSQL persistence adapters.</summary>
public sealed class MailMcpDbContext : DbContext
{
    /// <summary>Initializes a new database context.</summary>
    public MailMcpDbContext(DbContextOptions<MailMcpDbContext> options) : base(options) { }

    internal DbSet<MailAccountRecord> MailAccounts => this.Set<MailAccountRecord>();

    internal DbSet<MailFolderRecord> MailFolders => this.Set<MailFolderRecord>();

    internal DbSet<MessageMetadataRecord> MessageMetadata => this.Set<MessageMetadataRecord>();

    internal DbSet<MessageContentRecord> MessageContents => this.Set<MessageContentRecord>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<MailAccountRecord>(entity =>
        {
            entity.ToTable("mail_accounts");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.AccountId).HasMaxLength(128).IsRequired();
            entity.HasIndex(x => x.AccountId).IsUnique();
        });
        modelBuilder.Entity<MailFolderRecord>(entity =>
        {
            entity.ToTable("mail_folders");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.AccountId).HasMaxLength(128).IsRequired();
            entity.Property(x => x.FolderName).HasMaxLength(512).IsRequired();
            entity.HasIndex(x => new { x.AccountId, x.FolderName }).IsUnique();
        });
        modelBuilder.Entity<MessageMetadataRecord>(entity =>
        {
            entity.ToTable("message_metadata");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.AccountId).HasMaxLength(128).IsRequired();
            entity.Property(x => x.FolderName).HasMaxLength(512).IsRequired();
            entity.Property(x => x.InternetMessageId).HasMaxLength(998);
            entity.Property(x => x.Subject).HasMaxLength(998);
            entity.HasIndex(x => new { x.AccountId, x.FolderName, x.UidValidity, x.Uid }).IsUnique();
            entity.HasIndex(x => new { x.AccountId, x.FolderName, x.SentAt, x.Uid });
        });
        modelBuilder.Entity<MessageContentRecord>(entity =>
        {
            entity.ToTable("message_contents");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.AccountId).HasMaxLength(128).IsRequired();
            entity.Property(x => x.FolderName).HasMaxLength(512).IsRequired();
            entity.Property(x => x.RawMime).IsRequired();
            entity.HasIndex(x => new { x.AccountId, x.FolderName, x.UidValidity, x.Uid }).IsUnique();
        });
    }
}

[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "EF Core instantiates this record through materialization and model metadata.")]
internal sealed class MailAccountRecord
{
    public long Id { get; set; }

    public required string AccountId { get; set; }
}

internal sealed class MailFolderRecord
{
    public long Id { get; set; }

    public required string AccountId { get; set; }

    public required string FolderName { get; set; }

    public uint UidValidity { get; set; }

    public uint? LastSeenUid { get; set; }

    public DateTimeOffset? SynchronizedAt { get; set; }
}

internal sealed class MessageMetadataRecord
{
    public long Id { get; set; }

    public required string AccountId { get; set; }

    public required string FolderName { get; set; }

    public uint UidValidity { get; set; }

    public uint Uid { get; set; }

    public string? InternetMessageId { get; set; }

    public string? Subject { get; set; }

    public DateTimeOffset? SentAt { get; set; }

    public long SizeOctets { get; set; }
}

internal sealed class MessageContentRecord
{
    public long Id { get; set; }

    public required string AccountId { get; set; }

    public required string FolderName { get; set; }

    public uint UidValidity { get; set; }

    public uint Uid { get; set; }

    public required byte[] RawMime { get; set; }
}
