// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using System.Net.Sockets;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using MailFathom.Application.Mail.Delivery;
using MailFathom.Application.Mail.Delivery.Transmission;
using MailFathom.Application.Resilience;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Transport;
using MailFathom.Infrastructure.Mail.OAuth;
using MailFathom.Infrastructure.Resilience;
using MailKit.Net.Smtp;
using Microsoft.Extensions.Logging;
using MimeKit;

namespace MailFathom.Infrastructure.Mail.MailKit.Delivery;

/// <summary>Keeps one account authenticated against its submission server for as long as a delivery needs it.</summary>
/// <remarks>
/// <para>
/// It is established once and never re-established. A mailbox connection rebuilds itself because a read costs nothing
/// to repeat; a submission that fails after the message data cannot be told apart from one that succeeded, so a
/// connection that dies takes its session with it and whatever recorded the delivery decides what happens next.
/// </para>
/// <para>
/// Establishment runs under the delivery dependency class, which is the same budget a submission spends. The class
/// repeats only a server's explicit temporary rejection, so a connection dropped mid-handshake ends the attempt rather
/// than being retried here — the conservative reading, and the one that keeps a retry above this from ever being
/// nested inside one below it.
/// </para>
/// <para>
/// Each stage of reaching the server carries a budget of its own, inside the attempt budget of that class. A stage that
/// expires is a <see cref="TimeoutException" /> naming the stage, which is deliberately not the caller's
/// <see cref="OperationCanceledException" />: a worker has to tell a submission endpoint that stopped answering from a
/// host that is shutting down.
/// </para>
/// <para>
/// One connection is used by one caller at a time. Nothing here is safe for concurrent use.
/// </para>
/// </remarks>
internal sealed partial class MailKitSmtpConnection : IAsyncDisposable
{
    /// <summary>Names the delivery session in resilience telemetry, where a connection has no folder to name instead.</summary>
    private const string DeliverySessionOperationKey = "delivery-session";

    private readonly Func<ISubmissionClient> clientFactory;
    private readonly Func<string, int, CancellationToken, Task<Socket>> socketConnector;
    private readonly ISmtpAccountSettingsProvider settingsProvider;
    private readonly IMailAccessTokenSource accessTokenSource;
    private readonly OutboundOperationExecutor operationExecutor;
    private readonly ITransientFailureClassifier transientFailureClassifier;
    private readonly MailAccountId accountId;
    private readonly MailTransportSecurityPolicy transportSecurityPolicy;
    private readonly MailDeliveryTimeouts timeouts;
    private readonly TimeProvider timeProvider;
    private readonly ILogger<MailKitSmtpConnection> logger;

    private ISubmissionClient? client;

    /// <summary>Creates a connection that has not reached its server yet.</summary>
    /// <param name="clientFactory">Creates the SMTP client the session is spoken over.</param>
    /// <param name="socketConnector">Opens the transport the client then greets the server across.</param>
    /// <param name="settingsProvider">Resolves the endpoint and the credential material of the account.</param>
    /// <param name="accessTokenSource">Supplies the access token when the account's policy authenticates with one.</param>
    /// <param name="operationExecutor">Runs establishment under the delivery pipeline.</param>
    /// <param name="transientFailureClassifier">Decides whether a spent budget stopped a failure worth repeating.</param>
    /// <param name="accountId">The account this connection belongs to, which also isolates its pipeline state.</param>
    /// <param name="transportSecurityPolicy">The connection and authentication policy the attempt must obey.</param>
    /// <param name="timeouts">The budget each stage of reaching the server is given.</param>
    /// <param name="timeProvider">Measures those budgets.</param>
    /// <param name="logger">Records what a refusal was, as codes rather than as the server's own words.</param>
    /// <exception cref="ArgumentNullException">Thrown when a required collaborator is <see langword="null" />.</exception>
    internal MailKitSmtpConnection(
        Func<ISubmissionClient> clientFactory,
        Func<string, int, CancellationToken, Task<Socket>> socketConnector,
        ISmtpAccountSettingsProvider settingsProvider,
        IMailAccessTokenSource accessTokenSource,
        OutboundOperationExecutor operationExecutor,
        ITransientFailureClassifier transientFailureClassifier,
        MailAccountId accountId,
        MailTransportSecurityPolicy transportSecurityPolicy,
        MailDeliveryTimeouts timeouts,
        TimeProvider timeProvider,
        ILogger<MailKitSmtpConnection> logger)
    {
        ArgumentNullException.ThrowIfNull(clientFactory);
        ArgumentNullException.ThrowIfNull(socketConnector);
        ArgumentNullException.ThrowIfNull(settingsProvider);
        ArgumentNullException.ThrowIfNull(accessTokenSource);
        ArgumentNullException.ThrowIfNull(operationExecutor);
        ArgumentNullException.ThrowIfNull(transientFailureClassifier);
        ArgumentNullException.ThrowIfNull(transportSecurityPolicy);
        ArgumentNullException.ThrowIfNull(timeouts);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        this.clientFactory = clientFactory;
        this.socketConnector = socketConnector;
        this.settingsProvider = settingsProvider;
        this.accessTokenSource = accessTokenSource;
        this.operationExecutor = operationExecutor;
        this.transientFailureClassifier = transientFailureClassifier;
        this.accountId = accountId;
        this.transportSecurityPolicy = transportSecurityPolicy;
        this.timeouts = timeouts;
        this.timeProvider = timeProvider;
        this.logger = logger;
    }

