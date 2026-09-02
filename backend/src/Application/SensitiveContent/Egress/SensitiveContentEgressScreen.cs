// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.SensitiveContent.Detection;
using MailFathom.Application.SensitiveContent.Redaction;
using MailFathom.Domain.Access;

namespace MailFathom.Application.SensitiveContent.Egress;

/// <summary>The thing an egress point calls when a finding has to stop the act rather than be removed from a result.</summary>
/// <remarks>
/// <para>
/// It is the sibling of <see cref="SensitiveContentEgressGuard" /> and shares everything with it but the disposition:
/// the same scanners, the same categories and suppressions, the same analyzed ceiling, the same per-call budget, the
/// same concurrency permits, and the same fail-closed answer when a switched-on scanner cannot run. What differs is
/// what a finding means. A guard is asked *how may this text be published*, and answers with the text minus what it
/// found; a screen is asked *may this act happen at all*, and answers yes or no.
/// </para>
/// <para>
/// <b>The screen exists because redaction is the wrong answer for a message somebody wrote.</b> Replacing a region of
/// an outgoing body would transmit text its author never wrote, under their own address, to a person who has no way of
/// knowing anything was changed — and the author would learn of it, if at all, from the copy in their sent folder. So
/// the act is stopped and the author decides what to do, which is the only disposition that leaves the decision with
/// whoever is accountable for the message.
/// </para>
/// <para>
/// <b>The redaction behind it is the same instance every other consumer redacts through</b>, and running it is how the
/// findings are reached. The redacted text it produces is discarded here, which costs nothing on the path that matters:
/// a text carrying no finding is returned by that redaction unchanged and unrewritten, so a clean message pays for the
/// scan and for nothing else.
/// </para>
/// <para>
/// <b>A text the ceiling cut is refused rather than passed.</b> That is the one place a screen must behave differently
/// from a guard and not merely mean something different by the result: a guard drops the remainder and publishes the
/// rest, losing nothing that was never scanned, while a screen asked about a whole message cannot say a tail nobody
/// analyzed is clean. Both readings are fail-closed; they just fail closed about different questions.
/// </para>
/// <para>
/// <b>With nothing to screen this is inert.</b> It is registered whatever a deployment configured, so no consumer
/// carries a null check or a second code path, and with no redaction or a policy that stops nothing every call answers
/// without constructing a detector, taking a concurrency permit, or touching an instrument.
/// </para>
/// <para>
/// <b>Which findings stop a message is the author's own posture.</b> The deployment names the categories no message
/// may carry out of it and an owner may add to that set for their own mail, so what a send is judged by is composed
/// from both rather than read off the deployment alone. The owner is an argument here because the two callers — the
/// outbox and the draft book — hold the message's author before they hold its bytes.
/// </para>
/// </remarks>
public sealed class SensitiveContentEgressScreen
{
    private readonly ISensitiveContentPostures postures;
    private readonly ISensitiveContentEgressTelemetry telemetry;
    private readonly TimeProvider timeProvider;

