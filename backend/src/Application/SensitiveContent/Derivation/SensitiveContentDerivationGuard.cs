// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.SensitiveContent.Detection;
using MailFathom.Application.SensitiveContent.Redaction;
using MailFathom.Domain.Access;

namespace MailFathom.Application.SensitiveContent.Derivation;

/// <summary>The one thing every derived write calls before it copies mail text into a store of its own.</summary>
/// <remarks>
/// <para>
/// Derived data is where redaction is cheapest and most durable: text redacted once stays redacted for every reader the
/// chunk, the vector, and the search document ever have, and putting it back costs a re-derivation from raw MIME rather
/// than a refetch from a mail server. So the derived path redacts on the way in, while
/// <see cref="Egress.SensitiveContentEgressGuard" /> redacts on the way out — two boundaries, one
/// <see cref="SensitiveContentRedactor" /> behind both for any one owner, which is what keeps a citation drawn from a
/// redacted chunk landing on the same redacted text when a reader opens the message.
/// </para>
/// <para>
/// <b>Whose mail is being derived is an argument rather than an ambient fact.</b> Both paths that reach here already
/// hold it — synchronization is running one owner's account, and a re-derivation was enqueued for one owner — so the
/// posture is resolved from what the caller knows instead of from a scope somebody has to remember to open. The egress
/// guard settles the same question differently, and says why in its own words: there the values are guarded several
/// layers below whoever resolved the owner, and here they are not.
/// </para>
/// <para>
/// <b>It carries the stamp as well as the redaction.</b> A derived row records the configuration it was written under,
/// so a scanner switched on over an already-indexed mailbox is answerable rather than silently partial: what was written
/// under an older configuration is stale in exactly the sense an embedding profile already uses, and the way back is a
/// rebuild. The stamp exists precisely when a redaction does, which is what makes "written under no scanner" and
/// "written under this scanner" two readable states rather than one absence. It is one owner's stamp rather than the
/// deployment's, so a posture one owner changed leaves nobody else's rows stale.
/// </para>
/// <para>
/// <b>With nothing switched on for an owner this guard is inert.</b> It is registered whatever a deployment configured,
/// so no writer carries a null check or a second code path, and with no redaction behind that owner's posture every
/// call returns its argument without constructing a detector, taking a concurrency permit, or touching an instrument —
/// and stamps nothing, so a derived row is byte-identical to the one the same message produced before this feature
/// existed.
/// </para>
/// </remarks>
public sealed class SensitiveContentDerivationGuard
{
    private readonly ISensitiveContentPostures postures;
    private readonly ISensitiveContentDerivationTelemetry telemetry;
    private readonly TimeProvider timeProvider;

    /// <summary>Initializes the derived-write guard of a deployment, whether or not it scans anything.</summary>
    /// <param name="postures">Answers what each owner's mail is derived under.</param>
    /// <param name="telemetry">Reports what each derived write found and what it cost.</param>
    /// <param name="timeProvider">Measures what the scan added to the derivation.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    public SensitiveContentDerivationGuard(
        ISensitiveContentPostures postures,
        ISensitiveContentDerivationTelemetry telemetry,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(postures);
        ArgumentNullException.ThrowIfNull(telemetry);
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.postures = postures;
        this.telemetry = telemetry;
        this.timeProvider = timeProvider;
    }

    /// <summary>Gets whether any owner this deployment serves has what is derived from their mail redacted.</summary>
    /// <remarks>
    /// Read by a walk deciding whether work only a redaction makes necessary is worth arranging at all. What one
    /// message is judged by is <see cref="StampFor" />, because a deployment that redacts somebody's mail need not
    /// redact everybody's.
    /// </remarks>
    public bool IsActive => this.postures.IsActiveForAnyOwner;

    /// <summary>Gets what every owner this deployment serves has their mail derived under, ordered by owner.</summary>
    /// <remarks>
    /// For the walk that judges rows belonging to several owners in one query, which is the one consumer that cannot
    /// ask about the owner in front of it. <see cref="ISensitiveContentPostures.Current" /> holds why.
    /// </remarks>
    public IReadOnlyList<OwnerSensitiveContentPosture> Current => this.postures.Current;

    /// <summary>Gets the configuration a row of one owner's mail written now records, or nothing where it is not scanned.</summary>
    /// <param name="owner">The owner whose mail the row is derived from.</param>
    /// <returns>That owner's stamp, or <see langword="null" /> where nothing scans their mail.</returns>
    public SensitiveContentDerivationStamp? StampFor(MailOwnerId owner) => this.postures.ForOwner(owner).Stamp;

    /// <summary>Redacts one text about to be written into a derived store.</summary>
    /// <param name="owner">The owner whose mail the text was extracted from.</param>
    /// <param name="text">The text to redact, which must be a value rather than a document composed around one.</param>
    /// <param name="cancellationToken">Cancels the scan.</param>
    /// <returns>The text with every detected region replaced, or the text itself where nothing scans this owner's mail.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="text" /> is <see langword="null" />.</exception>
    /// <exception cref="SensitiveContentScannerUnavailableException">Thrown when a switched-on scanner could not establish what the text carries, which refuses the derived write.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken" /> is cancelled.</exception>
    public Task<string> GuardAsync(MailOwnerId owner, string text, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(text);

        return this.postures.ForOwner(owner).Redactor is { } active
            ? this.RedactAsync(active, text, cancellationToken)
            : Task.FromResult(text);
    }

    /// <summary>Runs the owner's redaction and reports what it found, or reports the refusal and re-raises it.</summary>
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
