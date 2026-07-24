// Copyright © 2026 Krzysztof Kasprowicz

using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using MailKit.Security;
using MailMcp.Application.MessageContent;
using MailMcp.Application.Synchronization;
using MailMcp.Domain.Accounts;
using MailMcp.Domain.Folders;
using MailMcp.Domain.Messages;

namespace MailMcp.Infrastructure.Mail.MailKit;

internal interface IMailKitImapClient : IAsyncDisposable
{
    bool IsConnected { get; }

    Task ConnectAsync(
        string host,
        int port,
        SecureSocketOptions options,
        CancellationToken cancellationToken);

    Task AuthenticateAsync(
        string userName,
        string password,
        CancellationToken cancellationToken);

    IMailFolder GetFolder(
        string path,
        CancellationToken cancellationToken);

    Task DisconnectAsync(
        bool quit,
        CancellationToken cancellationToken);
}

[ExcludeFromCodeCoverage(Justification = "Thin MailKit delegation wrapper requires future adapter integration coverage.")]
internal sealed class MailKitImapClientAdapter(ImapClient client) : IMailKitImapClient
{
    public bool IsConnected => client.IsConnected;

    public Task ConnectAsync(
        string host,
        int port,
        SecureSocketOptions options,
        CancellationToken cancellationToken) => client.ConnectAsync(host, port, options, cancellationToken);

    public Task AuthenticateAsync(
        string userName,
        string password,
        CancellationToken cancellationToken) => client.AuthenticateAsync(userName, password, cancellationToken);

    public IMailFolder GetFolder(
        string path,
        CancellationToken cancellationToken) => client.GetFolder(path, cancellationToken);

    public Task DisconnectAsync(
        bool quit,
        CancellationToken cancellationToken) => client.DisconnectAsync(quit, cancellationToken);

    public ValueTask DisposeAsync()
    {
        client.Dispose();
        return ValueTask.CompletedTask;
    }
}

/// <summary>MailKit-backed factory for authenticated read-only IMAP folder sessions.</summary>
internal sealed class MailKitImapMailboxSessionFactory(
    Func<IMailKitImapClient> clientFactory,
    IMailKitImapAccountSettingsProvider settingsProvider) : IMailboxSessionFactory
{
    /// <inheritdoc />
    public async Task<IMailboxSession> OpenReadOnlyAsync(
        MailAccountId accountId,
        MailFolderName folderName,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(clientFactory);
        var settings = settingsProvider.GetSettings(accountId.Value);
        var client = clientFactory();
        var ownershipTransferred = false;
        try
        {
            var socketOptions = settings.UseTls ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls;
            await client.ConnectAsync(settings.Host, settings.Port, socketOptions, cancellationToken);
            await client.AuthenticateAsync(settings.UserName, settings.Password, cancellationToken);
            var folder = client.GetFolder(folderName.Value, cancellationToken);
            await folder.OpenAsync(FolderAccess.ReadOnly, cancellationToken);
            ownershipTransferred = true;
            return new MailKitImapMailboxSession(accountId, folderName, client, folder);
        }
        finally
        {
            if (!ownershipTransferred)
            {
                await CleanupFailedOpenAsync(client);
            }
        }
    }

    private static async Task CleanupFailedOpenAsync(IMailKitImapClient client)
    {
        try
        {
            if (client.IsConnected)
            {
                await client.DisconnectAsync(quit: true, CancellationToken.None);
            }
        }
        finally
        {
            await client.DisposeAsync();
        }
    }
}