    /// <summary>Initializes the screen of a deployment, whether or not it screens anything.</summary>
    /// <param name="postures">Answers which of an owner's findings stop their outgoing message.</param>
    /// <param name="telemetry">Reports what each screened act found and what it cost.</param>
    /// <param name="timeProvider">Measures what the scan added to the act being screened.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    public SensitiveContentEgressScreen(
        ISensitiveContentPostures postures,
        ISensitiveContentEgressTelemetry telemetry,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(postures);
        ArgumentNullException.ThrowIfNull(telemetry);
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.postures = postures;
        this.telemetry = telemetry;
        this.timeProvider = timeProvider;
    }

    /// <summary>Reports whether anything stops one owner's message at this kind of egress.</summary>
    /// <param name="owner">The owner whose message is about to be queued or filed.</param>
    /// <returns><see langword="true" /> when a finding in their message could stop it.</returns>
    /// <remarks>
    /// Read by a consumer deciding whether work only a screen makes necessary is worth doing — parsing a message back
    /// into the values to screen, for one — never as permission to let the act happen unscreened, which is what calling
    /// the screen already does when it is inactive.
    /// </remarks>
    public bool IsActiveFor(MailOwnerId owner) => this.postures.ForOwner(owner).ScreensAnything;

    /// <summary>Screens every text of one act, and reports the first thing that stops it.</summary>
    /// <param name="egressPoint">Where the texts were about to go.</param>
    /// <param name="owner">The owner whose message is about to leave, whose posture the findings are judged by.</param>
    /// <param name="texts">The texts to screen, each a value rather than a document composed around one.</param>
    /// <param name="cancellationToken">Cancels the scan.</param>
    /// <returns>What stopped the act, or <see langword="null" /> where nothing did.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="texts" /> is <see langword="null" />, or one of them is.</exception>
    /// <exception cref="SensitiveContentScannerUnavailableException">Thrown when a switched-on scanner could not establish what a text carries, which stops the act without saying what was in it.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken" /> is cancelled.</exception>
    /// <remarks>
    /// <para>
    /// It stops at the first text that refuses rather than screening the rest, because the act is refused whichever
    /// text carried the material and every further scan would be a round trip spent on an act that is already not
    /// happening. Which text it was is deliberately not reported: an author told the subject rather than the body
    /// carried a credential has been told where to look for it in a message a log line will outlive.
    /// </para>
    /// <para>
    /// Each text is scanned on its own for the reason the guard scans a collection that way — a joined scan would let a
    /// detection straddle the join between a subject and a body that have nothing to do with each other.
    /// </para>
    /// </remarks>
    public Task<SensitiveContentEgressRefusal?> ScreenAsync(
        SensitiveContentEgressPoint egressPoint,
        MailOwnerId owner,
        IReadOnlyList<string> texts,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(texts);

        var posture = this.postures.ForOwner(owner);

        return texts.Count > 0 && posture is { ScreensAnything: true, Redactor: { } active }
            ? this.ScreenEachAsync(active, posture.Screening, egressPoint, owner, texts, cancellationToken)
            : Task.FromResult<SensitiveContentEgressRefusal?>(null);
    }

    private async Task<SensitiveContentEgressRefusal?> ScreenEachAsync(
        SensitiveContentRedactor active,
        SensitiveContentScreeningPolicy policy,
        SensitiveContentEgressPoint egressPoint,
        MailOwnerId owner,
        IReadOnlyList<string> texts,
        CancellationToken cancellationToken)
    {
        using var operation = this.telemetry.BeginGuardedOperation(egressPoint, owner, cancellationToken);

        try
        {
            foreach (var text in texts)
            {
                var screened = await this.ScreenOneAsync(active, policy, egressPoint, text, cancellationToken);

                operation.TextGuarded();

                if (screened is { } refusal)
                {
                    this.telemetry.RecordStopped(egressPoint, refusal);

                    // Completed rather than refused. The operation is the scanning, and scanning that found what it was
                    // looking for did its whole job; what was refused is the act in front of it, which reports its own
                    // failure with a code of its own. The scope's refusal means the opposite thing — a scanner that
                    // could not answer — and is written where that happens.
                    operation.Completed();

                    return refusal;
                }
            }

            operation.Completed();

            return null;
        }
        catch (SensitiveContentScannerUnavailableException)
        {
            operation.Refused();

            throw;
        }
    }

    /// <summary>Scans one text through the shared redaction and reads its findings against the policy.</summary>
    /// <remarks>
    /// The refusal a scanner that could not answer raises reaches the caller unchanged, for the reason the guard passes
    /// it on: it already names the scanner and no text, and translating it here would cost the error code an operator
    /// reads the failure by. It is recorded through the same instrument the guard records it through, because a
    /// deployment whose analyzer is down is one fact rather than one per consumer.
    /// </remarks>
    private async Task<SensitiveContentEgressRefusal?> ScreenOneAsync(
        SensitiveContentRedactor active,
        SensitiveContentScreeningPolicy policy,
        SensitiveContentEgressPoint egressPoint,
        string text,
        CancellationToken cancellationToken)
    {
        var startedAt = this.timeProvider.GetTimestamp();

        RedactedText scanned;

        try
        {
            scanned = await active.RedactAsync(text, cancellationToken);
        }
        catch (SensitiveContentScannerUnavailableException refusal)
        {
            this.telemetry.RecordRefused(egressPoint, refusal.Scanner);

            throw;
        }

        this.telemetry.RecordGuarded(egressPoint, scanned, this.timeProvider.GetElapsedTime(startedAt));

        var stopping = scanned.Findings
            .Select(finding => (Finding: finding, Scanner: policy.StoppedBy(finding)))
            .FirstOrDefault(judged => judged.Scanner is not null);

        if (stopping.Scanner is { } scanner)
        {
            return SensitiveContentEgressRefusal.ContentFound(scanner, stopping.Finding.Category);
        }

        // Read after the findings rather than before them, so a message that carries something inside the analyzed part
        // is refused for what was found rather than for the length that stopped the scan. Both refuse the act; only the
        // first tells the author something they can act on.
        return scanned.OmittedCharacterCount > 0 ? SensitiveContentEgressRefusal.NotFullyScanned() : null;
    }
}
