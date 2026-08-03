// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Infrastructure.Security.OAuth;

namespace MailFathom.Host.Configuration.Access;

/// <summary>One external authorization server whose access tokens this deployment accepts.</summary>
/// <remarks>
/// <para>
/// A profile carries what MailFathom needs to find and trust a server's signing keys, and nothing about how a user signs
/// in. Interactive login, consent, multi-factor policy, client registration, and token issuance belong to the server the
/// operator already runs; MailFathom is a resource server that verifies what that server signed. Keycloak, Entra ID, Auth0,
/// and Okta are therefore configuration rather than code, and none of them appears in a type name anywhere below this
/// section — a difference between two servers is something discovery reports, never something a branch decides.
/// </para>
/// <para>
/// Several profiles are supported so a deployment can serve two populations — an internal directory and a partner's,
/// say — without one becoming able to speak for the other. Each profile validates against its own issuer and its own key
/// set, so a token signed by one server is refused when it claims the other's issuer, and a key identifier that happens
/// to collide across two servers resolves to the keys of the server the token names rather than to whichever was loaded
/// first.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The options framework materializes this type during configuration binding.")]
internal sealed class AuthorizationServerOptions
{
    /// <summary>Gets or sets the operator's own name for this profile, which diagnostics correlate on.</summary>
    /// <remarks>It never reaches a client and is never compared against anything a token carries; it exists so a startup message and a log line can say which profile they mean without printing an issuer URL.</remarks>
    public string? Name { get; set; }

    /// <summary>Gets or sets the authorization server's issuer identifier, for example <c>https://sso.example.test/realms/mailfathom</c>.</summary>
    /// <remarks>
    /// Copy it exactly as the authorization server publishes it, trailing slash included where there is one. It is
    /// compared against a token's <c>iss</c> by exact string equality, it decides which profile validates a token, and it
    /// is checked against the <c>issuer</c> the discovery document reports — so a value rewritten to look tidier is a
    /// configuration that starts cleanly and then refuses every token the server issues.
    /// </remarks>
    public string? Issuer { get; set; }

    /// <summary>Gets or sets the discovery document to read instead of the addresses derived from <see cref="Issuer" />.</summary>
    /// <remarks>
    /// <para>
    /// Left unset, MailFathom looks for the document where the MCP authorization specification says to look: the OAuth 2.0
    /// Authorization Server Metadata address first, then the two OpenID Connect Discovery addresses, taking the first
    /// that answers with a document whose own <c>issuer</c> matches. That covers every server this repository's
    /// documentation describes, so this setting is for one that publishes its metadata somewhere else entirely.
    /// </para>
    /// <para>
    /// It must sit on the same host and port as <see cref="Issuer" />. The address is operator-supplied rather than
    /// token-supplied, so it is not untrusted input, but it is the one setting that names something MailFathom will fetch —
    /// and a mistyped one pointing at an internal address would make the host fetch it on every key refresh. Tying it to
    /// the issuer's authority means the profile can only ever reach the server it already names.
    /// </para>
    /// </remarks>
    public string? MetadataAddress { get; set; }

    /// <summary>Gets the subjects this authorization server may authenticate, each its own stable identifier for one person.</summary>
    /// <remarks>
    /// <para>
    /// A tenant holds whoever the operator's identity platform holds, and a token proves which of them is asking rather
    /// than that they were meant to read this mailbox. MailFathom serves one configured owner's mail to everyone it lets
    /// in, so without this list every colleague who can obtain a token for this resource reads that owner's mail.
    /// </para>
    /// <para>
    /// Write the <c>sub</c> the server issues, which its administration console shows as the user's identifier — a UUID
    /// in Keycloak, <c>auth0|…</c> in Auth0, the object identifier in Entra ID. An email address is not it: a subject is
    /// what the server promises not to reuse, and an address is reassigned to whoever holds the mailbox next.
    /// </para>
    /// </remarks>
    public IList<string> AuthorizedSubjects { get; } = [];

    /// <summary>Gets whether anything at all was configured for this profile.</summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(this.Name)
        || !string.IsNullOrWhiteSpace(this.Issuer)
        || !string.IsNullOrWhiteSpace(this.MetadataAddress)
        || this.AuthorizedSubjects.Count > 0;

