// Copyright © 2026 Krzysztof Kasprowicz

using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Net.Security;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography.X509Certificates;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using MailKit.Security;
using MailMcp.Application.EmailContent;
using MailMcp.Application.Synchronization;
using MailMcp.CodeCoverage;
using MailMcp.Domain.Accounts;
using MailMcp.Domain.Emails;
using MailMcp.Domain.Folders;
using MailMcp.Domain.Transport;

namespace MailMcp.Infrastructure.Mail.MailKit;

internal interface IMailKitImapClient : IAsyncDisposable
{
    bool IsConnected { get; }

    /// <summary>Gets the mechanism set the server advertised while connecting, which the caller narrows before authenticating.</summary>
    ISet<string> AuthenticationMechanisms { get; }

    /// <summary>Gets or sets the decision the client asks for when the platform's own certificate validation objects.</summary>
    /// <remarks>
    /// It is left unset for an account that trusts the system store alone, which keeps the client's validating default
    /// in place. Nothing assigned here may accept a certificate the configured policy rejects; it exists to admit a
    /// deployment-provisioned authority, not to forgive an error.
    /// </remarks>
    RemoteCertificateValidationCallback? ServerCertificateValidationCallback { get; set; }

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

[RequiresIntegrationCoverage]
internal sealed class MailKitImapClientAdapter(ImapClient client) : IMailKitImapClient
{
    public bool IsConnected => client.IsConnected;

    public ISet<string> AuthenticationMechanisms => client.AuthenticationMechanisms;

    public RemoteCertificateValidationCallback? ServerCertificateValidationCallback
    {
        get => client.ServerCertificateValidationCallback;
        set => client.ServerCertificateValidationCallback = value;
    }

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
    IImapAccountSettingsProvider settingsProvider) : IMailboxSessionFactory
{
    /// <inheritdoc />
    public async Task<IMailboxSession> OpenReadOnlyAsync(
        MailAccountId accountId,
        MailFolderName folderName,
        MailTransportSecurityPolicy transportSecurityPolicy,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(clientFactory);
        ArgumentNullException.ThrowIfNull(transportSecurityPolicy);

        var settings = await settingsProvider.GetSettingsAsync(accountId.Value, cancellationToken);

        // The resolved material is owned by this connection attempt and released when it ends, whether it succeeded or
        // not, so the password exists for one attempt rather than for the lifetime of the process. A rotation that
        // lands mid-attempt therefore reaches the next connection instead of the one already authenticating.
        using (settings.Material)
        {
            return await this.OpenAuthenticatedFolderAsync(
                accountId,
                folderName,
                transportSecurityPolicy,
                settings,
                cancellationToken);
        }
    }

    private async Task<IMailboxSession> OpenAuthenticatedFolderAsync(
        MailAccountId accountId,
        MailFolderName folderName,
        MailTransportSecurityPolicy transportSecurityPolicy,
        ImapAccountSettings settings,
        CancellationToken cancellationToken)
    {
        var client = clientFactory();
        try
        {
            TrustConfiguredCertificateAuthority(client, settings.Material.TrustedCertificateAuthority);

            await client.ConnectAsync(
                settings.Host,
                settings.Port,
                transportSecurityPolicy.ConnectionSecurity.ToSecureSocketOptions(),
                cancellationToken);

            // The advertised set is narrowed before authenticating, because MailKit selects a mechanism from whatever
            // remains in it. Authentication is then attempted once: retrying with a wider set would let the server
            // negotiate a mechanism the operator's allow-list refused.
            MailKitTransportSecurityMapping.RestrictAdvertisedMechanisms(
                client.AuthenticationMechanisms,
                transportSecurityPolicy.Authentication,
                settings.AccountId);

            // MailKit's authentication contract takes a string, so an un-erasable copy of the password is unavoidable
            // here. It is created at the call itself and never stored, logged, or passed on.
            await client.AuthenticateAsync(
                settings.UserName,
                settings.Material.Password.RevealAsString(),
                cancellationToken);

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

    /// <summary>Points the client at the account's configured authority before the handshake that will consult it.</summary>
    /// <remarks>
    /// The anchor lives as long as the connection attempt that resolved it, and so does the callback that closes over
    /// it: the client is created per attempt and disposed with it, so no callback outlives the certificate it reads.
    /// An account without a configured authority leaves the client's own validating default untouched.
    /// </remarks>
    private static void TrustConfiguredCertificateAuthority(
        IMailKitImapClient client,
        X509Certificate2? trustedCertificateAuthority)
    {
        if (trustedCertificateAuthority is null)
        {
            return;
        }

        client.ServerCertificateValidationCallback = (_, certificate, chain, sslPolicyErrors) =>
            MailServerCertificateValidator.IsServerCertificateTrusted(
                trustedCertificateAuthority,
                certificate,
                chain,
                sslPolicyErrors);
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
