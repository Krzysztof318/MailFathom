// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Security.Claims;
using System.Text.Encodings.Web;
using MailFathom.Host.Security.ApiKeys;
using MailFathom.Host.Security.Transport;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace MailFathom.Host.Security.ClientAssertions;

/// <summary>Authenticates a request against the public keys this deployment registered for its users.</summary>
/// <remarks>
/// <para>
/// The handler is the adapter and nothing more: it lifts the credential out of the request, hands it to
/// <see cref="UserClientAssertionAuthenticator" />, and turns the answer into the framework's own vocabulary. Every
/// rule worth asserting — which key verified it, what the assertion had to claim, whether its identifier had been spent
/// — lives below this boundary, where a test reaches it without a request pipeline.
/// </para>
/// <para>
/// It carries no key list, which is the whole difference between it and the handler beside it. An assertion names the
/// fingerprint of the key that signed it, that fingerprint resolves a credential row, and the user and the grant both
/// arrive from the row rather than from a configuration entry the host resolved at startup.
/// </para>
/// <para>
/// Every refusal produces one indistinguishable answer: an empty <c>401</c> carrying the same challenge every other
/// method returns.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The authentication framework materializes this handler for its registered scheme.")]
internal sealed class UserClientAssertionAuthenticationHandler
    : AuthenticationHandler<UserClientAssertionAuthenticationSchemeOptions>
{
    private readonly UserClientAssertionAuthenticator authenticator;

    /// <summary>Initializes a new user client assertion authentication handler.</summary>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="authenticator" /> is <see langword="null" />.</exception>
    public UserClientAssertionAuthenticationHandler(
        IOptionsMonitor<UserClientAssertionAuthenticationSchemeOptions> schemeOptions,
        ILoggerFactory loggerFactory,
        UrlEncoder urlEncoder,
        UserClientAssertionAuthenticator authenticator)
        : base(schemeOptions, loggerFactory, urlEncoder)
    {
        ArgumentNullException.ThrowIfNull(authenticator);

        this.authenticator = authenticator;
    }

    /// <inheritdoc />
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var result = await this.authenticator.AuthenticateAsync(
            this.Options.Surface.ClientAssertionAudience,
            this.Request.Headers.Authorization.ToString(),
            this.Context.RequestAborted);

        if (result.Admitted is not { } admitted)
        {
            return AuthenticateResult.Fail("The request presented no usable credential.");
        }

        var identity = TransportGrant.IdentityFor(
            admitted.CredentialId.ToString("D", CultureInfo.InvariantCulture),
            ClientAssertionAuthentication.KeyNameClaimType,
            ClientAssertionAuthentication.RoleClaimType,
            this.Options.Surface.ClientAssertionSchemeName,
            admitted.Permissions);

        // The user is what the registered key resolved, so the principal carries them rather than leaving the surface
        // to answer for whose mail the request acts on. The credential travels beside them because a session minted
        // from this request outlives it, and disabling the credential is what ends that session.
        identity.AddClaim(TransportCallerUser.ClaimFor(admitted.User));
        identity.AddClaim(TransportCallerCredential.ClaimFor(admitted.CredentialId));

        return AuthenticateResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(identity), this.Scheme.Name));
    }

    /// <inheritdoc />
    /// <remarks>The same bare challenge every other method on the surface produces, written by the API key scheme because a client is told which protection space to hold a credential for and never which method judged it.</remarks>
    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        ApiKeyAuthentication.WriteBareChallenge(this.Response);

        return Task.CompletedTask;
    }
}

/// <summary>Which surface a user-facing assertion scheme protects, and therefore which audience it requires.</summary>
/// <remarks>
/// There is no key list here and no grant, unlike the configured scheme's options: the keys are rows in the
/// deployment's own database and what each one grants is recorded beside the user it resolves. What is left is the
/// surface, which names the audience an assertion must carry and the identity a success reports itself under.
/// </remarks>
internal sealed class UserClientAssertionAuthenticationSchemeOptions : AuthenticationSchemeOptions
{
    /// <summary>Gets or sets the surface this scheme protects, which names the audience an assertion must carry.</summary>
    internal TransportSurface Surface { get; set; }

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">Thrown when the scheme was registered without a surface, which would leave the audience an assertion is judged against unstated.</exception>
    public override void Validate()
    {
        base.Validate();

        if (!this.Surface.IsSpecified)
        {
            throw new InvalidOperationException(
                "The user client assertion authentication scheme was registered without a transport surface.");
        }
    }
}
