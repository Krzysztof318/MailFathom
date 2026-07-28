// Copyright © 2026 Krzysztof Kasprowicz

using System.Diagnostics.CodeAnalysis;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography.X509Certificates;
using MailKit;
using MailKit.Net.Imap;
using MailMcp.Application.Resilience;
using MailMcp.Application.Synchronization;
using MailMcp.Domain.Accounts;
using MailMcp.Domain.Emails;
using MailMcp.Domain.Folders;
using MailMcp.Domain.Transport;
using MailMcp.Infrastructure.Resilience;

namespace MailMcp.Infrastructure.Mail.MailKit;

/// <summary>Keeps one account authenticated for as long as a mailbox session or a folder discovery needs it.</summary>
/// <remarks>
/// <para>
/// The connection is what makes a retried read possible. A mail server that drops a socket mid-run leaves the client
/// unusable, so an attempt that finds no live connection establishes a new one before it reads. A connection opened
/// for a folder selects it with <see cref="FolderAccess.ReadOnly" /> every single time. There is no code path that
/// selects it any other way, which is what keeps the remote <c>\Seen</c> flag untouched across a recovery.
/// </para>
/// <para>
/// A connection may also be opened for no folder at all, which is what folder discovery uses: an IMAP <c>LIST</c>
/// selects nothing, so there is no folder to pin and no message whose flags could change.
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
    /// <summary>Names folder discovery in resilience telemetry, where a connection has no alias to name instead.</summary>
    private const string FolderDiscoveryOperationKey = "folder-discovery";

    private readonly Func<IImapClient> clientFactory;
    private readonly IImapAccountSettingsProvider settingsProvider;
    private readonly OutboundOperationExecutor operationExecutor;
    private readonly ITransientFailureClassifier transientFailureClassifier;
    private readonly MailAccountId accountId;
    private readonly MailFolderResolution? folder;
    private readonly MailTransportSecurityPolicy transportSecurityPolicy;

    private IImapClient? client;
    private IMailFolder? selectedFolder;
    private ImapUidValidity? sessionUidValidity;

    /// <summary>Initializes a connection that has not been established yet.</summary>
    /// <param name="clientFactory">Creates one IMAP client per establishment attempt.</param>
    /// <param name="settingsProvider">Resolves the endpoint and the credential material of the account, per attempt.</param>
    /// <param name="operationExecutor">Runs establishment and retrieval under their configured pipelines.</param>
    /// <param name="transientFailureClassifier">Decides whether a failure left the connection worth keeping.</param>
    /// <param name="accountId">The account this connection belongs to, which also isolates its pipeline state.</param>
    /// <param name="folder">The alias binding every establishment selects read-only, or <see langword="null" /> for a connection that selects no folder.</param>
    /// <param name="transportSecurityPolicy">The connection and authentication policy each attempt must obey.</param>
    /// <exception cref="ArgumentNullException">Thrown when a required collaborator is <see langword="null" />.</exception>
    internal MailKitImapConnection(
        Func<IImapClient> clientFactory,
        IImapAccountSettingsProvider settingsProvider,
        OutboundOperationExecutor operationExecutor,
        ITransientFailureClassifier transientFailureClassifier,
        MailAccountId accountId,
        MailFolderResolution? folder,
        MailTransportSecurityPolicy transportSecurityPolicy)
    {
        ArgumentNullException.ThrowIfNull(clientFactory);
        ArgumentNullException.ThrowIfNull(settingsProvider);
        ArgumentNullException.ThrowIfNull(operationExecutor);
        ArgumentNullException.ThrowIfNull(transientFailureClassifier);
        ArgumentNullException.ThrowIfNull(transportSecurityPolicy);

        this.clientFactory = clientFactory;
        this.settingsProvider = settingsProvider;
        this.operationExecutor = operationExecutor;
        this.transientFailureClassifier = transientFailureClassifier;
        this.accountId = accountId;
        this.folder = folder;
        this.transportSecurityPolicy = transportSecurityPolicy;
    }

    /// <summary>Gets whether the established connection is still in the state its owner needs.</summary>
    private bool IsUsable => this.client is { IsConnected: true }
        && (this.folder is null || this.selectedFolder is { IsOpen: true });

    /// <summary>Returns an authenticated client, establishing the session first when no usable one is open.</summary>
    /// <param name="cancellationToken">Cancels connecting, authenticating, and selecting the folder.</param>
    /// <returns>The client, authenticated and — when the connection is pinned to a folder — with that folder selected read-only.</returns>
    /// <exception cref="MailboxUnavailableException">Thrown when the establishment pipeline stopped the attempt at a configured limit.</exception>
    /// <exception cref="MailboxFolderRecreatedException">Thrown when a recovered connection reselected the folder with a different UIDVALIDITY.</exception>
    internal Task<IImapClient> EnsureAuthenticatedClientAsync(CancellationToken cancellationToken) =>
        this.IsUsable ? Task.FromResult(this.client!) : this.EstablishAsync(cancellationToken);

    /// <summary>Returns the selected folder, establishing the session first when no usable one is open.</summary>
    /// <param name="cancellationToken">Cancels connecting, authenticating, and selecting the folder.</param>
    /// <returns>The folder, selected read-only.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the connection was opened for no folder.</exception>
    /// <exception cref="MailboxUnavailableException">Thrown when the establishment pipeline stopped the attempt at a configured limit.</exception>
    /// <exception cref="MailboxFolderRecreatedException">Thrown when a recovered connection reselected the folder with a different UIDVALIDITY.</exception>
    internal async Task<IMailFolder> EnsureOpenFolderAsync(CancellationToken cancellationToken)
    {
        if (this.folder is null)
        {
            throw new InvalidOperationException("This connection was opened for folder discovery and selects no folder.");
        }

        await this.EnsureAuthenticatedClientAsync(cancellationToken);

        return this.selectedFolder!;
    }

    /// <summary>Runs a read against the selected folder under the mailbox retrieval pipeline.</summary>
    /// <typeparam name="TResult">The result the read produces.</typeparam>
    /// <param name="read">The read, which must be repeatable and must never change remote state.</param>
    /// <param name="cancellationToken">Cancels the read and every remaining attempt.</param>
    /// <returns>The result of the attempt that succeeded.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="read" /> is <see langword="null" />.</exception>
    /// <exception cref="MailboxUnavailableException">Thrown when the retrieval pipeline stopped the read at a configured limit.</exception>
    /// <remarks>
    /// Every attempt starts by making sure a folder is selected, and an attempt that failed on something worth
    /// repeating hands the next one a connection to rebuild rather than the one it was just failing on.
    /// </remarks>
    internal Task<TResult> ExecuteFolderReadAsync<TResult>(
        Func<IMailFolder, CancellationToken, Task<TResult>> read,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(read);

        return this.ExecuteUnderPipelineAsync(
            OutboundDependency.MailboxDataRetrieval,
            async attemptToken =>
            {
                var openFolder = await this.EnsureOpenFolderAsync(attemptToken);

                return await this.AttemptRepeatableReadAsync(() => read(openFolder, attemptToken));
            },
            cancellationToken);
    }

    /// <summary>Runs a read against the authenticated client under the mailbox retrieval pipeline.</summary>
    /// <typeparam name="TResult">The result the read produces.</typeparam>
    /// <param name="read">The read, which must be repeatable and must never change remote state.</param>
    /// <param name="cancellationToken">Cancels the read and every remaining attempt.</param>
    /// <returns>The result of the attempt that succeeded.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="read" /> is <see langword="null" />.</exception>
    /// <exception cref="MailboxUnavailableException">Thrown when the retrieval pipeline stopped the read at a configured limit.</exception>
    internal Task<TResult> ExecuteClientReadAsync<TResult>(
        Func<IImapClient, CancellationToken, Task<TResult>> read,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(read);

        return this.ExecuteUnderPipelineAsync(
            OutboundDependency.MailboxDataRetrieval,
            async attemptToken =>
            {
                var authenticatedClient = await this.EnsureAuthenticatedClientAsync(attemptToken);

                return await this.AttemptRepeatableReadAsync(() => read(authenticatedClient, attemptToken));
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
            await DisconnectAndDisposeAsync(ownedClient);
        }
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Every failed attempt is inspected for whether it left the connection usable; the failure itself is rethrown untouched.")]
    private async Task<TResult> AttemptRepeatableReadAsync<TResult>(Func<Task<TResult>> read)
    {
        try
        {
            return await read();
        }
        catch (Exception failure)
        {
            this.DiscardConnectionUnlessItSurvived(failure);

            throw;
        }
    }

    private async Task<IImapClient> EstablishAsync(CancellationToken cancellationToken)
    {
        this.DiscardUnusableConnection();

        return await this.ExecuteUnderPipelineAsync(
            OutboundDependency.MailboxSessionEstablishment,
            this.ConnectAuthenticateAndSelectFolderAsync,
            cancellationToken);
    }

    private async Task<IImapClient> ConnectAuthenticateAndSelectFolderAsync(CancellationToken cancellationToken)
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

                await this.AdoptSelectedFolderAsync(attemptClient, cancellationToken);

                this.client = attemptClient;

                return attemptClient;
            }
            catch
            {
                // A half-established connection is unusable by definition, and this cleanup runs inside an attempt the
                // pipeline may abandon, so it closes the socket rather than waiting on a logout the server owes it.
                Abandon(attemptClient);
                throw;
            }
        }
    }

    /// <summary>Selects the pinned folder read-only, once it is confirmed to be the one the session started on.</summary>
    /// <remarks>
    /// A server answers a reselection with its current UIDVALIDITY, and a changed one means the UIDs already handed
    /// out name different emails now. Adopting such a folder would attach the recovered folder's emails to the
    /// previous folder's checkpoint, so the session refuses it and lets the next run start the folder over. A
    /// connection opened for discovery pins no folder and selects nothing here.
    /// </remarks>
    private async Task AdoptSelectedFolderAsync(
        IImapClient establishedClient,
        CancellationToken cancellationToken)
    {
        if (this.folder is not { } pinnedFolder)
        {
            return;
        }

        var openedFolder = await establishedClient.GetFolderAsync(pinnedFolder.RemotePath.Value, cancellationToken);
        await openedFolder.OpenAsync(FolderAccess.ReadOnly, cancellationToken);

        var reselectedUidValidity = ImapUidValidity.Create(openedFolder.UidValidity);
        if (this.sessionUidValidity is { } openedUidValidity && openedUidValidity != reselectedUidValidity)
        {
            throw new MailboxFolderRecreatedException(
                this.accountId,
                pinnedFolder.Alias,
                openedUidValidity,
                reselectedUidValidity);
        }

        this.selectedFolder = openedFolder;
        this.sessionUidValidity = reselectedUidValidity;
    }

    /// <summary>Runs one mailbox operation under its pipeline and reports every spent budget as one mailbox failure.</summary>
    /// <remarks>
    /// Two outcomes mean the same thing to a caller. A limit the pipeline imposed arrives as
    /// <see cref="OutboundDependencyUnavailableException" />, and a transient failure that survived every attempt
    /// arrives as itself, because a retry strategy that runs out of attempts rethrows the last failure rather than a
    /// rejection. Both say the mail server did not serve this operation and the work belongs to a later run, so both
    /// become <see cref="MailboxUnavailableException" /> and neither lets a mail-library type past an application
    /// port. A terminal failure — a rejected credential, a refused command, an oversized payload — is the operator's
    /// to see and passes through untouched, as does the caller's own cancellation.
    /// </remarks>
    private async Task<TResult> ExecuteUnderPipelineAsync<TResult>(
        OutboundDependency dependency,
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken)
    {
        try
        {
            return await this.operationExecutor.ExecuteAsync(
                new OutboundPipelineKey(dependency, this.accountId.Value),
                this.folder?.Alias.Value ?? FolderDiscoveryOperationKey,
                operation,
                cancellationToken);
        }
        catch (OutboundDependencyUnavailableException rejection)
        {
            throw this.MailboxDidNotServeTheOperation(rejection);
        }
        catch (Exception exhaustedFailure) when (this.IsRepeatableFailure(dependency, exhaustedFailure))
        {
            throw this.MailboxDidNotServeTheOperation(exhaustedFailure);
        }
    }

    private MailboxUnavailableException MailboxDidNotServeTheOperation(Exception failure) =>
        this.folder is { } pinnedFolder
            ? new MailboxUnavailableException(this.accountId, pinnedFolder.Alias, failure)
            : new MailboxUnavailableException(this.accountId, failure);

    /// <summary>Discards the connection unless the failure proves it is still able to carry the next command.</summary>
    /// <remarks>
    /// Nothing in an exception states that the command stream is still synchronized, and a client that answers
    /// <see cref="IMailService.IsConnected" /> is only reporting a socket. A dropped connection, a
    /// desynchronized stream, and an attempt abandoned mid-command therefore all end this connection, so the next
    /// attempt starts from a session it established itself. A terminal failure ends the operation anyway and leaves
    /// the connection to its owner.
    /// </remarks>
    private void DiscardConnectionUnlessItSurvived(Exception failure)
    {
        var connectionSurvived = this.client is { IsConnected: true }
            && failure is not OperationCanceledException
            && !this.IsRepeatableFailure(OutboundDependency.MailboxDataRetrieval, failure);

        if (!connectionSurvived)
        {
            this.DiscardUnusableConnection();
        }
    }

    private bool IsRepeatableFailure(OutboundDependency dependency, Exception failure) =>
        this.transientFailureClassifier.IsTransientFailure(dependency, failure);

    private void DiscardUnusableConnection()
    {
        var discardedClient = this.client;
        this.client = null;
        this.selectedFolder = null;

        if (discardedClient is not null)
        {
            Abandon(discardedClient);
        }
    }

    /// <summary>Points the client at the account's configured authority before the handshake that will consult it.</summary>
    /// <remarks>
    /// The anchor lives as long as the connection attempt that resolved it, and so does the callback that closes over
    /// it: the client is created per attempt and disposed with it, so no callback outlives the certificate it reads.
    /// An account without a configured authority leaves the client's own validating default untouched.
    /// </remarks>
    private static void TrustConfiguredCertificateAuthority(
        IImapClient client,
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

    /// <summary>Drops a connection this type has already declared unusable, without speaking the protocol again.</summary>
    /// <remarks>
    /// A graceful logout is a command, and a command sent to a server that stopped answering waits for a reply that
    /// may never come — on a client whose only timeout is its own, far beyond the attempt budget, and through a
    /// cancellation token that this cleanup has no way to observe. The pipeline would abandon the attempt and start
    /// the next one while this call was still running against a connection object that is not safe for concurrent
    /// use. Closing the socket asks the server for nothing and cannot block on it. Politeness belongs to
    /// <see cref="DisposeAsync" />, where the session is ending in order and no attempt is racing it.
    /// </remarks>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "A connection already being replaced must not have its cleanup failure replace the failure that is being retried.")]
    [SuppressMessage("Roslynator", "RCS1075:Avoid empty catch clause that catches System.Exception", Justification = "There is no second action to take: the connection is already unusable, and the caller is about to rethrow the failure that made it so.")]
    private static void Abandon(IImapClient client)
    {
        try
        {
            client.Dispose();
        }
        catch (Exception)
        {
        }
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Both cleanup operations must be attempted while the first cleanup failure remains observable.")]
    private static async ValueTask DisconnectAndDisposeAsync(IImapClient client)
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
            client.Dispose();
        }
        catch (Exception exception)
        {
            firstCleanupException ??= exception;
        }

        if (firstCleanupException is not null)
        {
            ExceptionDispatchInfo.Capture(firstCleanupException).Throw();
        }
    }
}
