// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.SensitiveContent.Detection;
using MailFathom.Application.SensitiveContent.Redaction;

namespace MailFathom.Application.SensitiveContent.Derivation;

/// <summary>The one thing every derived write calls before it copies mail text into a store of its own.</summary>
/// <remarks>
/// <para>
/// Derived data is where redaction is cheapest and most durable: text redacted once stays redacted for every reader the
/// chunk, the vector, and the search document ever have, and putting it back costs a re-derivation from raw MIME rather
/// than a refetch from a mail server. So the derived path redacts on the way in, while
/// <see cref="Egress.SensitiveContentEgressGuard" /> redacts on the way out — two boundaries, one
/// <see cref="SensitiveContentRedactor" /> behind both, which is what keeps a citation drawn from a redacted chunk
/// landing on the same redacted text when a reader opens the message.
/// </para>
/// <para>
/// <b>It carries the stamp as well as the redaction.</b> A derived row records the configuration it was written under,
/// so a scanner switched on over an already-indexed mailbox is answerable rather than silently partial: what was written
/// under an older configuration is stale in exactly the sense an embedding profile already uses, and the way back is a
/// rebuild. The stamp exists precisely when a redaction does, which is what makes "written under no scanner" and
/// "written under this scanner" two readable states rather than one absence.
/// </para>
/// <para>
/// <b>With both switches off this guard is inert.</b> It is registered whatever a deployment configured, so no writer
/// carries a null check or a second code path, and with no redactor behind it every call returns its argument without
/// constructing a detector, taking a concurrency permit, or touching an instrument — and stamps nothing, so a derived
/// row is byte-identical to the one the same message produced before this feature existed.
/// </para>
/// </remarks>
public sealed class SensitiveContentDerivationGuard
{
    private readonly SensitiveContentRedactor? redactor;
    private readonly ISensitiveContentDerivationTelemetry telemetry;
    private readonly TimeProvider timeProvider;

    /// <summary>Initializes the derived-write guard of a deployment, whether or not it scans anything.</summary>
    /// <param name="redactor">The one redaction every consumer shares, or <see langword="null" /> where both switches are off.</param>
    /// <param name="stamp">The configuration derived rows are written under, present exactly when <paramref name="redactor" /> is.</param>
    /// <param name="telemetry">Reports what each derived write found and what it cost.</param>
    /// <param name="timeProvider">Measures what the scan added to the derivation.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="telemetry" /> or <paramref name="timeProvider" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when a stamp is supplied without a redactor, or a redactor without a stamp.</exception>
    public SensitiveContentDerivationGuard(
        SensitiveContentRedactor? redactor,
        SensitiveContentDerivationStamp? stamp,
        ISensitiveContentDerivationTelemetry telemetry,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(telemetry);
        ArgumentNullException.ThrowIfNull(timeProvider);

        // The pair is the contract every reader of a stored stamp depends on: a stamp on a row promises that the text
        // beside it went through a redaction, and a redaction with no stamp would produce rows nothing could ever tell
        // apart from the ones written before the feature existed.
        if (redactor is null != stamp is null)
        {
            throw new ArgumentException(
                "A derived-write guard either redacts and stamps or does neither, so a stamp without a redactor and a redactor without a stamp are both refused.",
                redactor is null ? nameof(stamp) : nameof(redactor));
        }

        this.redactor = redactor;
        this.Stamp = stamp;
        this.telemetry = telemetry;
        this.timeProvider = timeProvider;
    }

    /// <summary>Gets the configuration a row written now records, or <see langword="null" /> where nothing is scanned.</summary>
    public SensitiveContentDerivationStamp? Stamp { get; }

    /// <summary>Gets whether this deployment redacts what it derives at all.</summary>
    public bool IsActive => this.redactor is not null;

    /// <summary>Redacts one text about to be written into a derived store.</summary>
    /// <param name="text">The text to redact, which must be a value rather than a document composed around one.</param>
    /// <param name="cancellationToken">Cancels the scan.</param>
    /// <returns>The text with every detected region replaced, or the text itself where nothing is scanned.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="text" /> is <see langword="null" />.</exception>
    /// <exception cref="SensitiveContentScannerUnavailableException">Thrown when a switched-on scanner could not establish what the text carries, which refuses the derived write.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken" /> is cancelled.</exception>
    public Task<string> GuardAsync(string text, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(text);

        return this.redactor is { } active
            ? this.RedactAsync(active, text, cancellationToken)
            : Task.FromResult(text);
    }

    /// <summary>Runs the shared redaction and reports what it found, or reports the refusal and re-raises it.</summary>
    /// <remarks>
    /// The refusal reaches the caller unchanged, so a synchronization run or a backfill batch fails with the error code
    /// naming the scanner rather than with something this layer invented. That failure is the fail-closed contract at
    /// work: nothing derived from that text is written, whatever was already stored is left as it was, and the next run
    /// derives the message once the detector answers again.
    /// </remarks>
    private async Task<string> RedactAsync(
        SensitiveContentRedactor active,
        string text,
        CancellationToken cancellationToken)
    {
        var startedAt = this.timeProvider.GetTimestamp();

        try
        {
            var redacted = await active.RedactAsync(text, cancellationToken);

            this.telemetry.RecordDerived(redacted, this.timeProvider.GetElapsedTime(startedAt));

            return redacted.Text;
        }
        catch (SensitiveContentScannerUnavailableException refusal)
        {
            this.telemetry.RecordRefused(refusal.Scanner);

            throw;
        }
    }
}