    /// <summary>Finds everything an operator must fix before this profile can validate a token.</summary>
    /// <returns>One message per faulty setting, relative to this profile, empty when the profile is usable.</returns>
    public IReadOnlyList<string> FindConfigurationErrors()
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(this.Name))
        {
            errors.Add($"{nameof(this.Name)} — every authorization server needs a name, because a startup message and a log line identify a profile by it rather than by its issuer.");
        }

        // The faulty value is described rather than quoted, unlike a name or an origin elsewhere in this section. A URL
        // an operator mistyped can carry user information or a query, so the one message that would name it is also the
        // one that could copy a credential into a startup log; the setting path already says which entry to look at.
        if (!OAuthIdentifierUri.IsWellFormed(this.Issuer))
        {
            errors.Add($"{nameof(this.Issuer)} — the configured value is not an issuer identifier; write an absolute https URL with no user information, no query, and no fragment, exactly as the authorization server publishes it.");

            return errors;
        }

        errors.AddRange(this.FindMetadataAddressErrors());
        errors.AddRange(this.FindAuthorizedSubjectErrors());

        return errors;
    }

    /// <summary>Reports the identities a token from this server must match to be served.</summary>
    /// <returns>The authorized subjects, each paired with this profile's issuer.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the profile has not passed <see cref="FindConfigurationErrors" />.</exception>
    /// <remarks>A subject is only unique within the server that issued it, so it is paired with the issuer here rather than compared on its own; two servers may both call someone <c>1</c> without either being wrong.</remarks>
    public IEnumerable<string> AuthorizedIdentities() =>
        this.AuthorizedSubjects.Select(subject => OAuthIdentity.IdentityOf(this.ValidatedIssuer(), subject.Trim()));

    /// <summary>Reports the issuer every comparison against this profile uses.</summary>
    /// <returns>The configured issuer, trimmed and otherwise unchanged.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the profile has not passed <see cref="FindConfigurationErrors" />.</exception>
    public string ValidatedIssuer() =>
        OAuthIdentifierUri.IsWellFormed(this.Issuer)
            ? this.Issuer.Trim()
            : throw new InvalidOperationException(
                "The profile's issuer was read before it was validated, so it is not usable as an issuer identifier.");

    /// <summary>Reports where this profile looks for the authorization server's discovery document.</summary>
    /// <returns>The configured override alone, or the candidate addresses derived from the issuer, in the order they are tried.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the profile has not passed <see cref="FindConfigurationErrors" />.</exception>
    public IReadOnlyList<string> MetadataAddresses() =>
        string.IsNullOrWhiteSpace(this.MetadataAddress)
            ? OAuthMetadataAddresses.ForIssuer(this.ValidatedIssuer())
            : [this.MetadataAddress.Trim()];

    private IEnumerable<string> FindAuthorizedSubjectErrors()
    {
        if (this.AuthorizedSubjects.Count == 0)
        {
            yield return $"{nameof(this.AuthorizedSubjects)} — an authorization server authenticates whoever its tenant holds, so a profile names the subjects it may authenticate; configure at least one, or every user who can obtain a token for this resource reads the configured owner's mail.";

            yield break;
        }

        var claimedSubjects = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (index, configuredSubject) in this.AuthorizedSubjects.Index())
        {
            var settingPath = $"{nameof(this.AuthorizedSubjects)}:{index}";

            // A subject is opaque, so nothing about its shape can be checked beyond its presence. It is quoted back
            // because it is an identifier the operator copied from their own console rather than a URL that could carry
            // a credential, and naming it is what lets them see which entry does not match the token being refused.
            if (string.IsNullOrWhiteSpace(configuredSubject))
            {
                yield return $"{settingPath} — a subject is the authorization server's own identifier for one person; write the value it issues as 'sub'.";
            }
            else if (!claimedSubjects.Add(configuredSubject.Trim()))
            {
                yield return $"{settingPath} — '{configuredSubject}' repeats a subject the list already carries.";
            }
        }
    }

    private IEnumerable<string> FindMetadataAddressErrors()
    {
        if (string.IsNullOrWhiteSpace(this.MetadataAddress))
        {
            yield break;
        }

        if (!Uri.TryCreate(this.MetadataAddress.Trim(), UriKind.Absolute, out var metadataAddress)
            || metadataAddress.Scheme != Uri.UriSchemeHttps)
        {
            yield return $"{nameof(this.MetadataAddress)} — the configured value is not an absolute https URL.";

            yield break;
        }

        var issuer = new Uri(this.ValidatedIssuer());
        var sitsOnTheIssuersServer =
            string.Equals(metadataAddress.Host, issuer.Host, StringComparison.OrdinalIgnoreCase)
            && metadataAddress.Port == issuer.Port;

        if (!sitsOnTheIssuersServer)
        {
            yield return $"{nameof(this.MetadataAddress)} — the discovery document must sit on the same host and port as {nameof(this.Issuer)}, so a mistyped address cannot make the host fetch something the profile never named.";
        }
    }
}