internal sealed class MailKitImapMailboxSession(
    MailAccountId accountId,
    MailFolderName folderName,
    IMailKitImapClient client,
    IMailFolder folder) : IMailboxSession
{
    public async ValueTask DisposeAsync()
    {
        if (client.IsConnected)
        {
            await client.DisconnectAsync(quit: true, CancellationToken.None);
        }

        await client.DisposeAsync();
    }

    public Task<ImapUidValidity> GetUidValidityAsync(CancellationToken cancellationToken) => Task.FromResult(ImapUidValidity.Create(folder.UidValidity));

    public async Task<RemoteMessageMetadataBatch> GetMessageBatchAfterAsync(
        ImapUid? lastSeenUid,
        int maxMessageCount,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxMessageCount);
        if (lastSeenUid is { } checkpointUid && checkpointUid.Value >= UniqueId.MaxValue.Id)
        {
            return new RemoteMessageMetadataBatch([], checkpointUid, HasMore: false);
        }

        var minValue = lastSeenUid is { } uid ? uid.Value + 1U : 1U;
        var highestAssignedUid = GetHighestAssignedUid(folder.UidNext);
        if (highestAssignedUid is null || minValue > highestAssignedUid.Value)
        {
            return new RemoteMessageMetadataBatch([], lastSeenUid, HasMore: false);
        }

        var requestedMaxValue = (ulong)minValue + (uint)maxMessageCount - 1UL;
        var maxValue = requestedMaxValue > highestAssignedUid.Value ? highestAssignedUid.Value : (uint)requestedMaxValue;
        var minUid = new UniqueId(minValue);
        var maxUid = new UniqueId(maxValue);
        var matchingUids = await folder.SearchAsync(SearchQuery.Uids(new UniqueIdRange(minUid, maxUid)), cancellationToken);
        var boundedUids = matchingUids.Take(maxMessageCount).ToArray();
        var summaries = boundedUids.Length == 0
            ? []
            : await folder.FetchAsync(boundedUids, MessageSummaryItems.Envelope | MessageSummaryItems.UniqueId | MessageSummaryItems.Size, cancellationToken);
        var uidValidity = ImapUidValidity.Create(folder.UidValidity);
        var messages = summaries.Select(summary => new RemoteMessageMetadata(
            MessageOccurrenceId.Create(accountId, folderName, uidValidity, ImapUid.Create(summary.UniqueId.Id)),
            summary.Envelope?.MessageId,
            summary.Envelope?.Subject,
            summary.Envelope?.Date?.ToUniversalTime(),
            summary.Size ?? 0)).ToArray();
        return new RemoteMessageMetadataBatch(messages, ImapUid.Create(maxValue), maxValue < highestAssignedUid.Value);
    }

    public async Task<RemoteMessageContent> FetchMessageContentWithoutSettingSeenAsync(
        MessageOccurrenceId occurrenceId,
        long maxRawMimeBytes,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxRawMimeBytes);
        // The folder is selected read-only and MailKit's GetStreamAsync issues a content retrieval for the selected UID without requesting flag mutation.
        await using var stream = await folder.GetStreamAsync(new UniqueId(occurrenceId.Uid.Value), cancellationToken);
        using var memory = new MemoryStream();
        await CopyToMemoryWithLimitAsync(occurrenceId, stream, memory, maxRawMimeBytes, cancellationToken);
        return new RemoteMessageContent(occurrenceId, memory.ToArray());
    }

    private static uint? GetHighestAssignedUid(UniqueId? uidNext)
    {
        if (uidNext is null || uidNext.Value.Id <= 1U)
        {
            return null;
        }

        return uidNext.Value.Id - 1U;
    }

    private static async Task CopyToMemoryWithLimitAsync(
        MessageOccurrenceId occurrenceId,
        Stream source,
        MemoryStream destination,
        long maxRawMimeBytes,
        CancellationToken cancellationToken)
    {
        var rentedBuffer = ArrayPool<byte>.Shared.Rent(81920);
        try
        {
            var buffer = rentedBuffer.AsMemory();
            long totalBytes = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
            {
                totalBytes += read;
                if (totalBytes > maxRawMimeBytes)
                {
                    throw new MessageContentTooLargeException(occurrenceId, totalBytes, maxRawMimeBytes);
                }

                await destination.WriteAsync(buffer[..read], cancellationToken);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rentedBuffer);
        }
    }
}
