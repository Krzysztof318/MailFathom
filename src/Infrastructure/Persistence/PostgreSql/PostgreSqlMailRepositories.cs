// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.Application.MessageContent;
using MailMcp.Application.Synchronization;
using MailMcp.Domain.Accounts;
using MailMcp.Domain.Folders;
using MailMcp.Domain.Messages;
using MailMcp.Domain.Synchronization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace MailMcp.Infrastructure.Persistence.PostgreSql;

/// <summary>PostgreSQL implementation for synchronization checkpoints.</summary>
public sealed class PostgreSqlMailSynchronizationUnitOfWorkFactory(MailMcpDbContext dbContext) : IMailSynchronizationUnitOfWorkFactory
{
    /// <inheritdoc />
    public async Task<IMailSynchronizationUnitOfWorkSession> BeginSynchronizationWriteAsync(CancellationToken cancellationToken)
    {
        var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        return new PostgreSqlMailSynchronizationUnitOfWorkSession(dbContext, transaction);
    }
}

internal sealed class PostgreSqlMailSynchronizationUnitOfWorkSession(MailMcpDbContext dbContext, IDbContextTransaction transaction) : IMailSynchronizationUnitOfWorkSession
{
    private bool completed;

    public async Task CommitAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(this.completed, this);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        this.completed = true;
    }

    public async ValueTask DisposeAsync()
    {
        if (!this.completed)
        {
            await transaction.RollbackAsync();
        }

        await transaction.DisposeAsync();
    }
}

/// <summary>PostgreSQL implementation for synchronization checkpoints.</summary>
public sealed class PostgreSqlSynchronizationCheckpointStore(MailMcpDbContext dbContext) : ISynchronizationCheckpointStore
{
    /// <inheritdoc />
    public async Task<SynchronizationCheckpoint?> GetCheckpointAsync(MailAccountId accountId, MailFolderName folderName, CancellationToken cancellationToken)
    {
        var record = await dbContext.MailFolders.AsNoTracking().SingleOrDefaultAsync(x => x.AccountId == accountId.Value && x.FolderName == folderName.Value, cancellationToken);
        if (record is null || record.UidValidity == 0)
        {
            return null;
        }

        return new SynchronizationCheckpoint(ImapUidValidity.Create(record.UidValidity), record.LastSeenUid is { } uid ? ImapUid.Create(uid) : null, record.SynchronizedAt);
    }

    /// <inheritdoc />
    public async Task SaveCheckpointAsync(IMailSynchronizationUnitOfWorkSession session, MailAccountId accountId, MailFolderName folderName, SynchronizationCheckpoint checkpoint, CancellationToken cancellationToken)
    {
        var record = await dbContext.MailFolders.SingleOrDefaultAsync(x => x.AccountId == accountId.Value && x.FolderName == folderName.Value, cancellationToken);
        if (record is null)
        {
            dbContext.MailFolders.Add(new MailFolderRecord { AccountId = accountId.Value, FolderName = folderName.Value, UidValidity = checkpoint.UidValidity.Value, LastSeenUid = checkpoint.LastSeenUid?.Value, SynchronizedAt = checkpoint.SynchronizedAt });
        }
        else
        {
            record.UidValidity = checkpoint.UidValidity.Value;
            record.LastSeenUid = checkpoint.LastSeenUid?.Value;
            record.SynchronizedAt = checkpoint.SynchronizedAt;
        }

    }
}

/// <summary>PostgreSQL implementation for message metadata persistence.</summary>
public sealed class PostgreSqlMessageMetadataRepository(MailMcpDbContext dbContext) : IMessageMetadataRepository
{
    /// <inheritdoc />
    public async Task UpsertMetadataAsync(IMailSynchronizationUnitOfWorkSession session, RemoteMessageMetadata metadata, CancellationToken cancellationToken)
    {
        var id = metadata.OccurrenceId;
        var record = await dbContext.MessageMetadata.SingleOrDefaultAsync(x => x.AccountId == id.AccountId.Value && x.FolderName == id.FolderName.Value && x.UidValidity == id.UidValidity.Value && x.Uid == id.Uid.Value, cancellationToken);
        if (record is null)
        {
            dbContext.MessageMetadata.Add(new MessageMetadataRecord { AccountId = id.AccountId.Value, FolderName = id.FolderName.Value, UidValidity = id.UidValidity.Value, Uid = id.Uid.Value, InternetMessageId = metadata.InternetMessageId, Subject = metadata.Subject, SentAt = metadata.SentAt, SizeOctets = metadata.SizeOctets });
        }
        else
        {
            record.InternetMessageId = metadata.InternetMessageId;
            record.Subject = metadata.Subject;
            record.SentAt = metadata.SentAt;
            record.SizeOctets = metadata.SizeOctets;
        }

    }
}

/// <summary>PostgreSQL raw MIME content store.</summary>
public sealed class PostgreSqlMessageContentStore(MailMcpDbContext dbContext) : IMessageContentStore
{
    /// <inheritdoc />
    public async Task SaveContentAsync(IMailSynchronizationUnitOfWorkSession session, RemoteMessageContent content, CancellationToken cancellationToken)
    {
        var id = content.OccurrenceId;
        var bytes = content.RawMime.ToArray();
        var record = await dbContext.MessageContents.SingleOrDefaultAsync(x => x.AccountId == id.AccountId.Value && x.FolderName == id.FolderName.Value && x.UidValidity == id.UidValidity.Value && x.Uid == id.Uid.Value, cancellationToken);
        if (record is null)
        {
            dbContext.MessageContents.Add(new MessageContentRecord { AccountId = id.AccountId.Value, FolderName = id.FolderName.Value, UidValidity = id.UidValidity.Value, Uid = id.Uid.Value, RawMime = bytes });
        }
        else
        {
            record.RawMime = bytes;
        }
    }
}
