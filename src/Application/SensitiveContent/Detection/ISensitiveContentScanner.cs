// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.SensitiveContent.Detection;

/// <summary>Detects sensitive material in text, whatever runs the detection and wherever it runs.</summary>
/// <remarks>
/// <para>
/// One port covers a detector compiled into this process and one reached over the network, because in-process or over
/// HTTP is an adapter's business and nothing above this line is written differently for the two. No detection-library
/// type, HTTP type, model type, tokenizer, or provider SDK type crosses it.
/// </para>
/// <para>
/// The port is synchronous with respect to the operation it guards: a caller that cannot get an answer does not
/// proceed. An implementation therefore raises <see cref="SensitiveContentScannerUnavailableException" /> rather than
/// returning no findings when it could not run, because an empty answer and an unanswerable one lead a consumer to
/// opposite decisions and only one of them is safe.
/// </para>
/// <para>
/// An implementation reads which categories to look for, and which rules inside them to leave alone, from the
/// <see cref="SensitiveContentScannerPlan" /> composed for its <see cref="Scanner" />. Bounding the analyzed length and
/// the time one call may take is not its work: <see cref="Redaction.SensitiveContentRedactor" /> applies both before
/// the text arrives, so every scanner is bounded identically rather than each in its own way.
/// </para>
/// </remarks>
public interface ISensitiveContentScanner
{
    /// <summary>Gets which of the two switches this scanner belongs to.</summary>
    SensitiveContentScannerKind Scanner { get; }

    /// <summary>Scans text and reports every region of it the configured categories cover.</summary>
    /// <param name="text">The text to analyze, already bounded by the caller.</param>
    /// <param name="cancellationToken">Cancels the scan.</param>
    /// <returns>The findings, in no guaranteed order, and empty when the text carries nothing the scanner looks for.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="text" /> is <see langword="null" />.</exception>
    /// <exception cref="SensitiveContentScannerUnavailableException">Thrown when the scanner could not establish what the text carries.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken" /> is cancelled.</exception>
    /// <remarks>Findings point into <paramref name="text" /> by offset and never carry any part of what was found.</remarks>
    Task<IReadOnlyList<SensitiveContentFinding>> ScanAsync(string text, CancellationToken cancellationToken);
}