    /// <summary>Gets what the server declared it will accept, read from the greeting this connection was opened with.</summary>
    /// <exception cref="InvalidOperationException">Thrown before the connection has been established.</exception>
    internal MailDeliveryCapabilities Capabilities
    {
        get => field ?? throw new InvalidOperationException("The delivery connection has not been established yet.");
        private set;
    }

    /// <summary>Connects, authenticates, and reads what the server advertised.</summary>
    /// <param name="cancellationToken">Cancels resolving the credential, connecting, and authenticating.</param>
    /// <returns>A task that completes when the account is authenticated against its submission server.</returns>
    /// <exception cref="MailDeliveryUnavailableException">Thrown when the delivery pipeline stopped the attempt at a configured limit.</exception>
    /// <exception cref="MailAuthenticationMechanismUnavailableException">Thrown when the server advertises no mechanism the account's policy permits.</exception>
    internal async Task EstablishAsync(CancellationToken cancellationToken)
    {
        try
        {
            await this.operationExecutor.ExecuteAsync(
                new OutboundPipelineKey(OutboundDependency.EmailDelivery, this.accountId.Value),
                DeliverySessionOperationKey,
                this.ConnectAuthenticateAndReadCapabilitiesAsync,
                cancellationToken);
        }
        catch (OutboundDependencyUnavailableException rejection)
        {
            throw new MailDeliveryUnavailableException(this.accountId, rejection);
        }
        catch (Exception exhaustedFailure) when (this.transientFailureClassifier.IsTransientFailure(
            OutboundDependency.EmailDelivery,
            exhaustedFailure))
        {
            throw new MailDeliveryUnavailableException(this.accountId, exhaustedFailure);
        }
    }

