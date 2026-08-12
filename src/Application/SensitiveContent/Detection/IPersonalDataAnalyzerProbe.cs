// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.SensitiveContent.Detection;

/// <summary>Establishes, before anything is scanned, that the personal-data analyzer this deployment configured answers.</summary>
/// <remarks>
/// <para>
/// The personal-data scanner reaches an analyzer deployed beside the service, and the port above it fails closed: with
/// the <c>Pii</c> switch on and that analyzer absent, every guarded read, write, and egress is refused. A deployment in
/// that state is not degraded, it is stopped, so the question is asked once while the host is coming up rather than
/// discovered one refused operation at a time.
/// </para>
/// <para>
/// It is a port of its own rather than a member of <see cref="ISensitiveContentScanner" /> because the two ask different
/// questions of the same analyzer. A scan asks what a text carries and must never name the analyzer in its failure; this
/// asks whether the analyzer is there at all, and its failure exists precisely to name the address and the configuration
/// key an operator has to fix.
/// </para>
/// <para>
/// Nothing registers an implementation unless the switch is on, so a deployment that never opted in never probes
/// anything.
/// </para>
/// </remarks>
public interface IPersonalDataAnalyzerProbe
{
    /// <summary>Verifies that the configured analyzer answers and understands the language it is configured for.</summary>
    /// <param name="cancellationToken">Cancels the probe.</param>
    /// <returns>A task that completes when the analyzer answered.</returns>
    /// <exception cref="PersonalDataAnalyzerUnavailableException">Thrown when the analyzer could not be reached, refused the probe, or answered without the configured language.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken" /> is cancelled.</exception>
    Task VerifyAvailableAsync(CancellationToken cancellationToken);
}
