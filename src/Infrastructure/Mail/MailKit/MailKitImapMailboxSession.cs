// Copyright © 2026 Krzysztof Kasprowicz

using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.ExceptionServices;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using MailKit.Security;
using MailMcp.Application.EmailContent;
using MailMcp.Application.Synchronization;
using MailMcp.Domain.Accounts;
using MailMcp.Domain.Emails;
using MailMcp.Domain.Folders;

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

    Task<IMailFolder> GetFolderAsync(
        string path,
        CancellationToken cancellationToken);

    Task DisconnectAsync(
        bool quit,
        CancellationToken cancellationToken);
}

// TODO: Remove this exclusion when the planned MailKit integration tests are enabled.
[ExcludeFromCodeCoverage(Justification = "Will be covered later by MailKit integration tests.")]
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

    public Task<IMailFolder> GetFolderAsync(
        string path,
        CancellationToken cancellationToken) => client.GetFolderAsync(path, cancellationToken);

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
        try
        {
            var socketOptions = settings.UseSslOnConnect ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls;
            await client.ConnectAsync(settings.Host, settings.Port, socketOptions, cancellationToken);
            await client.AuthenticateAsync(settings.UserName, settings.Password, cancellationToken);

            var folder = await client.GetFolderAsync(folderName.Value, cancellationToken);
            await folder.OpenAsync(FolderAccess.ReadOnly, cancellationToken);

            return new MailKitImapMailboxSession(accountId, folderName, client, folder);
        }
        catch
        {
            await CleanupFailedOpenAsync(client);
            throw;
        }
    }

    private static ValueTask CleanupFailedOpenAsync(IMailKitImapClient client) => DisconnectAndDisposeAsync(client, throwOnFailure: false);

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Both cleanup operations must be attempted while the first cleanup failure remains observable.")]
    internal static ValueTask DisconnectAndDisposeAsync(IMailKitImapClient client) => DisconnectAndDisposeAsync(client, throwOnFailure: true);

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Both cleanup operations must be attempted while the first cleanup failure remains observable.")]
    private static async ValueTask DisconnectAndDisposeAsync(
        IMailKitImapClient client,
        bool throwOnFailure)
    {
        Exception? firstCleanupException = null;
        try
        {
            if (client.IsConnected)
            {
                await client.DisconnectAsync(quit: true, CancellationToken.None);
            }
        }
        catch (Exception exception)
        {
            firstCleanupException = exception;
        }

        try
        {
            await client.DisposeAsync();
        }
        catch (Exception exception)
        {
            firstCleanupException ??= exception;
        }

        if (throwOnFailure && firstCleanupException is not null)
        {
            ExceptionDispatchInfo.Capture(firstCleanupException).Throw();
        }
    }
}

internal sealed class MailKitImapMailboxSession(
    MailAccountId accountId,
    MailFolderName folderName,
    IMailKitImapClient client,
    IMailFolder folder) : IMailboxSession
{
    public ValueTask DisposeAsync() => MailKitImapMailboxSessionFactory.DisconnectAndDisposeAsync(client);

    public Task<ImapUidValidity> GetUidValidityAsync(CancellationToken cancellationToken) => Task.FromResult(ImapUidValidity.Create(folder.UidValidity));

    public async Task<RemoteEmailMetadataBatch> GetEmailBatchAfterAsync(
        ImapUid? lastSeenUid,
        int maxEmailCount,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxEmailCount);

        if (lastSeenUid is { } checkpointUid && checkpointUid.Value >= UniqueId.MaxValue.Id)
        {
            return new RemoteEmailMetadataBatch([], checkpointUid, HasMore: false);
        }

        var minValue = lastSeenUid is { } uid ? uid.Value + 1U : 1U;
        var highestAssignedUid = GetHighestAssignedUid(folder.UidNext);
        if (highestAssignedUid is null || minValue > highestAssignedUid.Value)
        {
            return new RemoteEmailMetadataBatch([], lastSeenUid, HasMore: false);
        }

        // UID SEARCH returns identifiers only, so scanning the whole remaining assigned range stays cheap and lets the batch
        // be bounded by email count rather than by UID-space width. Bounding by UID-space width would advance a sparse
        // folder by at most maxEmailCount UIDs per batch and make an initial backfill take an impractical number of runs.
        var searchRange = new UniqueIdRange(new UniqueId(minValue), new UniqueId(highestAssignedUid.Value));
        var matchingUids = await folder.SearchAsync(SearchQuery.Uids(searchRange), cancellationToken);
        var assignedUids = matchingUids
            .Where(candidate => candidate.Id >= minValue && candidate.Id <= highestAssignedUid.Value)
            .OrderBy(candidate => candidate.Id)
            .ToArray();

        var batchedUids = assignedUids.Take(maxEmailCount).ToArray();
        var hasMore = assignedUids.Length > batchedUids.Length;

        // Everything the search covered has been inspected, so an exhausted range checkpoints to the highest assigned UID
        // even when it matched nothing. A truncated batch may only checkpoint through the last UID actually fetched.
        var inspectedThroughUid = hasMore ? batchedUids[^1].Id : highestAssignedUid.Value;
        var summaries = batchedUids.Length == 0
            ? []
            : await folder.FetchAsync(batchedUids, MessageSummaryItems.Envelope | MessageSummaryItems.UniqueId | MessageSummaryItems.Size, cancellationToken);

        var uidValidity = ImapUidValidity.Create(folder.UidValidity);
        var messages = summaries.Select(summary => new RemoteEmailMetadata(
            EmailOccurrenceId.Create(accountId, folderName, uidValidity, ImapUid.Create(summary.UniqueId.Id)),
            summary.Envelope?.MessageId,
            summary.Envelope?.Subject,
            summary.Envelope?.Date?.ToUniversalTime(),
            summary.Size ?? 0)).ToArray();

        return new RemoteEmailMetadataBatch(messages, ImapUid.Create(inspectedThroughUid), hasMore);
    }

    public async Task<RemoteEmailContent> FetchEmailContentWithoutSettingSeenAsync(
        EmailOccurrenceId occurrenceId,
        long maxRawMimeBytes,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxRawMimeBytes);

        if (occurrenceId.AccountId != accountId ||
            occurrenceId.FolderName != folderName ||
            occurrenceId.UidValidity.Value != folder.UidValidity)
        {
            throw new ArgumentException("The message occurrence does not belong to the open mailbox session.", nameof(occurrenceId));
        }

        // The folder is selected read-only and MailKit's GetStreamAsync(uid) issues "UID FETCH <uid> (BODY.PEEK[])", so neither
        // the selection mode nor the fetch item is capable of setting the remote \Seen flag. Changing this call to any
        // non-PEEK retrieval or to a StoreAsync-based flag update would break the read-only synchronization invariant.
        await using var stream = await folder.GetStreamAsync(new UniqueId(occurrenceId.Uid.Value), cancellationToken);
        using var memory = new MemoryStream();

        await CopyToMemoryWithLimitAsync(occurrenceId, stream, memory, maxRawMimeBytes, cancellationToken);

        return new RemoteEmailContent(occurrenceId, memory.ToArray());
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
        EmailOccurrenceId occurrenceId,
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
                    throw new EmailContentTooLargeException(occurrenceId, totalBytes, maxRawMimeBytes);
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
