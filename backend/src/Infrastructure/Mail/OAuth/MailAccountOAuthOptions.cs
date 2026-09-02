// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Secrets.Discovery;
using MailFathom.Infrastructure.Secrets.References;
using MailFathom.Infrastructure.Secrets.Resolution;

namespace MailFathom.Infrastructure.Mail.OAuth;

/// <summary>Binds one account's OAuth settings and resolves the secrets a token request needs.</summary>
/// <remarks>
/// <para>
/// This is the configuration adapter for an account's token acquisition, alongside
/// <see cref="MailAccountSecretOptions" /> for its password and
/// <see cref="MailAccountTransportSecurityOptions" /> for its transport rules. It stays mutable and binder-friendly,
/// and every credential it names arrives as a reference, so an operator's configuration file holds no client secret
/// and no refresh token.
/// </para>
/// <para>
/// MailFathom never obtains a refresh token: it has no console and serves no redirect callback, so the authorization
/// code is exchanged out of band by the operator and the resulting refresh token is provisioned like any other secret.
/// <c>docs/operations/mailbox-oauth.md</c> carries the recipe for each provider.
/// </para>
/// </remarks>
public sealed class MailAccountOAuthOptions
{
    /// <summary>Gets or sets the RFC 6749 grant this account exchanges, either <c>refresh_token</c> or <c>client_credentials</c>.</summary>
    public string Grant { get; set; } = string.Empty;

    /// <summary>Gets or sets the authorization server's token endpoint, which must be an absolute HTTPS address.</summary>
    /// <remarks>
    /// The scheme is not a preference. The request carries the client secret and the refresh token in its body, so an
    /// <c>http</c> endpoint would publish both to anyone on the path, and startup validation refuses one.
    /// </remarks>
    public string TokenEndpoint { get; set; } = string.Empty;

    /// <summary>Gets or sets the registered application's client identifier.</summary>
    /// <remarks>It stays a plain configuration value because it is an identifier rather than a credential, the same way the mailbox user name is.</remarks>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>Gets or sets the space-delimited scopes the token request asks for.</summary>
    /// <remarks>
    /// One string rather than a list, because RFC 6749 defines <c>scope</c> as exactly that and the request sends it
    /// unchanged. A bound list would also let the configuration binder drop a single malformed element silently,
    /// leaving a token request that asks for less than the operator wrote.
    /// </remarks>
    public string Scope { get; set; } = string.Empty;

    /// <summary>Gets or sets whether the application is registered as a public client, which holds no secret.</summary>
    /// <remarks>
    /// A public client authenticates by proving possession of the grant alone, which is the registration Microsoft
    /// Entra expects for the device flow and the one <c>mfctl mailbox authorize --public-client</c> produces. It
    /// is an explicit setting rather than an inference from a missing reference, because "no secret configured" and
    /// "no secret exists" are the same shape and only one of them is safe to accept: inferring it would turn a
    /// forgotten reference on a confidential client into a silently unauthenticated token request.
    /// </remarks>
    public bool PublicClient { get; set; }

    /// <summary>Gets or sets the reference to the registered application's client secret, absent for a public client and when the account authenticates with a password.</summary>
    /// <remarks>
    /// The block is nullable and defaults to absent rather than to an empty block, and that is a correctness
    /// requirement rather than a style choice. Secret discovery walks the bound options graph by type and resolves
    /// every <see cref="ConfiguredSecret" /> it finds, so an empty block left here by default would be discovered for
    /// every password-authenticated account and fail startup with an unresolvable reference the operator never wrote.
    /// </remarks>
    public ConfiguredSecret? ClientSecret { get; set; }

    /// <summary>Gets or sets the reference to the operator-provisioned refresh token, used only by the refresh-token grant.</summary>
    /// <remarks>Absent by default for the reason <see cref="ClientSecret" /> states, and absent by design for the client-credentials grant, which has no refresh token.</remarks>
    public ConfiguredSecret? RefreshToken { get; set; }

    /// <summary>Gets the parsed grant, or the unspecified default when the configured name is not supported.</summary>
    public MailOAuthGrant ParsedGrant => MailOAuthGrant.TryParseGrantTypeName(this.Grant, out var grant) ? grant : default;

    /// <summary>Gets whether the operator configured this block at all.</summary>
    /// <remarks>An account authenticating with a password leaves every member empty, and validation then never reads the block.</remarks>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(this.Grant)
        || !string.IsNullOrWhiteSpace(this.TokenEndpoint)
        || !string.IsNullOrWhiteSpace(this.ClientId);

    /// <summary>Resolves the client secret and, when the grant needs one, the refresh token.</summary>
    /// <param name="resolver">The resolver that turns references into material.</param>
    /// <param name="cancellationToken">Cancels the secret resolution.</param>
    /// <returns>The owned material, which the caller must dispose when its operation ends.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="resolver" /> is <see langword="null" />.</exception>
    /// <exception cref="InvalidOperationException">Thrown when a reference that passed startup validation no longer resolves.</exception>
    /// <remarks>The exception is a fail-closed path rather than an ordinary branch: startup already proved each reference resolves, so a failure here means the material disappeared underneath a running deployment.</remarks>
    public async Task<MailOAuthClientMaterial> ResolveClientMaterialAsync(
        ISecretReferenceResolver resolver,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(resolver);

        // A public client has no secret to resolve, and validation has already refused the combination where one was
        // expected and configured nothing.
        ResolvedSecret? clientSecret = null;
        if (!this.PublicClient)
        {
            var clientSecretResult = await resolver.ResolveAsync(this.ClientSecret?.SecretReference, cancellationToken);
            clientSecret = clientSecretResult.Secret ?? throw new InvalidOperationException(
                $"The account OAuth client secret reference could not be resolved [{clientSecretResult.Failure}].");
        }

        if (!this.ParsedGrant.RequiresRefreshToken)
        {
            return new MailOAuthClientMaterial(clientSecret, RefreshToken: null);
        }

        try
        {
            var refreshTokenResult = await resolver.ResolveAsync(this.RefreshToken?.SecretReference, cancellationToken);
            var refreshToken = refreshTokenResult.Secret ?? throw new InvalidOperationException(
                $"The account OAuth refresh token reference could not be resolved [{refreshTokenResult.Failure}].");

            return new MailOAuthClientMaterial(clientSecret, refreshToken);
        }
        catch
        {
            // The client secret is already owned material, and the caller never receives it when this throws.
            clientSecret?.Dispose();
            throw;
        }
    }
}
