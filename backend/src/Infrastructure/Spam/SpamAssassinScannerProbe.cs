// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Spam.Scanning;
using MailFathom.CodeCoverage;

namespace MailFathom.Infrastructure.Spam;

/// <summary>Asks the configured daemon, once, whether it is there and what it calls itself.</summary>
/// <remarks>
/// <para>
/// The probe scores one synthetic message rather than pinging the socket, and the difference is what each proves. A ping
/// is answered by a daemon that has accepted a connection; a scan is answered only by one that has loaded its rules and
/// can run them, which is the state the deployment actually needs and the slower of the two to reach. The same answer
/// carries the release the daemon is, which is what every scan afterwards records as its corpus — so the check and the
/// identity are one exchange rather than two.
/// </para>
/// <para>
/// One attempt, not a wait loop. A daemon still fetching its rule updates is reached by the orchestrator restarting this
/// process, which is the same answer the database schema gate gives, and a gate that waited would turn a misconfigured
/// address into a host that never finishes starting and never says why.
/// </para>
/// <para>
/// Marked as verified by the integration suite because both of its outcomes are a real daemon's: one is what an absent
/// one produces and the other is what a present one answers, and a substitute in between would be proving that the
/// mapping maps rather than that a startup refusal happens for the reason it is documented to.
/// </para>
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class SpamAssassinScannerProbe : ISpamScannerProbe
{
    private readonly SpamAssassinDaemon daemon;

    /// <summary>Initializes the probe over one configured daemon.</summary>
    /// <param name="daemon">The conversation with that daemon.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="daemon" /> is <see langword="null" />.</exception>
    public SpamAssassinScannerProbe(SpamAssassinDaemon daemon)
    {
        ArgumentNullException.ThrowIfNull(daemon);

        this.daemon = daemon;
    }

    /// <inheritdoc />
    public async Task VerifyAvailableAsync(CancellationToken cancellationToken)
    {
        try
        {
            _ = await this.daemon.IdentifyCorpusAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // The host is shutting down while it starts, which is not a verdict on the daemon.
            throw;
        }
        catch (InvalidOperationException)
        {
            // Something answered and it was not a spam daemon. Reported as its own state, because the address is
            // reachable and correcting it is a different act from starting a container.
            throw SpamScannerUnavailableException.NotASpamDaemon(this.daemon.Endpoint);
        }
        catch (Exception failure)
        {
            throw SpamScannerUnavailableException.NotReached(this.daemon.Endpoint, failure);
        }
    }
}
