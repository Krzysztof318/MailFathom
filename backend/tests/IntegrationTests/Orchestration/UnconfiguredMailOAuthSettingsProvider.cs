// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Mail.OAuth;

namespace MailFathom.IntegrationTests.Orchestration;

/// <summary>The token endpoint port for an account that configures no OAuth block, which is every account this suite serves.</summary>
/// <remarks>
/// <para>
/// Infrastructure composes its access token source from this port, so the port has to be present for a mailbox session
/// factory to resolve at all. It is supplied here for the reason every other composition-root input is: the suite does
/// not start the host resource, so nothing binds the configuration the real provider reads.
/// </para>
/// <para>
/// Requesting settings throws, which is what a configured provider does for an account with no OAuth block rather than
/// a refusal invented for the suite. The orchestrated server advertises no token-bearing mechanism and
/// <see cref="SyntheticMailAccount" /> permits none, so a connection never selects one and never reaches this port; a
/// change that made it reachable would fail loudly here instead of authenticating against settings nobody configured.
/// </para>
/// </remarks>
internal sealed class UnconfiguredMailOAuthSettingsProvider : IMailOAuthSettingsProvider
{
    /// <inheritdoc />
    public Task<MailOAuthAccountSettings> GetSettingsAsync(string accountId, CancellationToken cancellationToken) =>
        throw new InvalidOperationException(
            "The orchestrated account authenticates with clear text and configures no OAuth block.");
}
