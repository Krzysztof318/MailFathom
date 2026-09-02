// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Infrastructure.Mail.OAuth;

/// <summary>Identifies which OAuth 2.0 grant an account exchanges for an access token.</summary>
/// <remarks>
/// <para>
/// The value is a closed enumeration rather than a C# <see langword="enum" /> because the member is inseparable from
/// the <c>grant_type</c> string RFC 6749 puts on the wire: an operator writes that name in configuration, the token
/// request sends it verbatim, and a numeric member value would carry no meaning at either end.
/// </para>
/// <para>
/// Only the two grants a headless process can complete are members. The authorization-code grant is absent by design
/// rather than by omission — MailFathom has no console and serves no redirect callback, so a refresh token is obtained
/// out of band by the operator and supplied as a secret reference, exactly as a password is.
/// </para>
/// </remarks>
public readonly record struct MailOAuthGrant
{
    private readonly string? grantTypeName;

    private MailOAuthGrant(string grantTypeName, bool requiresRefreshToken)
    {
        this.grantTypeName = grantTypeName;
        this.RequiresRefreshToken = requiresRefreshToken;
    }

    /// <summary>Gets the grant that exchanges an operator-supplied refresh token for an access token.</summary>
    /// <remarks>This is the delegated path: the token acts for one mailbox owner, and Google offers no other route for a Workspace mailbox.</remarks>
    public static MailOAuthGrant RefreshToken { get; } = new("refresh_token", requiresRefreshToken: true);

    /// <summary>Gets the app-only grant that authenticates the registered application itself.</summary>
    /// <remarks>This is what Exchange Online's app-only access uses, and it needs no human present at any point.</remarks>
    public static MailOAuthGrant ClientCredentials { get; } = new("client_credentials", requiresRefreshToken: false);

    /// <summary>Gets every supported grant.</summary>
    /// <remarks>Declared last so the members it lists are already initialized when this initializer runs.</remarks>
    public static IReadOnlyList<MailOAuthGrant> All { get; } = [RefreshToken, ClientCredentials];

    /// <summary>Gets whether this value names a supported grant rather than the unusable struct default.</summary>
    public bool IsSpecified => this.grantTypeName is not null;

    /// <summary>Gets whether the grant requires the account to configure a refresh token.</summary>
    public bool RequiresRefreshToken { get; }

    /// <summary>Gets the <c>grant_type</c> value the token request sends.</summary>
    /// <exception cref="InvalidOperationException">Thrown when the value is the struct default rather than a grant.</exception>
    public string GrantTypeName => this.grantTypeName
        ?? throw new InvalidOperationException("The value is the default of the struct and does not name an OAuth grant.");

    /// <summary>Parses an operator-supplied grant name, ignoring case and surrounding whitespace.</summary>
    /// <param name="grantTypeName">The configured grant name.</param>
    /// <param name="grant">The parsed grant when the name is supported; otherwise the unspecified default.</param>
    /// <returns><see langword="true" /> when the name is a supported grant; otherwise <see langword="false" />.</returns>
    public static bool TryParseGrantTypeName(string? grantTypeName, out MailOAuthGrant grant)
    {
        grant = default;
        if (string.IsNullOrWhiteSpace(grantTypeName))
        {
            return false;
        }

        var normalizedName = grantTypeName.Trim();

        grant = All.FirstOrDefault(candidate => StringComparer.OrdinalIgnoreCase.Equals(candidate.GrantTypeName, normalizedName));

        return grant.IsSpecified;
    }

    /// <inheritdoc />
    public override string ToString() => this.grantTypeName ?? "(unspecified)";
}