    /// <summary>Offers the envelope and transmits the message over the established session.</summary>
    /// <param name="request">Who the message is from, who is still owed it, and the bytes to transmit.</param>
    /// <param name="envelope">Filled with what the server answers about each address, whether or not this call returns.</param>
    /// <param name="cancellationToken">Cancels the submission.</param>
    /// <returns>What the server answered about the message.</returns>
    /// <exception cref="InvalidOperationException">Thrown before the connection has been established.</exception>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="request" /> or <paramref name="envelope" /> is <see langword="null" />.</exception>
    /// <exception cref="MailDeliveryUnavailableException">Thrown when the exchange failed without the server stating anything.</exception>
    /// <exception cref="TimeoutException">Thrown when the submission outlived its budget, which says nothing about what the server received.</exception>
    /// <remarks>
    /// <para>
    /// It runs outside the delivery resilience pipeline, alone among the operations of this connection, and that is the
    /// single most important line in this type. The pipeline repeats a submission server's explicit temporary
    /// rejection, which is right for establishing a session and catastrophic here: the same message offered twice is a
    /// second copy in the mailbox of everybody the first envelope reached. One attempt reaches the server once, and
    /// when the next attempt happens is written onto the durable record instead.
    /// </para>
    /// <para>
    /// The stored bytes are parsed back into a message because the library submits messages rather than octets. What
    /// they are not is recomposed: the headers, the identity, and the encoding are the ones the composer wrote, so the
    /// message a retry transmits is the one an earlier attempt may already have begun transmitting.
    /// </para>
    /// <para>
    /// The blind recipients are hidden again on the way out. The stored bytes already omit them, and asking for it a
    /// second time costs nothing and means no stored message can put a blind address into the copy every other
    /// recipient reads.
    /// </para>
    /// </remarks>
    internal async Task<MailTransmission> TransmitAsync(
        MailTransmissionRequest request,
        MailEnvelopeLedger envelope,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(envelope);

        var submissionClient = this.client
            ?? throw new InvalidOperationException("The delivery connection has not been established yet.");

        using var storedMime = AsStream(request.RawMime);
        using var message = await MimeMessage.LoadAsync(storedMime, cancellationToken);

        var sender = new MailboxAddress(name: null, request.Sender.Address);
        var recipients = request.Recipients
            .Select(recipient => new MailboxAddress(name: null, recipient.Address.Address))
            .ToArray();

        submissionClient.Envelope = new SmtpEnvelopeObserver(request.Recipients, envelope);

        try
        {
            await this.RunPhaseAsync(
                MailDeliveryPhase.Transmission,
                phaseToken => submissionClient.SendAsync(
                    FormatFor(request),
                    message,
                    sender,
                    recipients,
                    phaseToken),
                cancellationToken);

            return new MailTransmission(MailTransmissionOutcome.Accepted, ReplyCode: null);
        }
        catch (SmtpNoRecipientsAcceptedException)
        {
            this.LogSubmissionEnvelopeRefused(this.accountId.Value, envelope.Replies.Count);

            return new MailTransmission(OutcomeOf(envelope), ReplyCode: null);
        }
        catch (SmtpCommandException refusal)
        {
            this.ReportRefusal(refusal);

            var classification = SmtpReplyClassifier.Classify(refusal);

            return new MailTransmission(
                classification.Disposition == SmtpRejectionDisposition.Transient
                    ? MailTransmissionOutcome.RefusedTemporarily
                    : MailTransmissionOutcome.RefusedPermanently,
                classification.ReplyCode);
        }
        catch (Exception failure) when (failure is SmtpProtocolException or IOException or SocketException)
        {
            // The server stated nothing, so nothing here may say what the recipients received. The record the caller
            // holds decides that, from the envelope this exchange had already settled.
            throw new MailDeliveryUnavailableException(this.accountId, failure);
        }
        finally
        {
            submissionClient.Envelope = null;
        }
    }

    /// <summary>Closes and releases the connection, reporting the first cleanup failure.</summary>
    public async ValueTask DisposeAsync()
    {
        var ownedClient = this.client;
        this.client = null;

        if (ownedClient is not null)
        {
            await DisconnectAndDisposeAsync(ownedClient);
        }
    }

    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The socket's ownership passes to the client once the greeting succeeds, and the client is abandoned with it on every failure path; a socket the client never took is disposed where the greeting fails.")]
    private async Task<ISubmissionClient> ConnectAuthenticateAndReadCapabilitiesAsync(CancellationToken cancellationToken)
    {
        var settings = await this.settingsProvider.GetSettingsAsync(this.accountId.Value, cancellationToken);

        // The resolved material is owned by this attempt and released when it ends, whether it succeeded or not, so the
        // password exists for one attempt rather than for the lifetime of the process.
        using (settings.Material)
        {
            var attemptClient = this.clientFactory();
            try
            {
                attemptClient.Timeout = (int)this.timeouts.Command.TotalMilliseconds;

                TrustConfiguredCertificateAuthority(attemptClient, settings.Material.TrustedCertificateAuthority);

                var socket = await this.RunPhaseAsync(
                    MailDeliveryPhase.Connection,
                    phaseToken => this.socketConnector(settings.Host, settings.Port, phaseToken),
                    cancellationToken);

                await this.GreetOverAsync(attemptClient, socket, settings, cancellationToken);

                await this.RunPhaseAsync(
                    MailDeliveryPhase.Authentication,
                    phaseToken => this.AuthenticateAsync(attemptClient, settings, phaseToken),
                    cancellationToken);

                // Read before the client is adopted, so a failure here still leaves this connection holding nothing and
                // the abandonment below is the only thing that closes the client.
                this.Capabilities = attemptClient.ToDeliveryCapabilities();
                this.client = attemptClient;

                return attemptClient;
            }
            catch (SmtpCommandException refusal)
            {
                this.ReportRefusal(refusal);
                Abandon(attemptClient);

                throw;
            }
            catch
            {
                // A half-established connection is unusable by definition, and this cleanup runs inside an attempt the
                // pipeline may abandon, so it closes the socket rather than waiting on a reply the server owes it.
                Abandon(attemptClient);

                throw;
            }
        }
    }

