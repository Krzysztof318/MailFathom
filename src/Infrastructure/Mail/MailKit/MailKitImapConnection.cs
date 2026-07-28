// Copyright © 2026 Krzysztof Kasprowicz

using System.Diagnostics.CodeAnalysis;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography.X509Certificates;
using MailKit;
using MailMcp.Application.Resilience;
using MailMcp.Application.Synchronization;
using MailMcp.Domain.Accounts;
using MailMcp.Domain.Emails;
using MailMcp.Domain.Folders;
using MailMcp.Domain.Transport;
using MailMcp.Infrastructure.Resilience;

namespace MailMcp.Infrastructure.Mail.MailKit;

/// <summary>Keeps one account's folder selected read-only for as long as a mailbox session needs it.</summary>
/// <remarks>
/// <para>
/// The connection is what makes a retried read possible. A mail server that drops a socket mid-run leaves the client
/// unusable, so an attempt that finds no live connection establishes a new one before it reads, and the folder is
/// selected with <see cref="FolderAccess.ReadOnly" /> every single time. There is no code path that selects it any
/// other way, which is what keeps the remote <c>\Seen</c> flag untouched across a recovery.
/// </para>
/// <para>
/// Establishment and retrieval run under different dependency classes, because a rejected credential must never be
/// repeated while a dropped read is free to be. Both pipelines are resolved for this account alone, so an unreachable
/// server opens a circuit for its own account and leaves every other account reading normally.
/// </para>
/// <para>
/// One session is used by one run at a time. Nothing here is safe for concurrent use, and no caller shares a session
/// between work units.
/// </para>
/// </remarks>
internal sealed class MailKitImapConnection : IAsyncDisposable
{
    private readonly Func<IMailKitImapClient> clientFactory;
    private readonly IImapAccountSettingsProvider settingsProvider;
    private readonly OutboundOperationExecutor operationExecutor;
    private readonly MailAccountId accountId;
    private readonly MailFolderName folderName;
    private readonly MailTransportSecurityPolicy transportSecurityPolicy;

    private IMailKitImapClient? client;
    private IMailFolder? selectedFolder;
    private ImapUidValidity? sessionUidValidity;

    /// <summary>Initializes a connection that has not been established yet.</summary>
    /// <param name="clientFactory">Creates one IMAP client per establishment attempt.</param>
    /// <param name="settingsProvider">Resolves the endpoint and the credential material of the account, per attempt.</param>
    /// <param name="operationExecutor">Runs establishment and retrieval under their configured pipelines.</param>
    /// <param name="accountId">The account this connection belongs to, which also isolates its pipeline state.</param>
    /// <param name="folderName">The folder every establishment selects read-only.</param>
    /// <param name="transportSecurityPolicy">The connection and authentication policy each attempt must obey.</param>
    /// <exception cref="ArgumentNullException">Thrown when a required collaborator is <see langword="null" />.</exception>
    internal MailKitImapConnection(
        Func<IMailKitImapClient> clientFactory,
        IImapAccountSettingsProvider settingsProvider,
        OutboundOperationExecutor operationExecutor,
        MailAccountId accountId,
        MailFolderName folderName,
        MailTransportSecurityPolicy transportSecurityPolicy)
    {
        ArgumentNullException.ThrowIfNull(clientFactory);
        ArgumentNullException.ThrowIfNull(settingsProvider);
        ArgumentNullException.ThrowIfNull(operationExecutor);
        ArgumentNullException.ThrowIfNull(transportSecurityPolicy);

        this.clientFactory = clientFactory;
        this.settingsProvider = settingsProvider;
        this.operationExecutor = operationExecutor;
        this.accountId = accountId;
        this.folderName = folderName;
        this.transportSecurityPolicy = transportSecurityPolicy;
    }

    /// <summary>Returns the selected folder, establishing the session first when no usable one is open.</summary>
    /// <param name="cancellationToken">Cancels connecting, authenticating, and selecting the folder.</param>
    /// <returns>The folder, selected read-only.</returns>
    /// <exception cref="MailboxUnavailableException">Thrown when the establishment pipeline stopped the attempt at a configured limit.</exception>
    /// <exception cref="MailboxFolderRecreatedException">Thrown when a recovered connection reselected the folder with a different UIDVALIDITY.</exception>
    internal Task<IMailFolder> EnsureOpenFolderAsync(CancellationToken cancellationToken)
    {
        if (this.client is { IsConnected: true } && this.selectedFolder is { IsOpen: true } openFolder)
        {
            return Task.FromResult(openFolder);
        }

        return this.EstablishSelectedFolderAsync(cancellationToken);
    }

    /// <summary>Runs a read against the selected folder under the mailbox retrieval pipeline.</summary>
    /// <typeparam name="TResult">The result the read produces.</typeparam>
    /// <param name="retrieval">The read, which must be repeatable and must never change remote state.</param>
    /// <param name="cancellationToken">Cancels the read and every remaining attempt.</param>
    /// <returns>The result of the attempt that succeeded.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="retrieval" /> is <see langword="null" />.</exception>
    /// <exception cref="MailboxUnavailableException">Thrown when the retrieval pipeline stopped the read at a configured limit.</exception>
    /// <remarks>
    /// Every attempt starts by making sure a folder is selected, so an attempt that follows a lost connection reads
    /// from a freshly established session instead of from a client the server has already closed.
    /// </remarks>
    internal Task<TResult> ExecuteRetrievalAsync<TResult>(
        Func<IMailFolder, CancellationToken, Task<TResult>> retrieval,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(retrieval);

        return this.ExecuteUnderPipelineAsync(
            OutboundDependency.MailboxDataRetrieval,
            async attemptToken =>
            {
                var folder = await this.EnsureOpenFolderAsync(attemptToken);

                return await retrieval(folder, attemptToken);
            },
            cancellationToken);
    }

