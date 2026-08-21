// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using MailFathom.Domain.Failures;

namespace MailFathom.Application.SensitiveContent.Detection;

/// <summary>The failure raised when a scanner an operator switched on could not establish what a text carries.</summary>
/// <remarks>
/// <para>
/// This is the whole of the fail-closed contract. An opt-in that degraded to handing the text on under load would be
/// worse than no switch at all, because the operator would believe the protection was in force, so a detector that is
/// unreachable, slow past its budget, or broken blocks the operation it guards rather than letting it through.
/// </para>
/// <para>
/// The message names the scanner and, where a budget was spent, how much of one. It names no text, no finding, and no
/// endpoint: none of them is something the caller can act on, and the content the scan was about is exactly what must
/// not appear in a failure written to a log.
/// </para>
/// </remarks>
public sealed class SensitiveContentScannerUnavailableException : MailFathomException
{
    private SensitiveContentScannerUnavailableException(
        string operatorSafeMessage,
        SensitiveContentScannerKind scanner)
        : base(operatorSafeMessage) => this.Scanner = scanner;

    private SensitiveContentScannerUnavailableException(
        string operatorSafeMessage,
        SensitiveContentScannerKind scanner,
        Exception innerException)
        : base(operatorSafeMessage, innerException) => this.Scanner = scanner;

    /// <summary>Gets which scanner could not answer.</summary>
    public SensitiveContentScannerKind Scanner { get; }

    /// <inheritdoc />
    public override MailFathomErrorCode ErrorCode => MailFathomErrorCode.SensitiveContentScannerUnavailable;

    /// <summary>Refuses an operation whose scanner did not answer inside the configured budget.</summary>
    /// <param name="scanner">The scanner that did not answer.</param>
    /// <param name="budget">The per-call budget it spent.</param>
    /// <returns>The failure to raise.</returns>
    public static SensitiveContentScannerUnavailableException DidNotAnswerInTime(
        SensitiveContentScannerKind scanner,
        TimeSpan budget) => new(
        string.Format(
            CultureInfo.InvariantCulture,
            "The {0} scanner did not answer within {1}, so the operation it guards was refused rather than served unscanned.",
            scanner,
            budget),
        scanner);

    /// <summary>Refuses an operation whose scanner failed.</summary>
    /// <param name="scanner">The scanner that failed.</param>
    /// <param name="failure">The failure it raised, which stays diagnostic detail for a log.</param>
    /// <returns>The failure to raise.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="failure" /> is <see langword="null" />.</exception>
    public static SensitiveContentScannerUnavailableException Failed(
        SensitiveContentScannerKind scanner,
        Exception failure)
    {
        ArgumentNullException.ThrowIfNull(failure);

        return new SensitiveContentScannerUnavailableException(
            string.Format(
                CultureInfo.InvariantCulture,
                "The {0} scanner failed, so the operation it guards was refused rather than served unscanned.",
                scanner),
            scanner,
            failure);
    }

    /// <summary>Refuses an operation whose scanner is switched on and absent from the running deployment.</summary>
    /// <param name="scanner">The scanner nothing registered.</param>
    /// <returns>The failure to raise.</returns>
    /// <remarks>
    /// Startup already refuses this configuration, so reaching it means a scanner disappeared after the deployment
    /// started. Refusing here as well is what keeps the guarantee a property of the operation rather than of a check
    /// that ran once at boot.
    /// </remarks>
    public static SensitiveContentScannerUnavailableException NotRegistered(SensitiveContentScannerKind scanner) => new(
        string.Format(
            CultureInfo.InvariantCulture,
            "The {0} scanner is switched on and this deployment registers no detector for it, so the operation it guards was refused rather than served unscanned.",
            scanner),
        scanner);
}
