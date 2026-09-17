// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Net;
using System.Security.Claims;
using MailFathom.Domain.Access;
using MailFathom.Host.Configuration.Access;
using MailFathom.Host.Configuration.Endpoints;

namespace MailFathom.Host.Security.Transport;

/// <summary>What admitting one configured administrator means on a request: the name it is attributed to, the grant it holds, and the networks it may act from.</summary>
/// <remarks>
/// <para>
/// Composed once per administrator while the host is composed, so nothing per request re-reads a configuration section
/// or re-parses a network. Every scheme on the administrative surface hands the credential it validated to the one
/// admission its administrator was composed into, which is what keeps a key, a public key, and a token from answering
/// the network question three ways.
/// </para>
/// <para>
/// A request from outside the administrator's networks is refused as an authentication failure, indistinguishable to
/// the caller from a credential that never matched. The operator is told otherwise: the refusal is recorded with the
/// administrator's name and the address observed, because a leaked credential tried from the wrong network is exactly
/// the event worth finding in a log.
/// </para>
/// </remarks>
internal sealed partial class AdministratorAdmission
{
    private readonly IReadOnlyList<IPNetwork> allowedSourceNetworks;

    private AdministratorAdmission(
        string name,
        IReadOnlyList<MailFathomPermission> grant,
        bool grantNarrowedByTokenScopes,
        IReadOnlyList<IPNetwork> allowedSourceNetworks)
    {
        this.Name = name;
        this.Grant = grant;
        this.GrantNarrowedByTokenScopes = grantNarrowedByTokenScopes;
        this.allowedSourceNetworks = allowedSourceNetworks;
    }

    /// <summary>Gets the name every act this administrator performs is attributed to.</summary>
    internal string Name { get; }

    /// <summary>Gets the permissions this administrator holds, which is the ceiling where a token's scopes narrow it.</summary>
    internal IReadOnlyList<MailFathomPermission> Grant { get; }

    /// <summary>Gets whether a token admitting this administrator holds only the part of <see cref="Grant" /> its own scopes carry.</summary>
    internal bool GrantNarrowedByTokenScopes { get; }

    /// <summary>Composes the admission of one validated administrator.</summary>
    /// <param name="administrator">The administrator, which has passed its configuration errors.</param>
    /// <returns>The admission.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="administrator" /> is <see langword="null" />.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the administrator has not passed its configuration errors.</exception>
    /// <exception cref="FormatException">Thrown when an entry of <see cref="AdministratorOptions.AllowedSourceNetworks" /> has not passed those errors.</exception>
    internal static AdministratorAdmission For(AdministratorOptions administrator)
    {
        ArgumentNullException.ThrowIfNull(administrator);

        return new AdministratorAdmission(
            administrator.ValidatedName(),
            administrator.GrantedPermissions(),
            administrator.PermissionsFromTokenScopes,
            administrator.SourceNetworks());
    }

    /// <summary>Composes one admission per administrator, so every credential under one administrator shares it.</summary>
    /// <param name="administrators">The validated administrators.</param>
    /// <returns>Each administrator's admission, keyed by the administrator's own settings object.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="administrators" /> is <see langword="null" />.</exception>
    internal static IReadOnlyDictionary<AdministratorOptions, AdministratorAdmission> ForEach(
        IEnumerable<AdministratorOptions> administrators)
    {
        ArgumentNullException.ThrowIfNull(administrators);

        return administrators.ToDictionary(administrator => administrator, For);
    }