    /// <summary>Closes and releases the connection, reporting the first cleanup failure.</summary>
    public async ValueTask DisposeAsync()
    {
        var ownedClient = this.client;
        this.client = null;
        this.selectedFolder = null;

        if (ownedClient is not null)
        {
            await DisconnectAndDisposeAsync(ownedClient, throwOnFailure: true);
        }
    }

    private async Task<IMailFolder> EstablishSelectedFolderAsync(CancellationToken cancellationToken)
    {
        await this.DiscardUnusableConnectionAsync();

        return await this.ExecuteUnderPipelineAsync(
            OutboundDependency.MailboxSessionEstablishment,
            this.ConnectAuthenticateAndSelectFolderAsync,
            cancellationToken);
    }

    private async Task<IMailFolder> ConnectAuthenticateAndSelectFolderAsync(CancellationToken cancellationToken)
    {
        var settings = await this.settingsProvider.GetSettingsAsync(this.accountId.Value, cancellationToken);

        // The resolved material is owned by this connection attempt and released when it ends, whether it succeeded or
        // not, so the password exists for one attempt rather than for the lifetime of the process. A rotation that
        // lands mid-attempt therefore reaches the next attempt instead of the one already authenticating.
        using (settings.Material)
        {
            var attemptClient = this.clientFactory();
            try
            {
                TrustConfiguredCertificateAuthority(attemptClient, settings.Material.TrustedCertificateAuthority);

                await attemptClient.ConnectAsync(
                    settings.Host,
                    settings.Port,
                    this.transportSecurityPolicy.ConnectionSecurity.ToSecureSocketOptions(),
                    cancellationToken);

                // The advertised set is narrowed before authenticating, because MailKit selects a mechanism from
                // whatever remains in it. Authentication is then attempted once: retrying with a wider set would let
                // the server negotiate a mechanism the operator's allow-list refused.
                MailKitTransportSecurityMapping.RestrictAdvertisedMechanisms(
                    attemptClient.AuthenticationMechanisms,
                    this.transportSecurityPolicy.Authentication,
                    settings.AccountId);

                // MailKit's authentication contract takes a string, so an un-erasable copy of the password is
                // unavoidable here. It is created at the call itself and never stored, logged, or passed on.
                await attemptClient.AuthenticateAsync(
                    settings.UserName,
                    settings.Material.Password.RevealAsString(),
                    cancellationToken);

                var folder = await attemptClient.GetFolderAsync(this.folderName.Value, cancellationToken);
                await folder.OpenAsync(FolderAccess.ReadOnly, cancellationToken);

                this.AdoptSelectedFolder(attemptClient, folder);

                return folder;
            }
            catch
            {
                await DisconnectAndDisposeAsync(attemptClient, throwOnFailure: false);
                throw;
            }
        }
    }

    /// <summary>Takes ownership of an established client, once its folder is confirmed to be the one the session started on.</summary>
    /// <remarks>
    /// A server answers a reselection with its current UIDVALIDITY, and a changed one means the UIDs already handed
    /// out name different emails now. Adopting such a folder would attach the recovered folder's emails to the
    /// previous folder's checkpoint, so the session refuses it and lets the next run start the folder over.
    /// </remarks>
    private void AdoptSelectedFolder(IMailKitImapClient establishedClient, IMailFolder folder)
    {
        var reselectedUidValidity = ImapUidValidity.Create(folder.UidValidity);

        if (this.sessionUidValidity is { } openedUidValidity && openedUidValidity != reselectedUidValidity)
        {
            throw new MailboxFolderRecreatedException(
                this.accountId,
                this.folderName,
                openedUidValidity,
                reselectedUidValidity);
        }

        this.client = establishedClient;
        this.selectedFolder = folder;
        this.sessionUidValidity = reselectedUidValidity;
    }

    private async Task<TResult> ExecuteUnderPipelineAsync<TResult>(
        OutboundDependency dependency,
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken)
    {
        try
        {
            return await this.operationExecutor.ExecuteAsync(
                new OutboundPipelineKey(dependency, this.accountId.Value),
                this.folderName.Value,
                operation,
                cancellationToken);
        }
        catch (OutboundDependencyUnavailableException rejection)
        {
            throw new MailboxUnavailableException(this.accountId, this.folderName, rejection);
        }
    }

    private async Task DiscardUnusableConnectionAsync()
    {
        var discardedClient = this.client;
        this.client = null;
        this.selectedFolder = null;

        if (discardedClient is not null)
        {
            // The connection being replaced is the one that already failed, so a cleanup failure on it says nothing
            // the caller needs and must not replace the failure that is about to be retried.
            await DisconnectAndDisposeAsync(discardedClient, throwOnFailure: false);
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
