// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.SensitiveContent.Derivation;
using MailFathom.Application.SensitiveContent.Egress;
using MailFathom.Application.SensitiveContent.Redaction;

namespace MailFathom.Application.SensitiveContent;

/// <summary>What one owner's mail is scanned for, what a finding in it does, and what a derived row of it records.</summary>
/// <remarks>
/// <para>
/// One deployment holds several of these, because a scanner switched on for one owner's correspondence is a cost with
/// no return over another's. What varies between them is only what is scanned for and what a finding stops: the
/// analyzer's address, the analyzed ceiling, the per-scan budget, and the process-wide concurrency are the
/// deployment's, and every posture reads mail through detectors registered once and permits budgeted once.
/// </para>
/// <para>
/// The three parts belong together because they are computed from one answer and would otherwise be resolved three
/// times per text: the redaction that runs the scanners, the policy that decides which of their findings refuse an
/// outgoing message, and the stamp a derived row records so a posture changed later is answerable rather than
/// silently partial.
/// </para>
/// <para>
/// <see cref="ScanningNothing" /> is an ordinary state rather than a missing one. An owner whose deployment switched
/// both scanners off and who switched neither on themselves reads mail through a posture that constructs no detector,
/// takes no permit, and stamps nothing, which is what keeps an opt-in nobody took free on every path.
/// </para>
/// </remarks>
public sealed record SensitiveContentPosture
{
    private SensitiveContentPosture(
        IReadOnlyList<SensitiveContentScannerKind> scanners,
        SensitiveContentRedactor? redactor,
        SensitiveContentScreeningPolicy screening,
        SensitiveContentDerivationStamp? stamp)
    {
        this.Scanners = scanners;
        this.Redactor = redactor;
        this.Screening = screening;
        this.Stamp = stamp;
    }

    /// <summary>Gets the posture of an owner whose mail nothing is scanned for.</summary>
    public static SensitiveContentPosture ScanningNothing { get; } =
        new([], null, SensitiveContentScreeningPolicy.ScreeningNothing(), null);

    /// <summary>Gets which scanners run over this owner's mail, which is empty where nothing does.</summary>
    /// <remarks>
    /// Published beside the redaction rather than read out of it, because one consumer asks about a scanner rather than
    /// about a text: the readiness probe of the analyzer the personal-data scanner reaches, which answers for a
    /// dependency nothing on this deployment need be using.
    /// </remarks>
    public IReadOnlyList<SensitiveContentScannerKind> Scanners { get; }

    /// <summary>Gets the redaction this owner's mail is read through, or <see langword="null" /> where nothing scans it.</summary>
    public SensitiveContentRedactor? Redactor { get; }

    /// <summary>Gets which findings stop this owner's outgoing message rather than being read for a placeholder.</summary>
    public SensitiveContentScreeningPolicy Screening { get; }

    /// <summary>Gets the configuration a derived row of this owner's mail records, present exactly when <see cref="Redactor" /> is.</summary>
    public SensitiveContentDerivationStamp? Stamp { get; }

    /// <summary>Gets whether anything is scanned for at all under this posture.</summary>
    public bool IsActive => this.Redactor is not null;

    /// <summary>Gets whether a finding under this posture can stop an outgoing message.</summary>
    public bool ScreensAnything => this.Redactor is not null && this.Screening.RefusesAnything;

    /// <summary>Reports whether one scanner runs over this owner's mail.</summary>
    /// <param name="scanner">The scanner to ask about.</param>
    /// <returns><see langword="true" /> when this posture runs it.</returns>
    public bool Runs(SensitiveContentScannerKind scanner) => this.Scanners.Contains(scanner);

    /// <summary>Composes the posture of an owner with at least one scanner switched on for their mail.</summary>
    /// <param name="scanners">Which scanners run over their mail, which is what the redaction below runs.</param>
    /// <param name="redactor">The redaction their mail is read through.</param>
    /// <param name="screening">Which of its findings stop their outgoing message.</param>
    /// <param name="stamp">The configuration a derived row of their mail records.</param>
    /// <returns>The posture every path scanning that owner's mail reads.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when no scanner is named, which is <see cref="ScanningNothing" /> rather than a posture.</exception>
    public static SensitiveContentPosture Scanning(
        IReadOnlyList<SensitiveContentScannerKind> scanners,
        SensitiveContentRedactor redactor,
        SensitiveContentScreeningPolicy screening,
        SensitiveContentDerivationStamp stamp)
    {
        ArgumentNullException.ThrowIfNull(scanners);
        ArgumentNullException.ThrowIfNull(redactor);
        ArgumentNullException.ThrowIfNull(screening);

        if (scanners.Count == 0)
        {
            throw new ArgumentException(
                "A posture that runs a redaction runs at least one scanner.",
                nameof(scanners));
        }

        return new SensitiveContentPosture([.. scanners], redactor, screening, stamp);
    }
}