    /// <summary>Negotiates encryption, reads the greeting, and exchanges capabilities over an already open transport.</summary>
    /// <remarks>
    /// The socket is disposed here only where the client never took it. A successful greeting hands ownership to the
    /// client, which closes it when the connection is abandoned or disposed.
    /// </remarks>
    private async Task GreetOverAsync(
        ISmtpClient attemptClient,
        Socket socket,
        SmtpAccountSettings settings,
        CancellationToken cancellationToken)
    {
        try
        {
            await this.RunPhaseAsync(
                MailDeliveryPhase.Greeting,
                phaseToken => attemptClient.ConnectAsync(
                    socket,
                    settings.Host,
                    settings.Port,
                    this.transportSecurityPolicy.ConnectionSecurity.ToSecureSocketOptions(),
                    phaseToken),
                cancellationToken);
        }
        catch
        {
            socket.Dispose();

            throw;
        }
    }

    /// <summary>Narrows the advertised mechanisms to the allow-list and authenticates with whichever credential the survivors need.</summary>
    /// <remarks>
    /// The advertised set is narrowed first, because MailKit selects a mechanism from whatever remains in it, and
    /// nothing widens it again. A submission server that offers nothing the account permits ends the attempt here with
    /// the account's own coded failure, since SMTP has no clear-text command to fall back to.
    /// </remarks>
    private async Task AuthenticateAsync(
        ISmtpClient attemptClient,
        SmtpAccountSettings settings,
        CancellationToken cancellationToken)
    {
        MailKitTransportSecurityMapping.RestrictAdvertisedSubmissionMechanisms(
            attemptClient.AuthenticationMechanisms,
            this.transportSecurityPolicy.Authentication,
            settings.AccountId);

        if (MailKitTransportSecurityMapping.TrySelectAccessTokenMechanism(
            attemptClient.AuthenticationMechanisms,
            this.transportSecurityPolicy.Authentication,
            out var tokenMechanism))
        {
            var accessToken = await this.accessTokenSource.GetAccessTokenAsync(settings.AccountId, cancellationToken);

            await attemptClient.AuthenticateAsync(
                MailKitTransportSecurityMapping.ToSaslMechanism(tokenMechanism, settings.UserName, accessToken.Value),
                cancellationToken);

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

    /// <summary>Runs one stage of reaching the server under a budget of its own.</summary>
    /// <remarks>
    /// A stage that outlives its budget is reported as a timeout naming the stage, while the caller's own cancellation
    /// passes through as itself. The two are told apart by which token was cancelled rather than by the exception,
    /// because the linked source reports both as the same one.
    /// </remarks>
    private async Task<TResult> RunPhaseAsync<TResult>(
        MailDeliveryPhase phase,
        Func<CancellationToken, Task<TResult>> phaseOperation,
        CancellationToken cancellationToken)
    {
        using var phaseBudget = new CancellationTokenSource(this.timeouts.For(phase), this.timeProvider);
        using var stageToken = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, phaseBudget.Token);

        try
        {
            return await phaseOperation(stageToken.Token);
        }
        catch (OperationCanceledException) when (phaseBudget.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"The submission server for {this.accountId.Value} did not complete the {phase} stage within its budget.");
        }
    }

    /// <summary>Runs one stage that produces no result under a budget of its own.</summary>
    private async Task RunPhaseAsync(
        MailDeliveryPhase phase,
        Func<CancellationToken, Task> phaseOperation,
        CancellationToken cancellationToken) =>
        await this.RunPhaseAsync<object?>(
            phase,
            async phaseToken =>
            {
                await phaseOperation(phaseToken);

                return null;
            },
            cancellationToken);