    /// <summary>Reports whether a request arrived from a network this administrator may act from, and records it when it did not.</summary>
    /// <param name="context">The request being authenticated.</param>
    /// <param name="schemeName">The scheme that validated the credential, which the refusal record names.</param>
    /// <param name="logger">Where a refusal is recorded.</param>
    /// <returns><see langword="true" /> when the administrator is unrestricted or the client address falls inside one of its networks.</returns>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    /// <remarks>
    /// The address is the one the forwarded-headers policy left on the connection, which behind a named proxy is the
    /// client's own. A request whose address is unknown is refused wherever a restriction applies, because nothing about
    /// it can be shown to be inside one.
    /// </remarks>
    internal bool AdmitsSourceOf(HttpContext context, string schemeName, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(schemeName);
        ArgumentNullException.ThrowIfNull(logger);

        if (this.allowedSourceNetworks.Count == 0)
        {
            return true;
        }

        var source = context.Connection.RemoteIpAddress;

        if (source is not null && ConfiguredAddressRanges.Contain(this.allowedSourceNetworks, source))
        {
            return true;
        }

        LogSourceRefused(
            logger,
            this.Name,
            source is null ? "unknown" : ConfiguredAddressRanges.InComparableForm(source).ToString(),
            schemeName,
            context.TraceIdentifier);

        return false;
    }

    /// <summary>Builds the identity a credential admitting this administrator produces.</summary>
    /// <param name="credentialName">The configured name of the credential that authenticated.</param>
    /// <param name="credentialClaimType">The claim type that name travels as, which is the scheme's own private type.</param>
    /// <param name="roleClaimType">The claim type a role check reads, which nothing ever issues.</param>
    /// <param name="schemeName">The authentication type the identity reports.</param>
    /// <returns>An identity named by the administrator, carrying the credential's name and the grant.</returns>
    /// <exception cref="ArgumentException">Thrown when a name or claim type is <see langword="null" />, empty, or white space.</exception>
    /// <remarks>
    /// The credential's name stays on the identity beside the administrator's, because it is what tells a credential
    /// this deployment holds from a token an authorization server issued; the administrator's name is what the identity
    /// is named by, so every reader of a caller's name — the session read, the rate limiter, a log scope — reads the
    /// person or system rather than the key.
    /// </remarks>
    internal ClaimsIdentity IdentityFor(
        string credentialName,
        string credentialClaimType,
        string roleClaimType,
        string schemeName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(credentialName);
        ArgumentException.ThrowIfNullOrWhiteSpace(credentialClaimType);

        return this.IdentityOver([new Claim(credentialClaimType, credentialName)], this.Grant, schemeName, roleClaimType);
    }

    /// <summary>Builds the identity a validated token admitting this administrator produces.</summary>
    /// <param name="tokenIdentity">The minimal identity kept from the validated token.</param>
    /// <param name="roleClaimType">The claim type a role check reads, which nothing ever issues.</param>
    /// <returns>An identity named by the administrator, carrying the token's claims and the grant the token holds.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="tokenIdentity" /> is <see langword="null" />.</exception>
    internal ClaimsIdentity IdentityForToken(ClaimsIdentity tokenIdentity, string roleClaimType)
    {
        ArgumentNullException.ThrowIfNull(tokenIdentity);

        return this.IdentityOver(
            tokenIdentity.Claims,
            TransportGrant.HeldByToken(tokenIdentity, this.Grant, this.GrantNarrowedByTokenScopes),
            tokenIdentity.AuthenticationType!,
            roleClaimType);
    }

    private ClaimsIdentity IdentityOver(
        IEnumerable<Claim> credentialClaims,
        IReadOnlyList<MailFathomPermission> held,
        string schemeName,
        string roleClaimType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(schemeName);
        ArgumentException.ThrowIfNullOrWhiteSpace(roleClaimType);

        return new ClaimsIdentity(
            [
                .. credentialClaims,
                new Claim(TransportCallerIdentity.AdministratorClaimType, this.Name),
                .. TransportGrant.ClaimsFor(held),
            ],
            schemeName,
            TransportCallerIdentity.AdministratorClaimType,
            roleClaimType);
    }

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "A credential of administrator {AdministratorName} was presented to {AuthenticationScheme} from "
            + "{SourceAddress}, which none of the administrator's allowed source networks holds, so the request was "
            + "refused as unauthenticated. TraceIdentifier={TraceIdentifier}.")]
    private static partial void LogSourceRefused(
        ILogger logger,
        string administratorName,
        string sourceAddress,
        string authenticationScheme,
        string traceIdentifier);
}
