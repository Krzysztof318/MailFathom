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

    public async Task<IReadOnlyList<RemoteMessageMetadata>> GetMessagesAfterAsync(ImapUid? lastSeenUid, CancellationToken cancellationToken)
    {
        var minUid = lastSeenUid is { } uid ? new UniqueId(uid.Value + 1) : new UniqueId(1);
        var summaries = await folder.FetchAsync(minUid, UniqueId.MaxValue, MessageSummaryItems.Envelope | MessageSummaryItems.UniqueId | MessageSummaryItems.Size, cancellationToken);
        var uidValidity = ImapUidValidity.Create(folder.UidValidity);
        return summaries.Select(summary => new RemoteMessageMetadata(MessageOccurrenceId.Create(accountId, folderName, uidValidity, ImapUid.Create(summary.UniqueId.Id)), summary.Envelope?.MessageId, summary.Envelope?.Subject, summary.Envelope?.Date, summary.Size ?? 0)).ToArray();
    }

    public async Task<RemoteMessageContent> FetchMessageContentWithoutSettingSeenAsync(MessageOccurrenceId occurrenceId, CancellationToken cancellationToken)
    {
        // The folder is selected read-only and MailKit's GetStreamAsync issues a content retrieval for the selected UID without requesting flag mutation.
        await using var stream = await folder.GetStreamAsync(new UniqueId(occurrenceId.Uid.Value), cancellationToken);
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory, cancellationToken);
        return new RemoteMessageContent(occurrenceId, memory.ToArray());
    }
}