    /// <summary>Reads an envelope that accepted nobody as the refusal it amounts to.</summary>
    /// <remarks>
    /// One address a server refused for now is worth returning for, so a mixed answer is temporary; an envelope
    /// refused for good throughout is settled and nothing offers the message again. An envelope with no reply at all
    /// is treated as settled for the reason every unrecognized refusal is.
    /// </remarks>
    private static MailTransmissionOutcome OutcomeOf(MailEnvelopeLedger envelope) =>
        envelope.Replies.Any(reply => reply.Acceptance == MailRecipientAcceptance.RefusedTemporarily)
            ? MailTransmissionOutcome.RefusedTemporarily
            : MailTransmissionOutcome.RefusedPermanently;

    /// <summary>States how the stored message is written onto the wire.</summary>
    /// <remarks>
    /// The line ending is the one SMTP requires rather than the platform's, and the international format is asked for
    /// only where an address needs it — a message whose addresses are all ASCII is written the way every server
    /// understands. The composer already refused an internationalized address the server cannot carry, so this is the
    /// same decision restated over the addresses this attempt is actually offering.
    /// </remarks>
    private static FormatOptions FormatFor(MailTransmissionRequest request)
    {
        var format = FormatOptions.Default.Clone();

        format.NewLineFormat = NewLineFormat.Dos;
        format.EnsureNewLine = true;
        format.International = !Ascii.IsValid(request.Sender.Address)
            || request.Recipients.Any(recipient => !Ascii.IsValid(recipient.Address.Address));
        format.HiddenHeaders.Add(HeaderId.Bcc);
        format.HiddenHeaders.Add(HeaderId.ResentBcc);

        return format;
    }

    /// <summary>Reads the stored bytes without copying them where the buffer allows it.</summary>
    /// <remarks>
    /// A composed message runs to the deployment's whole size bound, so a copy per attempt would be a large-object
    /// allocation for every send and every retry of one. The fallback exists because a memory that is not array-backed
    /// has no other way to be read as a stream.
    /// </remarks>
    private static MemoryStream AsStream(ReadOnlyMemory<byte> rawMime) =>
        MemoryMarshal.TryGetArray(rawMime, out var segment) && segment.Array is not null
            ? new MemoryStream(segment.Array, segment.Offset, segment.Count, writable: false)
            : new MemoryStream(rawMime.ToArray(), writable: false);

    /// <summary>Records what the server refused with, as the numbers it stated rather than the sentence it wrote.</summary>
    private void ReportRefusal(SmtpCommandException refusal)
    {
        var classification = SmtpReplyClassifier.Classify(refusal);

        this.LogSubmissionRefused(
            this.accountId.Value,
            classification.ReplyCode,
            classification.EnhancedStatusCode?.ToString() ?? "none",
            classification.Disposition.ToString());
    }

    /// <summary>Points the client at the account's configured authority before the handshake that will consult it.</summary>
    /// <remarks>
    /// The anchor lives as long as the connection attempt that resolved it, and so does the callback that closes over
    /// it: the client is created per attempt and disposed with it, so no callback outlives the certificate it reads.
    /// An account without a configured authority leaves the client's own validating default untouched.
    /// </remarks>
    private static void TrustConfiguredCertificateAuthority(
        ISmtpClient client,
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

    /// <summary>Drops a client this type has already declared unusable, without speaking the protocol again.</summary>
    /// <remarks>
    /// A graceful quit is a command, and a command sent to a server that stopped answering waits for a reply that may
    /// never come, through a cancellation token this cleanup has no way to observe. Closing the socket asks the server
    /// for nothing. Politeness belongs to <see cref="DisposeAsync" />, where the session is ending in order.
    /// </remarks>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "A connection already being abandoned must not have its cleanup failure replace the failure that caused the abandonment.")]
    [SuppressMessage("Roslynator", "RCS1075:Avoid empty catch clause that catches System.Exception", Justification = "There is no second action to take: the connection is already unusable, and the caller is about to rethrow the failure that made it so.")]
    private static void Abandon(ISmtpClient client)
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
    private static async ValueTask DisconnectAndDisposeAsync(ISmtpClient client)
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

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "The submission server for account {AccountId} accepted none of the {AnsweredRecipientCount} recipient(s) offered, so the message was not transmitted. What it said about each is on the send's own record.")]
    private partial void LogSubmissionEnvelopeRefused(string accountId, int answeredRecipientCount);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "The submission server for account {AccountId} refused a command [reply {ReplyCode}, enhanced status {EnhancedStatusCode}, {Disposition}].")]
    private partial void LogSubmissionRefused(
        string accountId,
        int replyCode,
        string enhancedStatusCode,
        string disposition);
}
