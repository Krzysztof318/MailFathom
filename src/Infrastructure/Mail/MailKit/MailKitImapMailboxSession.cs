// Copyright © 2026 Krzysztof Kasprowicz

using MailKit;
using MailKit.Net.Imap;
using MailKit.Security;
using MailMcp.Application.MessageContent;
using MailMcp.Application.Synchronization;
using MailMcp.Domain.Accounts;
using MailMcp.Domain.Folders;
using MailMcp.Domain.Messages;

namespace MailMcp.Infrastructure.Mail.MailKit;

/// <summary>MailKit-backed factory for authenticated read-only IMAP folder sessions.</summary>
public sealed class MailKitImapMailboxSessionFactory(Func<ImapClient> clientFactory, IMailKitImapAccountSettingsProvider settingsProvider) : IImapMailboxSessionFactory
{
    /// <inheritdoc />
    public async Task<IImapMailboxSession> OpenReadOnlyAsync(MailAccountId accountId, MailFolderName folderName, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(clientFactory);
        var settings = settingsProvider.GetSettings(accountId.Value);
        var client = clientFactory();
        var socketOptions = settings.UseTls ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls;
        await client.ConnectAsync(settings.Host, settings.Port, socketOptions, cancellationToken);
        await client.AuthenticateAsync(settings.UserName, settings.Password, cancellationToken);
        var folder = client.GetFolder(folderName.Value);
        await folder.OpenAsync(FolderAccess.ReadOnly, cancellationToken);
        return new MailKitImapMailboxSession(accountId, folderName, client, folder);
    }
}

internal sealed class MailKitImapMailboxSession(MailAccountId accountId, MailFolderName folderName, ImapClient client, IMailFolder folder) : IImapMailboxSession
{
    public async ValueTask DisposeAsync()
    {
        if (client.IsConnected)
        {
            await client.DisconnectAsync(quit: true, CancellationToken.None);
        }

        client.Dispose();
    }

    public Task<ImapUidValidity> GetUidValidityAsync(CancellationToken cancellationToken) => Task.FromResult(ImapUidValidity.Create(folder.UidValidity));

    public async Task<RemoteMessageMetadataBatch> GetMessageBatchAfterAsync(ImapUid? lastSeenUid, int maxMessageCount, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxMessageCount);
        if (lastSeenUid is { } checkpointUid && checkpointUid.Value >= UniqueId.MaxValue.Id)
        {
            return new RemoteMessageMetadataBatch([], checkpointUid, HasMore: false);
        }

        var minValue = lastSeenUid is { } uid ? uid.Value + 1 : 1U;
        var requestedMaxValue = (ulong)minValue + (uint)maxMessageCount - 1UL;
        var maxValue = requestedMaxValue > UniqueId.MaxValue.Id ? UniqueId.MaxValue.Id : (uint)requestedMaxValue;
        var minUid = new UniqueId(minValue);
        var maxUid = new UniqueId(maxValue);
        var summaries = await folder.FetchAsync(minUid, maxUid, MessageSummaryItems.Envelope | MessageSummaryItems.UniqueId | MessageSummaryItems.Size, cancellationToken);
        var uidValidity = ImapUidValidity.Create(folder.UidValidity);
        var messages = summaries.Select(summary => new RemoteMessageMetadata(MessageOccurrenceId.Create(accountId, folderName, uidValidity, ImapUid.Create(summary.UniqueId.Id)), summary.Envelope?.MessageId, summary.Envelope?.Subject, summary.Envelope?.Date, summary.Size ?? 0)).ToArray();
        return new RemoteMessageMetadataBatch(messages, ImapUid.Create(maxValue), maxValue < UniqueId.MaxValue.Id);
    }

    public async Task<RemoteMessageContent> FetchMessageContentWithoutSettingSeenAsync(MessageOccurrenceId occurrenceId, long maxRawMimeBytes, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxRawMimeBytes);
        // The folder is selected read-only and MailKit's GetStreamAsync issues a content retrieval for the selected UID without requesting flag mutation.
        await using var stream = await folder.GetStreamAsync(new UniqueId(occurrenceId.Uid.Value), cancellationToken);
        using var memory = new MemoryStream();
        await CopyToMemoryWithLimitAsync(occurrenceId, stream, memory, maxRawMimeBytes, cancellationToken);
        return new RemoteMessageContent(occurrenceId, memory.ToArray());
    }

    private static async Task CopyToMemoryWithLimitAsync(MessageOccurrenceId occurrenceId, Stream source, MemoryStream destination, long maxRawMimeBytes, CancellationToken cancellationToken)
    {
        var buffer = new byte[81920];
        long totalBytes = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
        {
            totalBytes += read;
            if (totalBytes > maxRawMimeBytes)
            {
                throw new MessageContentTooLargeException(occurrenceId, totalBytes, maxRawMimeBytes);
            }

            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
    }
}
