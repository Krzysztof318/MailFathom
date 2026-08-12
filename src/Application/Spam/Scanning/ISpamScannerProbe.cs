// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Spam.Scanning;

/// <summary>Establishes, before any mail is scored, that the scanner this deployment switched on answers at all.</summary>
/// <remarks>
/// <para>
/// <see cref="ISpamScanner" /> deliberately never raises: a scanner that cannot be reached leaves an occurrence with the
/// deterministic verdict, which is the right answer for one message and the wrong one for a deployment. An operator who
/// switched the scanner on and whose sidecar is absent would otherwise read their own configuration as a scanner being
/// consulted while every classification came from headers alone, and nothing on any serving path would say so.
/// </para>
/// <para>
/// So the question is asked once, while the host is coming up, and asked as a question of its own rather than as a
/// member of the scanning port. A scan asks what a message scores and answers with a result the caller continues past;
/// this asks whether there is a scanner there, and its failure exists to name the configuration key an operator has to
/// fix.
/// </para>
/// <para>
/// Nothing registers an implementation unless the scanner switch is on, so a deployment that never opted in probes
/// nothing.
/// </para>
/// </remarks>
public interface ISpamScannerProbe
{
    /// <summary>Verifies that the configured scanner answers, and establishes the corpus it scores under.</summary>
    /// <param name="cancellationToken">Cancels the probe.</param>
    /// <returns>A task that completes when the scanner answered.</returns>
    /// <exception cref="SpamScannerUnavailableException">Thrown when the scanner could not be reached, did not answer inside its bound, or answered unintelligibly.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken" /> is cancelled.</exception>
    Task VerifyAvailableAsync(CancellationToken cancellationToken);
}
