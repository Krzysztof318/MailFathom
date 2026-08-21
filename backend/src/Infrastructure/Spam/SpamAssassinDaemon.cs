// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Buffers;
using System.Globalization;
using System.Net.Sockets;
using System.Text;
using MailFathom.CodeCoverage;

namespace MailFathom.Infrastructure.Spam;

/// <summary>Speaks the spam daemon's line protocol, under this deployment's bounds.</summary>
/// <remarks>
/// <para>
/// One connection per exchange, which is the protocol rather than a choice: the daemon serves one command per
/// connection and closes its side when it has answered. Nothing is pooled, so there is no handler chain to grow stale
/// and no socket held open across a scan that never came.
/// </para>
/// <para>
/// The three bounds all live here so that no caller can be the one that forgot: the concurrency permit is taken before
/// a socket is opened and inside the same budget as the exchange it guards, the timeout covers all of that rather than
/// any single read, and the answer is read into a buffer that refuses to grow past what an answer can be. A daemon that
/// stopped answering therefore costs one timeout rather than a stalled classification run, and so does a saturated one.
/// </para>
/// <para>
/// Caller cancellation and this adapter's own timeout are deliberately different outcomes. The first propagates, because
/// it is a fact about the caller; the second is reported as a timeout, because it is a fact about the daemon and the
/// classification continues past it with its deterministic verdict.
/// </para>
/// <para>
/// Everything this type does is a socket, so nothing here is reachable from a unit test at all: the bounds are enforced
/// around an exchange rather than computed, and what proves them is a real daemon reached over a real connection. The
/// answer's own reading lives in <see cref="SpamdReply" />, which is pure and unit-tested as such.
/// </para>
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class SpamAssassinDaemon : IDisposable
{
    /// <summary>Scores a message and answers with the score, the threshold, and the names of the rules that fired.</summary>
    /// <remarks>
    /// The longer of the two scoring commands, and the reason to pay for it is provenance: the shorter one answers the
    /// verdict and the score, and a record that stated a score with nothing behind it would be a number nobody could
    /// argue with.
    /// </remarks>
    public const string SymbolsCommand = "SYMBOLS";

    /// <summary>Scores a message and answers with the headers the daemon would have written onto it.</summary>
    /// <remarks>Issued once, while the host starts, because those headers are the only place the daemon names its own release.</remarks>
    public const string HeadersCommand = "HEADERS";

    /// <summary>The protocol version every request states.</summary>
    /// <remarks>
    /// The version that defines every command issued here. A daemon speaking an older one answers with its own version
    /// in the status line, which is read rather than compared: the two commands used here have been in the protocol
    /// since well before it, and refusing an older daemon would refuse one that answers correctly.
    /// </remarks>
    private const string ProtocolVersion = "1.5";

    /// <summary>The greatest answer that is read before the daemon is treated as not answering usably.</summary>
    /// <remarks>
    /// A symbol list is a few hundred bytes and a rewritten header block a few thousand. The bound is generous against
    /// both and small enough that whatever else might be listening on a mistyped port cannot stream into this process.
    /// </remarks>
    private const int MaximumAnswerBytes = 64 * 1024;

    private readonly SpamAssassinScannerProfile profile;
    private readonly SemaphoreSlim exchangePermits;

    /// <summary>The corpus identity, established once and read by every scan afterwards.</summary>
    /// <remarks>
    /// Written by the startup probe before any scan runs and never written again, so a reader sees either nothing or the
    /// one value. A scan that finds nothing establishes it for itself, which happens only where the probe did not run
    /// first; two scans racing to do so agree, because they are asking the same daemon the same question.
    /// </remarks>
    private volatile string? corpusRevision;

    /// <summary>Initializes the conversation with one configured daemon.</summary>
    /// <param name="profile">Where the daemon is and what a call to it may cost.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="profile" /> is <see langword="null" />.</exception>
    public SpamAssassinDaemon(SpamAssassinScannerProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        this.profile = profile;
        this.exchangePermits = new SemaphoreSlim(profile.MaximumConcurrentScans, profile.MaximumConcurrentScans);
    }

    /// <summary>Gets the address the daemon is reached at, for a caller with somewhere safe to record it.</summary>
    public string Endpoint => this.profile.Endpoint;

    /// <summary>Sends one command with one message and reads the answer.</summary>
    /// <param name="command">The command to issue.</param>
    /// <param name="message">The raw RFC 822 bytes to score.</param>
    /// <param name="cancellationToken">Cancels the exchange.</param>
    /// <returns>The parsed answer.</returns>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken" /> is cancelled.</exception>
    /// <exception cref="TimeoutException">Thrown when the exchange did not finish inside the configured budget.</exception>
    /// <exception cref="SocketException">Thrown when the daemon could not be reached.</exception>
    /// <exception cref="IOException">Thrown when the connection failed part-way through the exchange.</exception>
    /// <exception cref="InvalidOperationException">Thrown when what answered did not speak this protocol.</exception>
    public async Task<SpamdReply> ExchangeAsync(
        string command,
        ReadOnlyMemory<byte> message,
        CancellationToken cancellationToken)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(this.profile.ScanTimeout);

        try
        {
            return await this.ExchangeWithinBudgetAsync(command, message, budget.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "The spam daemon did not answer {0} within {1}.",
                    command,
                    this.profile.ScanTimeout));
        }
    }

    /// <summary>Establishes what the daemon calls itself, asking it at most once per process.</summary>
    /// <param name="cancellationToken">Cancels the exchange, when one is needed.</param>
    /// <returns>The corpus revision every scan is stamped with.</returns>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken" /> is cancelled.</exception>
    /// <exception cref="TimeoutException">Thrown when the exchange did not finish inside the configured budget.</exception>
    /// <exception cref="SocketException">Thrown when the daemon could not be reached.</exception>
    /// <exception cref="IOException">Thrown when the connection failed part-way through the exchange.</exception>
    /// <exception cref="InvalidOperationException">Thrown when what answered did not speak this protocol.</exception>
    /// <remarks>
    /// A failure is not remembered, only an answer is: a daemon that was restarting while the first caller asked is
    /// asked again by the next, rather than leaving the process unable to name a corpus for as long as it runs.
    /// </remarks>
    public async Task<string> IdentifyCorpusAsync(CancellationToken cancellationToken)
    {
        if (this.corpusRevision is { } established)
        {
            return established;
        }

        var reply = await this.ExchangeAsync(HeadersCommand, IdentityProbeMessage, cancellationToken);
        var identified = SpamAssassinCorpus.Identify(reply);

        this.corpusRevision = identified;

        return identified;
    }

    /// <inheritdoc />
    public void Dispose() => this.exchangePermits.Dispose();

    /// <summary>The one message this adapter ever composes: a synthetic note the daemon rewrites so it names its release.</summary>
    /// <remarks>
    /// Deliberately unremarkable and entirely invented — the domain is reserved so nothing addressed here resolves, and
    /// the text triggers nothing. What the daemon scores it is never read; the answer is wanted for the header the
    /// rewrite carries, which the daemon writes onto every message it is asked to rewrite whatever it thought of it.
    /// </remarks>
    private static ReadOnlyMemory<byte> IdentityProbeMessage { get; } = Encoding.ASCII.GetBytes(
        string.Join(
            "\r\n",
            "From: startup-probe@mailfathom.invalid",
            "To: startup-probe@mailfathom.invalid",
            "Subject: MailFathom scanner startup probe",
            "Message-ID: <startup-probe@mailfathom.invalid>",
            string.Empty,
            "MailFathom is establishing that this scanner answers.",
            string.Empty));

    /// <summary>Takes a permit and performs one exchange, both inside the caller's budget.</summary>
    /// <remarks>
    /// The wait for a permit is inside the budget rather than in front of it, so a saturated daemon costs one timeout
    /// like an absent one does. Outside it, a caller arriving behind a queue of scans would wait for as many budgets as
    /// the queue is deep — which is the stalled classification run the bound exists to prevent, reached the one way the
    /// bound could not see.
    /// </remarks>
    private async Task<SpamdReply> ExchangeWithinBudgetAsync(
        string command,
        ReadOnlyMemory<byte> message,
        CancellationToken cancellationToken)
    {
        await this.exchangePermits.WaitAsync(cancellationToken);

        try
        {
            using var connection = new TcpClient();

            await connection.ConnectAsync(this.profile.Host, this.profile.Port, cancellationToken);

            var stream = connection.GetStream();

            await stream.WriteAsync(RequestHead(command, message.Length), cancellationToken);
            await stream.WriteAsync(message, cancellationToken);
            await stream.FlushAsync(cancellationToken);

            // The request states its own length, so the daemon reads exactly what it was promised and answers without
            // being told that nothing more is coming. Shutting the write half down would say the same thing one way
            // further: a half-close is carried by a direct connection and by nothing that has to relay one, so an
            // exchange that depended on it would turn every intermediary between here and the daemon — a proxied
            // endpoint, a load balancer, a forwarded port — into an answer that never arrives.
            var answer = await ReadAnswerAsync(stream, cancellationToken);

            return SpamdReply.TryParse(answer.Span, out var reply)
                ? reply
                : throw new InvalidOperationException("What answered at the configured address did not speak the spam daemon's protocol.");
        }
        finally
        {
            _ = this.exchangePermits.Release();
        }
    }

    /// <summary>Composes the request line and the one header the daemon needs to know how much is coming.</summary>
    /// <remarks>
    /// No <c>User</c> header is sent. It names the account a scan is performed on behalf of, MailFathom scans on behalf
    /// of nobody the daemon knows, and a daemon reading per-user preferences from a name a client supplied is a
    /// configuration this deployment neither wants nor should be able to influence.
    /// </remarks>
    private static ReadOnlyMemory<byte> RequestHead(string command, int messageLength) => Encoding.ASCII.GetBytes(
        string.Format(
            CultureInfo.InvariantCulture,
            "{0} SPAMC/{1}\r\nContent-length: {2}\r\n\r\n",
            command,
            ProtocolVersion,
            messageLength));

    /// <summary>Reads everything the daemon wrote, refusing to grow past what an answer can be.</summary>
    /// <remarks>
    /// Read to the end of the stream rather than to the length the reply's own header states, because the daemon closes
    /// its side when it has finished and a length it wrote is a claim rather than a bound. Exceeding the buffer is
    /// treated as a daemon that did not answer usably, which is the same outcome as any other unintelligible reply.
    /// </remarks>
    private static async Task<ReadOnlyMemory<byte>> ReadAnswerAsync(
        NetworkStream stream,
        CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(MaximumAnswerBytes);

        try
        {
            var received = 0;

            while (received < MaximumAnswerBytes)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(received, MaximumAnswerBytes - received), cancellationToken);

                if (read is 0)
                {
                    return buffer.AsMemory(0, received).ToArray();
                }

                received += read;
            }

            throw new InvalidOperationException(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "The spam daemon answered with more than the {0} bytes an answer can be.",
                    MaximumAnswerBytes));
        }
        finally
        {
            // Cleared on the way back, because the pool is shared with everything else in the process and an answer is
            // derived from somebody's mail: for a rewritten header block it is that message's own headers.
            ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
        }
    }
}
