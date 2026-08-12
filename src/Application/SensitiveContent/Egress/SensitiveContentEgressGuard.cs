// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.SensitiveContent.Detection;
using MailFathom.Application.SensitiveContent.Redaction;

namespace MailFathom.Application.SensitiveContent.Egress;

/// <summary>The one thing every egress point calls before it hands text to somebody else.</summary>
/// <remarks>
/// <para>
/// One guard rather than a redaction per consumer, for the reason there is one redactor behind it: a placeholder a
/// caller composed itself would drift from the shared one the first time either gained a rule, and a consumer holding
/// the redactor directly would decide for itself what to do with a finding. What a consumer gets here is guarded text
/// and nothing else — the findings stay inside, are counted by category, and are never handed to a caller that might
/// log one.
/// </para>
/// <para>
/// <b>Guard a value, never a composed document.</b> A detected region is replaced wherever it was found, so a scan of a
/// document this system assembled — an XML envelope, a JSON payload, a formatted listing — can report a region that
/// covers a delimiter as well as the text beside it, and replacing that region would destroy the structure while
/// leaving the value's neighbours in it. Every consumer therefore guards the field it is about to write and composes
/// afterwards.
/// </para>
/// <para>
/// <b>With both switches off this guard is inert.</b> It is registered whatever a deployment configured, so no consumer
/// carries a null check or a second code path, and with no redactor behind it every call returns its argument without
/// constructing a detector, taking a concurrency permit, or touching an instrument. That is what makes an opt-in nobody
/// took cost nothing on any of these paths.
/// </para>
/// </remarks>
public sealed class SensitiveContentEgressGuard
{
    private readonly SensitiveContentRedactor? redactor;
    private readonly ISensitiveContentEgressTelemetry telemetry;
    private readonly TimeProvider timeProvider;

    /// <summary>Initializes the guard of a deployment, whether or not it scans anything.</summary>
    /// <param name="redactor">The one redaction every consumer shares, or <see langword="null" /> where both switches are off.</param>
    /// <param name="telemetry">Reports what each guarded call found and what it cost.</param>
    /// <param name="timeProvider">Measures what the scan added to the operation being guarded.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="telemetry" /> or <paramref name="timeProvider" /> is <see langword="null" />.</exception>
    public SensitiveContentEgressGuard(
        SensitiveContentRedactor? redactor,
        ISensitiveContentEgressTelemetry telemetry,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(telemetry);
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.redactor = redactor;
        this.telemetry = telemetry;
        this.timeProvider = timeProvider;
    }

    /// <summary>Gets whether this deployment scans anything at all.</summary>
    /// <remarks>
    /// Read by a consumer deciding whether work only a scan makes necessary is worth doing — never as permission to
    /// hand text on unguarded, which is what calling the guard already does when it is inactive.
    /// </remarks>
    public bool IsActive => this.redactor is not null;

    /// <summary>Guards one text about to cross out of this deployment.</summary>
    /// <param name="egressPoint">Where the text is going.</param>
    /// <param name="text">The text to guard, which must be a value rather than a document composed around one.</param>
    /// <param name="cancellationToken">Cancels the scan.</param>
    /// <returns>The text with every detected region replaced, or the text itself where nothing is scanned.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="text" /> is <see langword="null" />.</exception>
    /// <exception cref="SensitiveContentScannerUnavailableException">Thrown when a switched-on scanner could not establish what the text carries, which refuses the egress.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken" /> is cancelled.</exception>
    public Task<string> GuardAsync(
        SensitiveContentEgressPoint egressPoint,
        string text,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(text);

        return this.redactor is { } active
            ? this.RedactAsync(active, egressPoint, text, cancellationToken)
            : Task.FromResult(text);
    }

    /// <summary>Guards a text that a message need not carry at all.</summary>
    /// <param name="egressPoint">Where the text is going.</param>
    /// <param name="text">The text to guard, or <see langword="null" /> where the message carried none.</param>
    /// <param name="cancellationToken">Cancels the scan.</param>
    /// <returns>The guarded text, or <see langword="null" /> where there was none.</returns>
    /// <exception cref="SensitiveContentScannerUnavailableException">Thrown when a switched-on scanner could not establish what the text carries, which refuses the egress.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken" /> is cancelled.</exception>
    /// <remarks>
    /// Absence is carried through rather than turned into an empty string, because a subject nobody wrote and a subject
    /// redacted to nothing are different facts and a reader acts differently on each.
    /// </remarks>
    public async Task<string?> GuardOptionalAsync(
        SensitiveContentEgressPoint egressPoint,
        string? text,
        CancellationToken cancellationToken)
    {
        if (text is null || this.redactor is not { } active)
        {
            return text;
        }

        return await this.RedactAsync(active, egressPoint, text, cancellationToken);
    }

    /// <summary>Guards every text of one publication about to cross out of this deployment.</summary>
    /// <param name="egressPoint">Where the texts are going.</param>
    /// <param name="texts">The texts to guard, in the order they are published.</param>
    /// <param name="cancellationToken">Cancels the scan.</param>
    /// <returns>The guarded texts, in the same order and the same number.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="texts" /> is <see langword="null" />.</exception>
    /// <exception cref="SensitiveContentScannerUnavailableException">Thrown when a switched-on scanner could not establish what one of them carries, which refuses the whole egress.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken" /> is cancelled.</exception>
    /// <remarks>
    /// Each text is scanned on its own rather than joined into one pass, because a joined scan would let a detection
    /// straddle the join and redact across two publications that have nothing to do with each other. The concurrency
    /// bound the redactor holds is what keeps a wide publication from opening a connection per text.
    /// </remarks>
    public Task<IReadOnlyList<string>> GuardAllAsync(
        SensitiveContentEgressPoint egressPoint,
        IReadOnlyList<string> texts,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(texts);

        return this.redactor is { } active && texts.Count > 0
            ? this.RedactAllAsync(active, egressPoint, texts, cancellationToken)
            : Task.FromResult(texts);
    }

    private async Task<IReadOnlyList<string>> RedactAllAsync(
        SensitiveContentRedactor active,
        SensitiveContentEgressPoint egressPoint,
        IReadOnlyList<string> texts,
        CancellationToken cancellationToken)
    {
        var guarded = new List<string>(texts.Count);

        foreach (var text in texts)
        {
            guarded.Add(await this.RedactAsync(active, egressPoint, text, cancellationToken));
        }

        return guarded;
    }

    /// <summary>Runs the shared redaction and reports what it found, or reports the refusal and re-raises it.</summary>
    /// <remarks>
    /// The refusal reaches the caller unchanged. It already names the scanner and no text, and translating it here would
    /// cost the error code an operator reads the failure by while adding nothing this layer knows.
    /// </remarks>
    private async Task<string> RedactAsync(
        SensitiveContentRedactor active,
        SensitiveContentEgressPoint egressPoint,
        string text,
        CancellationToken cancellationToken)
    {
        var startedAt = this.timeProvider.GetTimestamp();

        try
        {
            var redacted = await active.RedactAsync(text, cancellationToken);

            this.telemetry.RecordGuarded(egressPoint, redacted, this.timeProvider.GetElapsedTime(startedAt));

            return redacted.Text;
        }
        catch (SensitiveContentScannerUnavailableException refusal)
        {
            this.telemetry.RecordRefused(egressPoint, refusal.Scanner);

            throw;
        }
    }
}
