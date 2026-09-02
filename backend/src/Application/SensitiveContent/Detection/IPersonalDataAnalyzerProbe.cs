// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.SensitiveContent.Detection;

/// <summary>Establishes that the personal-data analyzer this deployment configured answers, and can be asked again whenever that has to be established.</summary>
/// <remarks>
/// <para>
/// The personal-data scanner reaches an analyzer deployed beside the service, and the port above it fails closed: with
/// the <c>Pii</c> switch on and that analyzer absent, every guarded read, write, and egress is refused. A deployment in
/// that state is not degraded, it is stopped — which is why the question is asked directly rather than discovered one
/// refused operation at a time, and why the answer removes an instance from traffic rather than softening what it
/// serves.
/// </para>
/// <para>
/// The analyzer is a sidecar with a lifetime of its own, so the answer is not a fact about start-up. It may become
/// reachable after this process does and may stop answering long afterwards, which is why the readiness probe asks on
/// every scrape rather than a gate asking once.
/// </para>
/// <para>
/// It is a port of its own rather than a member of <see cref="ISensitiveContentScanner" /> because the two ask different
/// questions of the same analyzer. A scan asks what a text carries and says nothing an operator can act on; this asks
/// whether the analyzer is there at all, and its failure exists precisely to name the configuration key an operator has to
/// fix. Neither one puts the analyzer's address in a message: this one carries it on the failure's own property instead.
/// </para>
/// <para>
/// Nothing registers an implementation unless the switch is on, so a deployment that never opted in never probes
/// anything.
/// </para>
/// </remarks>
public interface IPersonalDataAnalyzerProbe
{
    /// <summary>
    /// Verifies that the configured analyzer answers, and that it recognises at least one entity of every category this
    /// deployment switched on.
    /// </summary>
    /// <param name="cancellationToken">Cancels the probe.</param>
    /// <returns>A task that completes when the analyzer answered for every switched-on category.</returns>
    /// <exception cref="PersonalDataAnalyzerUnavailableException">Thrown when the analyzer could not be reached, refused the probe, or recognises no entity of a switched-on category — the last of which would otherwise be scanned for and never found, which reads exactly like a clean message.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken" /> is cancelled.</exception>
    Task VerifyAvailableAsync(CancellationToken cancellationToken);
}
