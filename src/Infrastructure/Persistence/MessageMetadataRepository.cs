// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.Application.Synchronization;
using Microsoft.EntityFrameworkCore;

namespace MailMcp.Infrastructure.Persistence;

/// <summary>EF Core implementation for message metadata persistence.</summary>
public sealed class MessageMetadataRepository(MailMcpDbContext dbContext) : IMessageMetadataRepository
{
    /// <inheritdoc />
    public async Task UpsertMetadataAsync(
        ISession session,
        RemoteMessageMetadata metadata,
        CancellationToken cancellationToken)
    {
        var id = metadata.OccurrenceId;
        var entity = await dbContext.MessageMetadata.SingleOrDefaultAsync(x => x.AccountId == id.AccountId.Value && x.FolderName == id.FolderName.Value && x.UidValidity == id.UidValidity.Value && x.Uid == id.Uid.Value, cancellationToken);
        if (entity is null)
        {
            dbContext.MessageMetadata.Add(new MessageMetadataEntity { AccountId = id.AccountId.Value, FolderName = id.FolderName.Value, UidValidity = id.UidValidity.Value, Uid = id.Uid.Value, InternetMessageId = metadata.InternetMessageId, Subject = metadata.Subject, SentAt = metadata.SentAt, SizeOctets = metadata.SizeOctets });
        }
        else
        {
            entity.InternetMessageId = metadata.InternetMessageId;
            entity.Subject = metadata.Subject;
            entity.SentAt = metadata.SentAt;
            entity.SizeOctets = metadata.SizeOctets;
        }
    }
}
