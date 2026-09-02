// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Infrastructure.Mail.OAuth;

/// <summary>Contains validated token endpoint settings and resolved secrets for one configured account.</summary>
/// <param name="AccountId">The normalized local account identifier.</param>
/// <param name="TokenEndpoint">The authorization server's token endpoint.</param>
/// <param name="ClientId">The registered application's client identifier.</param>
/// <param name="Scope">The space-delimited scopes the request asks for, empty when the account configured none.</param>
/// <param name="Grant">The grant the request exchanges.</param>
/// <param name="Material">
/// The client secret and refresh token this request resolved. This record is a carrier and does not own them: the
/// operation that requested the settings owns the material and must dispose it when it ends.
/// </param>
/// <remarks>The synthesized <see cref="object.ToString" /> is safe only because <see cref="MailOAuthClientMaterial" /> carries redacted secrets.</remarks>
public sealed record MailOAuthAccountSettings(
    string AccountId,
    Uri TokenEndpoint,
    string ClientId,
    string Scope,
    MailOAuthGrant Grant,
    MailOAuthClientMaterial Material);

/// <summary>Resolves token endpoint settings for accounts that authenticate with an access token.</summary>
/// <remarks>
/// The port carries behavior rather than exposing bound configuration, for the reason
/// <see cref="IImapAccountSettingsProvider" /> does: it resolves the account's secret references at the moment a token
/// is about to be requested and hands the material to the caller, so no settings object holds a live client secret
/// between requests.
/// </remarks>
public interface IMailOAuthSettingsProvider
{
    /// <summary>Gets token endpoint settings for one local account identifier, resolving its client secret and refresh token.</summary>
    /// <param name="accountId">The local account identifier.</param>
    /// <param name="cancellationToken">Cancels the secret resolution.</param>
    /// <returns>The settings, whose material the caller must dispose when its operation ends.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the account configured no OAuth block, which startup validation already refuses.</exception>
    Task<MailOAuthAccountSettings> GetSettingsAsync(string accountId, CancellationToken cancellationToken);
}
