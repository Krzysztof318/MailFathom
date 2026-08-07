// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography.X509Certificates;
using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Resilience;
using MailFathom.Application.Synchronization.Sessions;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Transport;
using MailFathom.Infrastructure.Mail.OAuth;
using MailFathom.Infrastructure.Resilience;
using MailKit;
using MailKit.Net.Imap;

namespace MailFathom.Infrastructure.Mail.MailKit;

/// <summary>Keeps one account authenticated for as long as a mailbox session, a folder discovery, or a mutation needs it.</summary>
/// <remarks>
/// <para>
/// The connection is what makes a retried read possible. A mail server that drops a socket mid-run leaves the client
/// unusable, so an attempt that finds no live connection establishes a new one before it reads.
/// </para>
/// <para>
/// How a connection selects its folder is fixed when it is created and can never change afterwards, which is what
/// keeps the remote <c>\Seen</c> flag untouched across a recovery. <see cref="ForReading" /> selects with
/// <see cref="FolderAccess.ReadOnly" /> on every establishment and on every reconnection, and a connection created
/// that way refuses <see cref="ExecuteMutationAsync" /> outright rather than relying on its callers not to ask.
/// <see cref="ForWriting" /> is the one path that selects otherwise, and only the write session reaches it.
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
    private readonly IMailAccessTokenSource accessTokenSource;
    private readonly OutboundOperationExecutor operationExecutor;
    private readonly ITransientFailureClassifier transientFailureClassifier;
    private readonly MailAccountId accountId;
    private readonly MailFolderResolution? folder;
    private readonly FolderAccess folderAccess;
    private readonly MailTransportSecurityPolicy transportSecurityPolicy;

    private IImapClient? client;
    private IMailFolder? selectedFolder;
    private ImapUidValidity? sessionUidValidity;

    private MailKitImapConnection(
        Func<IImapClient> clientFactory,
        IImapAccountSettingsProvider settingsProvider,
        IMailAccessTokenSource accessTokenSource,
        OutboundOperationExecutor operationExecutor,
        ITransientFailureClassifier transientFailureClassifier,
        MailAccountId accountId,
        MailFolderResolution? folder,
        FolderAccess folderAccess,
        MailTransportSecurityPolicy transportSecurityPolicy)
    {
        ArgumentNullException.ThrowIfNull(clientFactory);
        ArgumentNullException.ThrowIfNull(settingsProvider);
        ArgumentNullException.ThrowIfNull(accessTokenSource);
        ArgumentNullException.ThrowIfNull(operationExecutor);
        ArgumentNullException.ThrowIfNull(transientFailureClassifier);
        ArgumentNullException.ThrowIfNull(transportSecurityPolicy);

        this.clientFactory = clientFactory;
        this.settingsProvider = settingsProvider;
        this.accessTokenSource = accessTokenSource;
        this.operationExecutor = operationExecutor;
        this.transientFailureClassifier = transientFailureClassifier;
        this.accountId = accountId;
        this.folder = folder;
        this.folderAccess = folderAccess;
        this.transportSecurityPolicy = transportSecurityPolicy;
    }

    /// <summary>Creates a connection that selects one folder read-only and can never write to it.</summary>
    /// <param name="clientFactory">Creates one IMAP client per establishment attempt.</param>
    /// <param name="settingsProvider">Resolves the endpoint and the credential material of the account, per attempt.</param>
    /// <param name="accessTokenSource">Supplies the access token when the account's policy authenticates with one.</param>
    /// <param name="operationExecutor">Runs establishment and retrieval under their configured pipelines.</param>
    /// <param name="transientFailureClassifier">Decides whether a failure left the connection worth keeping.</param>
    /// <param name="accountId">The account this connection belongs to, which also isolates its pipeline state.</param>
    /// <param name="folder">The alias binding every establishment selects read-only.</param>
    /// <param name="transportSecurityPolicy">The connection and authentication policy each attempt must obey.</param>
    /// <returns>A connection that has not been established yet.</returns>
    /// <exception cref="ArgumentNullException">Thrown when a required collaborator is <see langword="null" />.</exception>
    /// <remarks>
    /// This is the connection every read path uses: synchronization, reconciliation, content retrieval, and the
    /// notification session. It selects with <see cref="FolderAccess.ReadOnly" />, which is IMAP <c>EXAMINE</c>
    /// semantics, and <see cref="ExecuteMutationAsync" /> refuses to run on it.
    /// </remarks>
    internal static MailKitImapConnection ForReading(
        Func<IImapClient> clientFactory,
        IImapAccountSettingsProvider settingsProvider,
        IMailAccessTokenSource accessTokenSource,
        OutboundOperationExecutor operationExecutor,
        ITransientFailureClassifier transientFailureClassifier,
        MailAccountId accountId,
        MailFolderResolution folder,
        MailTransportSecurityPolicy transportSecurityPolicy) => new(
            clientFactory,
            settingsProvider,
            accessTokenSource,
            operationExecutor,
            transientFailureClassifier,
            accountId,
            folder,
            FolderAccess.ReadOnly,
            transportSecurityPolicy);

    /// <summary>Creates a connection that selects one folder for writing, for the write session alone.</summary>
    /// <param name="clientFactory">Creates one IMAP client per establishment attempt.</param>
    /// <param name="settingsProvider">Resolves the endpoint and the credential material of the account, per attempt.</param>
    /// <param name="accessTokenSource">Supplies the access token when the account's policy authenticates with one.</param>
    /// <param name="operationExecutor">Runs establishment and mutation under their configured pipelines.</param>
    /// <param name="transientFailureClassifier">Decides whether a failure left the connection worth keeping.</param>
    /// <param name="accountId">The account this connection belongs to, which also isolates its pipeline state.</param>
    /// <param name="folder">The alias binding every establishment selects for writing.</param>
    /// <param name="transportSecurityPolicy">The connection and authentication policy each attempt must obey.</param>
    /// <returns>A connection that has not been established yet.</returns>
    /// <exception cref="ArgumentNullException">Thrown when a required collaborator is <see langword="null" />.</exception>
    /// <remarks>
    /// A connection created here selects with <see cref="FolderAccess.ReadWrite" />, which is IMAP <c>SELECT</c>
    /// semantics, and is the only kind <see cref="ExecuteMutationAsync" /> will run on. It is reached from exactly one
    /// place — the write session's own factory — and an account holds at most one of them at a time.
    /// </remarks>
    internal static MailKitImapConnection ForWriting(
        Func<IImapClient> clientFactory,
        IImapAccountSettingsProvider settingsProvider,
        IMailAccessTokenSource accessTokenSource,
        OutboundOperationExecutor operationExecutor,
        ITransientFailureClassifier transientFailureClassifier,
        MailAccountId accountId,
        MailFolderResolution folder,
        MailTransportSecurityPolicy transportSecurityPolicy) => new(
            clientFactory,
            settingsProvider,
            accessTokenSource,
            operationExecutor,
            transientFailureClassifier,
            accountId,
            folder,
            FolderAccess.ReadWrite,
            transportSecurityPolicy);

    /// <summary>Creates a connection that selects no folder at all, which is what folder discovery needs.</summary>
    /// <param name="clientFactory">Creates one IMAP client per establishment attempt.</param>
    /// <param name="settingsProvider">Resolves the endpoint and the credential material of the account, per attempt.</param>
    /// <param name="accessTokenSource">Supplies the access token when the account's policy authenticates with one.</param>
    /// <param name="operationExecutor">Runs establishment and retrieval under their configured pipelines.</param>
    /// <param name="transientFailureClassifier">Decides whether a failure left the connection worth keeping.</param>
    /// <param name="accountId">The account this connection belongs to, which also isolates its pipeline state.</param>
    /// <param name="transportSecurityPolicy">The connection and authentication policy each attempt must obey.</param>
    /// <returns>A connection that has not been established yet.</returns>
    /// <exception cref="ArgumentNullException">Thrown when a required collaborator is <see langword="null" />.</exception>
    /// <remarks>An IMAP <c>LIST</c> selects nothing, so there is no folder to pin and no message whose flags could change.</remarks>
    internal static MailKitImapConnection ForFolderDiscovery(
        Func<IImapClient> clientFactory,
        IImapAccountSettingsProvider settingsProvider,
        IMailAccessTokenSource accessTokenSource,
        OutboundOperationExecutor operationExecutor,
        ITransientFailureClassifier transientFailureClassifier,
        MailAccountId accountId,
        MailTransportSecurityPolicy transportSecurityPolicy) => new(
            clientFactory,
            settingsProvider,
            accessTokenSource,
            operationExecutor,
            transientFailureClassifier,
            accountId,
            folder: null,
            FolderAccess.ReadOnly,
            transportSecurityPolicy);

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
    /// <para>
    /// Every attempt starts by making sure a folder is selected, and an attempt that failed on something worth
    /// repeating hands the next one a connection to rebuild rather than the one it was just failing on.
    /// </para>
    /// <para>
    /// The read receives the client as well as the folder because a capability belongs to the connection an attempt is
    /// actually running on. A read that chose its protocol from a capability captured when the session opened would
    /// keep using it after a recovered connection landed on a server advertising something else.
    /// </para>
    /// </remarks>
    internal Task<TResult> ExecuteFolderReadAsync<TResult>(
        Func<IImapClient, IMailFolder, CancellationToken, Task<TResult>> read,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(read);

        return this.ExecuteUnderPipelineAsync(
            OutboundDependency.MailboxDataRetrieval,
            async attemptToken =>
            {
                var attemptClient = await this.EnsureAuthenticatedClientAsync(attemptToken);
                var openFolder = await this.EnsureOpenFolderAsync(attemptToken);

                return await this.AttemptRepeatableReadAsync(() => read(attemptClient, openFolder, attemptToken));
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

    /// <summary>Runs one operation against the selected folder exactly once, under no retry of its own.</summary>
    /// <typeparam name="TResult">The result the operation produces.</typeparam>
    /// <param name="operation">The operation, which is never repeated.</param>
    /// <param name="cancellationToken">Cancels establishing the session and the operation itself.</param>
    /// <returns>The result of the single attempt.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="operation" /> is <see langword="null" />.</exception>
    /// <exception cref="MailboxUnavailableException">Thrown when establishment stopped at a configured limit, or when the operation failed on something transient.</exception>
    /// <exception cref="MailboxFolderRecreatedException">Thrown when a recovered connection reselected the folder with a different UIDVALIDITY.</exception>
    /// <remarks>
    /// <para>
    /// Two unrelated operations need exactly one attempt, for opposite reasons, and both arrive here. A long wait for
    /// the server to say something is one: the retrieval pipeline's per-attempt timeout is measured in seconds because
    /// a read that stops answering is broken, while a wait that answers nothing for twenty minutes is a wait behaving
    /// exactly as designed — running it there would abandon it at every attempt boundary and spend the whole budget in
    /// under a minute. A mutation is the other, and it reaches this method only through
    /// <see cref="ExecuteMutationAsync" />, because repeating a change is not the same as repeating a question.
    /// </para>
    /// <para>
    /// Establishment still runs under its own pipeline, so a dropped connection is rebuilt before the operation begins
    /// and a rejected credential is still never repeated. What is given up is only the repetition of the operation
    /// itself, which its caller owns instead: a transient failure is reported as an unavailable mailbox and the
    /// connection is discarded, so the next call starts from a session it established itself.
    /// </para>
    /// </remarks>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Every failure is inspected for whether it left the connection usable and is then rethrown, translated only where a mail-library type would otherwise cross an application port.")]
    internal async Task<TResult> ExecuteUnrepeatedFolderOperationAsync<TResult>(
        Func<IImapClient, IMailFolder, CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);

        var authenticatedClient = await this.EnsureAuthenticatedClientAsync(cancellationToken);
        var openFolder = await this.EnsureOpenFolderAsync(cancellationToken);

        try
        {
            return await operation(authenticatedClient, openFolder, cancellationToken);
        }
        catch (Exception failure) when (this.IsRepeatableFailure(OutboundDependency.MailboxDataRetrieval, failure))
        {
            this.DiscardUnusableConnection();

            throw this.MailboxDidNotServeTheOperation(failure);
        }
        catch (Exception failure)
        {
            this.DiscardConnectionUnlessItSurvived(failure);

            throw;
        }
    }

    /// <summary>Runs one operation that changes the mailbox, and only on a connection created to be able to.</summary>
    /// <typeparam name="TResult">The result the mutation produces.</typeparam>
    /// <param name="mutation">The change, which is issued exactly once.</param>
    /// <param name="cancellationToken">Cancels establishing the session and the mutation itself.</param>
    /// <returns>The result of the single attempt.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="mutation" /> is <see langword="null" />.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the connection was created for reading, so it selects folders read-only and can change nothing.</exception>
    /// <exception cref="MailboxUnavailableException">Thrown when establishment stopped at a configured limit, or when the mutation failed on something transient.</exception>
    /// <exception cref="MailboxFolderRecreatedException">Thrown when a recovered connection reselected the folder with a different UIDVALIDITY.</exception>
    /// <remarks>
    /// <para>
    /// The guard is the point of the method. Every read path in MailFathom holds a connection from
    /// <see cref="ForReading" />, and asking one of those to change something fails here rather than reaching a server
    /// that would answer it — a folder selected read-only would refuse the command anyway, but a refusal that arrives
    /// as an IMAP error is a bug discovered in production, and this one is a bug discovered on the first test that
    /// makes the mistake.
    /// </para>
    /// <para>
    /// A change is never repeated on the caller's behalf. A <c>COPY</c> issued twice is a second message rather than a
    /// repeat of the first, and a failure that leaves the outcome unknown is exactly the case a caller has to
    /// reconcile against the server instead of guessing at — so this runs the mutation once and reports a transient
    /// failure as an unavailable mailbox, discarding the connection so the next call establishes its own.
    /// </para>
    /// </remarks>
    internal Task<TResult> ExecuteMutationAsync<TResult>(
        Func<IImapClient, IMailFolder, CancellationToken, Task<TResult>> mutation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(mutation);

        if (this.folderAccess is not FolderAccess.ReadWrite)
        {
            throw new InvalidOperationException(
                "This connection selects its folder read-only and cannot change the mailbox.");
        }

        return this.ExecuteUnrepeatedFolderOperationAsync(mutation, cancellationToken);
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

                await this.AuthenticateAsync(attemptClient, settings, cancellationToken);

                await EnableQuickResyncWhenAdvertisedAsync(attemptClient, cancellationToken);

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

    /// <summary>Turns on quick resynchronization where the server offers it, before any folder has been selected.</summary>
    /// <remarks>
    /// <para>
    /// RFC 7162 makes <c>ENABLE QRESYNC</c> a per-connection decision that has to be taken before the first selection,
    /// which is why it sits here rather than in whichever read first wants a vanished report. A connection that missed
    /// this point cannot enable it later without being rebuilt.
    /// </para>
    /// <para>
    /// It changes what every folder on this connection reports about a removed message: MailKit raises
    /// <c>MessagesVanished</c> in place of <c>MessageExpunged</c> once the feature is on. Anything on this connection
    /// that watches for a removal therefore has to watch both, and a watcher listening only for the older event would
    /// silently stop noticing deletions the moment a server started advertising the capability.
    /// </para>
    /// </remarks>
    private static async Task EnableQuickResyncWhenAdvertisedAsync(
        IImapClient attemptClient,
        CancellationToken cancellationToken)
    {
        if (!attemptClient.Capabilities.HasFlag(ImapCapabilities.QuickResync))
        {
            return;
        }

        await attemptClient.EnableQuickResyncAsync(cancellationToken);
    }

    /// <summary>Narrows the advertised mechanisms to the allow-list and authenticates with whichever credential the survivors need.</summary>
    /// <remarks>
    /// The advertised set is narrowed first, because MailKit selects a mechanism from whatever remains in it, and
    /// nothing widens it again: retrying against a wider set would let the server negotiate a mechanism the operator's
    /// allow-list refused.
    /// </remarks>
    private async Task AuthenticateAsync(
        IImapClient attemptClient,
        ImapAccountSettings settings,
        CancellationToken cancellationToken)
    {
        MailKitTransportSecurityMapping.RestrictAdvertisedMechanisms(
            attemptClient.AuthenticationMechanisms,
            this.transportSecurityPolicy.Authentication,
            settings.AccountId);

        if (MailKitTransportSecurityMapping.TrySelectAccessTokenMechanism(
            attemptClient.AuthenticationMechanisms,
            this.transportSecurityPolicy.Authentication,
            out var tokenMechanism))
        {
            await this.AuthenticateWithAccessTokenAsync(attemptClient, settings, tokenMechanism, cancellationToken);

            return;
        }

        // Startup validation refuses an account whose policy needs a password and configures none, so this is a
        // configured shape rather than a value that might be missing.
        var password = settings.Material.Password
            ?? throw new InvalidOperationException(
                $"Account '{settings.AccountId}' authenticates with a password and resolved none.");

        // MailKit's authentication contract takes a string, so an un-erasable copy of the password is unavoidable
        // here. It is created at the call itself and never stored, logged, or passed on.
        await attemptClient.AuthenticateAsync(settings.UserName, password.RevealAsString(), cancellationToken);
    }

    /// <summary>Presents an access token, renewing it once when the server rejects one this process believed was valid.</summary>
    /// <remarks>
    /// <para>
    /// The single retry is what separates an expired token from a rejected credential. A cached token can be refused
    /// for reasons no expiry instant predicts — it was revoked, the mailbox password changed, an administrator
    /// withdrew consent — and repeating the same value would spend the establishment budget on an answer that cannot
    /// change. Renewing once distinguishes the two: a fresh token that is also refused is a decision the authorization
    /// server and the mail server agree on, and it fails the attempt.
    /// </para>
    /// <para>
    /// This is bounded and non-recursive by construction. There is exactly one renewal per establishment attempt, and
    /// the token source has its own retry budget under a different dependency class, so no retry layer nests inside
    /// another.
    /// </para>
    /// </remarks>
    private async Task AuthenticateWithAccessTokenAsync(
        IImapClient attemptClient,
        ImapAccountSettings settings,
        MailAuthenticationMechanism tokenMechanism,
        CancellationToken cancellationToken)
    {
        var accessToken = await this.accessTokenSource.GetAccessTokenAsync(settings.AccountId, cancellationToken);

        try
        {
            await attemptClient.AuthenticateAsync(
                MailKitTransportSecurityMapping.ToSaslMechanism(tokenMechanism, settings.UserName, accessToken.Value),
                cancellationToken);
        }
        catch (global::MailKit.Security.AuthenticationException)
        {
            var renewedToken = await this.accessTokenSource.RenewAccessTokenAsync(
                settings.AccountId,
                accessToken,
                cancellationToken);

            await attemptClient.AuthenticateAsync(
                MailKitTransportSecurityMapping.ToSaslMechanism(tokenMechanism, settings.UserName, renewedToken.Value),
                cancellationToken);
        }
    }

    /// <summary>Selects the pinned folder with this connection's fixed access, once it is confirmed to be the one the session started on.</summary>
    /// <remarks>
    /// A server answers a reselection with its current UIDVALIDITY, and a changed one means the UIDs already handed
    /// out name different emails now. Adopting such a folder would attach the recovered folder's emails to the
    /// previous folder's checkpoint, so the session refuses it and lets the next run start the folder over. A
    /// connection opened for discovery pins no folder and selects nothing here.
    /// <para>
    /// The access is the one fixed when the connection was created, so a recovered connection reselects the folder
    /// exactly as the original selection did. A read connection cannot acquire the ability to write by losing its
    /// socket.
    /// </para>
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
        await openedFolder.OpenAsync(this.folderAccess, cancellationToken);

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
    /// port. A terminal failure — a rejected credential, a refused command — is the operator's to see and passes
    /// through untouched, as does the caller's own cancellation. An oversized payload reaches no failure path at all:
    /// the session reports it as a <see cref="RemoteEmailContentFetchResult" /> outcome, so it returns through this
    /// method as an ordinary result and never meets the retry or translation branches below.
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
