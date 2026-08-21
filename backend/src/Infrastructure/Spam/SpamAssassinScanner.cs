// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Spam.Scanning;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Spam;
using Microsoft.Extensions.Logging;

namespace MailFathom.Infrastructure.Spam;

/// <summary>Scores a message by asking an Apache SpamAssassin daemon deployed beside this service.</summary>
/// <remarks>
/// <para>
/// The port above stays what it was: a score, a threshold, the rule names, and the corpus. No socket, no protocol
/// vocabulary, and no SpamAssassin term crosses it — the rule names it carries are the corpus's own identifiers, which
/// is the one scanner-shaped thing a provenance record is for.
/// </para>
/// <para>
/// <b>The whole message is sent, and nothing is redacted before it goes.</b> A corpus scores what it reads, so a scanner
/// shown a redacted message scores the redactions: the markers become the text, the rules that read addresses and URIs
/// find placeholders, and the number that comes back describes a message nobody was sent. That is why the daemon is
/// expected inside the deployment rather than at an address on the internet.
/// </para>
/// <para>
/// Nothing here raises. Every failure is one of the port's outcomes, because a classification without its second opinion
/// is a weaker record rather than a failed operation — and a scanner that stopped answering must not be able to stall a
/// synchronization run behind it. Caller cancellation is the one exception, and it propagates because it is a fact about
/// the caller rather than about the daemon.
/// </para>
/// <para>
/// Marked as verified by the integration suite even though the unit suite drives every branch through a scripted daemon,
/// because the claim this class exists to make is not one a script can settle: that a real <c>spamd</c>, on the image the
/// deployment pulls, answers the request built here in the shape parsed here. A script answering a payload somebody
/// hand-wrote proves the parser handles the payload somebody hand-wrote.
/// </para>
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed partial class SpamAssassinScanner : ISpamScanner
{
    private readonly SpamAssassinDaemon daemon;
    private readonly SpamAssassinScannerProfile profile;
    private readonly ILogger<SpamAssassinScanner> logger;

    /// <summary>Initializes the scanner over one configured daemon.</summary>
    /// <param name="daemon">The conversation with that daemon.</param>
    /// <param name="profile">The bounds a scan is performed under.</param>
    /// <param name="logger">Reports an outcome that was not a score.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    public SpamAssassinScanner(
        SpamAssassinDaemon daemon,
        SpamAssassinScannerProfile profile,
        ILogger<SpamAssassinScanner> logger)
    {
        ArgumentNullException.ThrowIfNull(daemon);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(logger);

        this.daemon = daemon;
        this.profile = profile;
        this.logger = logger;
    }

    /// <inheritdoc />
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "The port answers with an outcome rather than raising: a scanner that failed for any reason leaves the classification with the verdict its headers reached, and a scan that threw instead would stall the run that asked for it.")]
    public async Task<SpamScanResult> ScanAsync(StoredEmailContent content, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);

        cancellationToken.ThrowIfCancellationRequested();

        if (content.RawMime.Length > this.profile.MaximumMessageBytes)
        {
            this.LogMessageTooLarge(content.RawMime.Length, this.profile.MaximumMessageBytes);

            return SpamScanResult.ContentTooLarge();
        }

        try
        {
            var corpusRevision = await this.daemon.IdentifyCorpusAsync(cancellationToken);
            var reply = await this.daemon.ExchangeAsync(
                SpamAssassinDaemon.SymbolsCommand,
                content.RawMime,
                cancellationToken);

            return Scored(reply, corpusRevision);
        }
        catch (OperationCanceledException)
        {
            // The caller's budget or the host's shutdown, and neither is a fact about the daemon.
            throw;
        }
        catch (Exception failure)
        {
            // No occurrence identifier reaches this port and no part of the message may reach a log, so what is
            // reported is the reason alone. The classification the caller goes on to record shows the same thing from
            // the other side: no scanner stage and no scanner signals.
            this.LogScanUnavailable(failure);

            return SpamScanResult.Unavailable();
        }
    }

    /// <summary>Maps one answer onto the port's result, or reports that it carried no usable numbers.</summary>
    /// <remarks>
    /// A reply that parsed but stated no score is treated as an unavailable scanner rather than as a score of zero. The
    /// daemon states its verdict as a pair of numbers, so a reply missing them is one this adapter did not understand,
    /// and a zero would be recorded as a message that was scored and found clean.
    /// </remarks>
    internal static SpamScanResult Scored(SpamdReply reply, string corpusRevision) =>
        reply.TryReadAssessment(out var score, out var threshold)
            ? SpamScanResult.Scored(
                SpamAssessment.Create(score, threshold),
                FiredRules(reply.Body),
                corpusRevision)
            : SpamScanResult.Unavailable();

    /// <summary>Reads the rule names out of the answer's body, which is one comma-separated line.</summary>
    /// <remarks>
    /// Whether that line ends with a line break varies between protocol versions by the protocol's own admission, so the
    /// parts are trimmed rather than the line being required to end a particular way. The result type caps how many are
    /// kept, so a corpus that fired dozens of rules bounds one message's derived data on its own.
    /// </remarks>
    internal static IReadOnlyList<string> FiredRules(string body) =>
        [.. body.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "A message of {MessageBytes} bytes was not sent to the spam scanner, which accepts {MaximumMessageBytes}. It keeps the verdict its headers reached.")]
    private partial void LogMessageTooLarge(int messageBytes, int maximumMessageBytes);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "The spam scanner did not answer usably, so the classification keeps the verdict its headers reached.")]
    private partial void LogScanUnavailable(Exception failure);
}
