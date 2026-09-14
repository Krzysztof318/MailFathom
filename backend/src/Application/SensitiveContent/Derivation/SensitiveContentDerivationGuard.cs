// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.SensitiveContent.Detection;
using MailFathom.Application.SensitiveContent.Redaction;
using MailFathom.Domain.Accounts;

namespace MailFathom.Application.SensitiveContent.Derivation;

/// <summary>The one thing every derived write calls before it copies mail text into a store of its own.</summary>
/// <remarks>
/// <para>
/// Derived data is where redaction is cheapest and most durable: text redacted once stays redacted for every reader the
/// chunk, the vector, and the search document ever have, and putting it back costs a re-derivation from raw MIME rather
/// than a refetch from a mail server. So the derived path redacts on the way in, while
/// <see cref="Egress.SensitiveContentEgressGuard" /> redacts on the way out — two boundaries, one
/// <see cref="SensitiveContentRedactor" /> behind both for any one account, which is what keeps a citation drawn from
/// a redacted chunk landing on the same redacted text when a reader opens the message.
/// </para>
/// <para>
/// <b>Which account's mail is being derived is an argument rather than an ambient fact.</b> Both paths that reach here
/// already hold it — synchronization is running one account, and a re-derivation was enqueued for one account — so the
/// posture is resolved from what the caller knows instead of from a scope somebody has to remember to open. The egress
/// guard settles the same question differently, and says why in its own words: there the values are guarded several
/// layers below whoever resolved the mail, and here they are not.
/// </para>
/// <para>
/// <b>It carries the stamp as well as the redaction.</b> A derived row records the configuration it was written under,
/// so a scanner switched on over an already-indexed mailbox is answerable rather than silently partial: what was written
/// under an older configuration is stale in exactly the sense an embedding profile already uses, and the way back is a
/// rebuild. The stamp exists precisely when a redaction does, which is what makes "written under no scanner" and
/// "written under this scanner" two readable states rather than one absence. It is one account's stamp rather than the
/// deployment's, so a posture one account changed leaves no other account's rows stale.
/// </para>
/// <para>
/// <b>With nothing switched on for an account this guard is inert.</b> It is registered whatever a deployment
/// configured, so no writer carries a null check or a second code path, and with no redaction behind its posture every
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
    /// <param name="postures">Answers what each account's mail is derived under.</param>
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

    /// <summary>Gets whether any account this deployment serves has what is derived from its mail redacted.</summary>
    /// <remarks>
    /// Read by a walk deciding whether work only a redaction makes necessary is worth arranging at all. What one
    /// message is judged by is <see cref="StampFor" />, because a deployment that redacts one mailbox's mail need not
    /// redact every mailbox's.
    /// </remarks>
    public bool IsActive => this.postures.IsActiveForAnyAccount;

    /// <summary>Gets what every account this deployment serves has its mail derived under, ordered by account.</summary>
    /// <remarks>
    /// For the walk that judges rows belonging to several accounts in one query, which is the one consumer that cannot
    /// ask about the account in front of it. <see cref="ISensitiveContentPostures.Current" /> holds why.
    /// </remarks>
    public IReadOnlyList<MailAccountSensitiveContentPosture> Current => this.postures.Current;

    /// <summary>Gets what a row belonging to an account this deployment no longer serves is judged against.</summary>
    /// <remarks>
    /// The deployment's own posture, which is what <see cref="ISensitiveContentPostures.ForAccount" /> answers for an
    /// account off the roster and is the stricter of the two candidates. Read by the walk that judges rows belonging to
    /// several accounts at once, so that mail still stored for a mailbox a deployment has stopped serving is judged by
    /// something rather than stepped over.
    /// <para>
    /// The unspecified identifier is what asks the question, because no roster carries one: an account identifier is
    /// created from text that may not be blank, so this reaches the fallback by the same route every account the
    /// roster does not name reaches it.
    /// </para>
    /// </remarks>
    public SensitiveContentDerivationStamp? StampForUnservedAccount => this.postures.ForAccount(default).Stamp;

    /// <summary>Gets the configuration a row of one account's mail written now records, or nothing where it is not scanned.</summary>
    /// <param name="account">The account whose mail the row is derived from.</param>
    /// <returns>That account's stamp, or <see langword="null" /> where nothing scans its mail.</returns>
    public SensitiveContentDerivationStamp? StampFor(MailAccountId account) =>
        this.postures.ForAccount(account).Stamp;

    /// <summary>Redacts one text about to be written into a derived store.</summary>
    /// <param name="account">The account whose mail the text was extracted from.</param>
    /// <param name="text">The text to redact, which must be a value rather than a document composed around one.</param>
    /// <param name="cancellationToken">Cancels the scan.</param>
    /// <returns>The text with every detected region replaced, or the text itself where nothing scans this account's mail.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="text" /> is <see langword="null" />.</exception>
    /// <exception cref="SensitiveContentScannerUnavailableException">Thrown when a switched-on scanner could not establish what the text carries, which refuses the derived write.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken" /> is cancelled.</exception>
    public async Task<string> GuardAsync(MailAccountId account, string text, CancellationToken cancellationToken) =>
        (await this.GuardTextAsync(account, text, cancellationToken)).Text;

    /// <summary>Redacts one text and reports where every placeholder ended up.</summary>
    /// <param name="account">The account whose mail the text was extracted from.</param>
    /// <param name="text">The text to redact, which must be a value rather than a document composed around one.</param>
    /// <param name="cancellationToken">Cancels the scan.</param>
    /// <returns>The redaction, which is the text itself with no placement where nothing scans this account's mail.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="text" /> is <see langword="null" />.</exception>
    /// <exception cref="SensitiveContentScannerUnavailableException">Thrown when a switched-on scanner could not establish what the text carries, which refuses the derived write.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken" /> is cancelled.</exception>
    /// <remarks>
    /// The same redaction <see cref="GuardAsync" /> performs, handed back whole rather than as its text alone. A caller
    /// needs this where it holds offsets into the text it passed in — an attachment's page boundaries are the case it
    /// exists for — because a placeholder is shorter or longer than what it replaced, so every such offset past the
    /// first finding has moved.
    /// </remarks>
    public Task<RedactedText> GuardTextAsync(MailAccountId account, string text, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(text);

        return this.postures.ForAccount(account).Redactor is { } active
            ? this.RedactAsync(active, text, cancellationToken)
            : Task.FromResult(RedactedText.Create(text, [], omittedCharacterCount: 0));
    }

    /// <summary>Runs the account's redaction and reports what it found, or reports the refusal and re-raises it.</summary>
    /// <remarks>
    /// The refusal reaches the caller unchanged, so a synchronization run or a backfill batch fails with the error code
    /// naming the scanner rather than with something this layer invented. That failure is the fail-closed contract at
    /// work: nothing derived from that text is written, whatever was already stored is left as it was, and the next run
    /// derives the message once the detector answers again.
    /// </remarks>
    private async Task<RedactedText> RedactAsync(
        SensitiveContentRedactor active,
        string text,
        CancellationToken cancellationToken)
    {
        var startedAt = this.timeProvider.GetTimestamp();

        try
        {
            var redacted = await active.RedactAsync(text, cancellationToken);

            this.telemetry.RecordDerived(redacted, this.timeProvider.GetElapsedTime(startedAt));

            return redacted;
        }
        catch (SensitiveContentScannerUnavailableException refusal)
        {
            this.telemetry.RecordRefused(refusal.Scanner);

            throw;
        }
    }
}
