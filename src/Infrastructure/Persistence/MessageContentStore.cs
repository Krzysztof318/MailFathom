// Copyright © 2026 Krzysztof Kasprowicz

using System.Diagnostics.CodeAnalysis;
using MailMcp.Application.MessageContent;
using MailMcp.Application.Synchronization;
using Microsoft.EntityFrameworkCore;

namespace MailMcp.Infrastructure.Persistence;

/// <summary>EF Core raw MIME content store.</summary>
[ExcludeFromCodeCoverage(Justification = "Provider-boundary adapter behavior requires future integration coverage.")]
public sealed class MessageContentStore(MailMcpDbContext dbContext) : IMessageContentStore
{
    /// <inheritdoc />
    public async Task SaveContentAsync(
        ISession session,
        RemoteMessageContent content,
        CancellationToken cancellationToken)
    {
        var id = content.OccurrenceId;
        var bytes = content.RawMime.ToArray();
        var entity = await dbContext.MessageContents.SingleOrDefaultAsync(x => x.AccountId == id.AccountId.Value && x.FolderName == id.FolderName.Value && x.UidValidity == id.UidValidity.Value && x.Uid == id.Uid.Value, cancellationToken);
        if (entity is null)
        {
            dbContext.MessageContents.Add(new MessageContentEntity { AccountId = id.AccountId.Value, FolderName = id.FolderName.Value, UidValidity = id.UidValidity.Value, Uid = id.Uid.Value, RawMime = bytes });
        }
        else
        {
            entity.RawMime = bytes;
        }
    }
}
