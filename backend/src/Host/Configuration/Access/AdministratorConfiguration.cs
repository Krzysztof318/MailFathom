// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using MailFathom.Infrastructure.Secrets.Discovery;
using MailFathom.Infrastructure.Security.OAuth;

namespace MailFathom.Host.Configuration.Access;

/// <summary>The rules the administrative endpoint's list of administrators follows as a whole.</summary>
/// <remarks>
/// <para>
/// An administrator answers for itself in <see cref="AdministratorOptions" />. What lives here is what only the list can
/// answer: that no two administrators share a name, that every token names exactly one of them, and that the OAuth
/// blocks spread across them still describe one protected resource and one validator per authorization server.
/// </para>
/// <para>
/// It is also where a request's credential is turned back into the administrator it admits. A key and a public key are
/// looked up by their configured name, which the secret validator already requires to be unique across the section; a
/// token is looked up by the issuer and subject it carries, which <see cref="FindConfigurationErrors" /> requires to name
/// one administrator.
/// </para>
/// </remarks>
internal static class AdministratorConfiguration
{
    /// <summary>The key beneath the endpoint section that the administrators are configured under.</summary>
    internal const string SettingName = "Administrators";

    /// <summary>Reports every key an administrator may present, across every administrator.</summary>
    /// <param name="administrators">The configured administrators, in configuration order.</param>
    /// <returns>The keys, in configuration order, empty when the endpoint accepts none.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="administrators" /> is <see langword="null" />.</exception>
    internal static IReadOnlyList<ConfiguredSecret> ApiKeysIn(IEnumerable<AdministratorOptions> administrators) =>
        [.. CredentialsIn(administrators).Select(credential => credential.ApiKey).OfType<ConfiguredSecret>()];

    /// <summary>Reports every client public key a signed assertion may be verified against, across every administrator.</summary>
    /// <param name="administrators">The configured administrators, in configuration order.</param>
    /// <returns>The public keys, in configuration order, empty when the endpoint accepts no assertion.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="administrators" /> is <see langword="null" />.</exception>
    internal static IReadOnlyList<ConfiguredSecret> PublicKeysIn(IEnumerable<AdministratorOptions> administrators) =>
        [.. CredentialsIn(administrators).Select(credential => credential.PublicKey).OfType<ConfiguredSecret>()];

    /// <summary>Reports what a token must prove, once per credential that states OAuth.</summary>
    /// <param name="administrators">The configured administrators, in configuration order.</param>
    /// <returns>The OAuth blocks, in configuration order, empty when the endpoint accepts no token.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="administrators" /> is <see langword="null" />.</exception>
    internal static IReadOnlyList<OAuthValidationOptions> OAuthMethodsIn(IEnumerable<AdministratorOptions> administrators) =>
        [.. CredentialsIn(administrators).Select(credential => credential.OAuth).OfType<OAuthValidationOptions>()];

    /// <summary>Reports each authorization server the OAuth credentials name, once however many administrators name it.</summary>
    /// <param name="administrators">The configured administrators, in configuration order.</param>
    /// <returns>The first profile written for each server name, in configuration order.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="administrators" /> is <see langword="null" />.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the settings have not passed <see cref="FindConfigurationErrors" />.</exception>
    /// <remarks>
    /// Several administrators signing in through one identity provider each write the same server beside their own
    /// subjects, and the endpoint validates that server's tokens once: <see cref="FindConfigurationErrors" /> has proved
    /// that every repetition agrees about the issuer and the discovery document, so the first one speaks for all of them.
    /// </remarks>
    internal static IReadOnlyList<AuthorizationServerOptions> DistinctAuthorizationServersIn(
        IEnumerable<AdministratorOptions> administrators) =>
        [
            .. OAuthMethodsIn(administrators)
                .SelectMany(oauth => oauth.AuthorizationServers)
                .DistinctBy(server => server.PublishedName(), StringComparer.OrdinalIgnoreCase),
        ];

    /// <summary>Maps each configured API key onto the administrator it admits.</summary>
    /// <param name="administrators">The configured administrators, in configuration order.</param>
    /// <returns>The administrators, keyed by the key's configured name.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="administrators" /> is <see langword="null" />.</exception>
    /// <remarks>Keyed by the name because that is the identity a successful comparison reports, and it is unique within the section a duplicate would be refused in.</remarks>
    internal static IReadOnlyDictionary<string, AdministratorOptions> AdministratorsByApiKeyName(
        IEnumerable<AdministratorOptions> administrators) =>
        AdministratorsByCredentialName(administrators, credential => credential.ApiKey);

    /// <summary>Maps each configured client public key onto the administrator it admits.</summary>
    /// <param name="administrators">The configured administrators, in configuration order.</param>
    /// <returns>The administrators, keyed by the public key's configured name.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="administrators" /> is <see langword="null" />.</exception>
    internal static IReadOnlyDictionary<string, AdministratorOptions> AdministratorsByPublicKeyName(
        IEnumerable<AdministratorOptions> administrators) =>
        AdministratorsByCredentialName(administrators, credential => credential.PublicKey);

    /// <summary>Maps each issuer and subject an OAuth credential names onto the administrator it admits, and the scopes that credential requires.</summary>
    /// <param name="administrators">The configured administrators, in configuration order.</param>
    /// <returns>The binding of each token identity, keyed by the joined issuer and subject a validated token carries.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="administrators" /> is <see langword="null" />.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the settings have not passed <see cref="FindConfigurationErrors" />.</exception>
    /// <remarks>
    /// A token is bound by its subject — the value the authorization server promises not to reuse, which a client
    /// credentials grant carries for the client as an authorization code grant does for a person — and never by an
    /// address or a display name. The scopes travel with the binding rather than being keyed by issuer, because two
    /// administrators signing in through one server may each be asked for different ones.
    /// </remarks>
    internal static IReadOnlyDictionary<string, AdministratorTokenBinding> TokenBindingsByIdentity(
        IEnumerable<AdministratorOptions> administrators)
    {
        ArgumentNullException.ThrowIfNull(administrators);

        return administrators
            .SelectMany(administrator => administrator.Credentials
                .Select(credential => credential.OAuth)
                .OfType<OAuthValidationOptions>()
                .SelectMany(oauth => oauth.AuthorizationServers
                    .SelectMany(server => server.AuthorizedIdentities())
                    .Select(identity => (
                        Identity: identity,
                        Binding: new AdministratorTokenBinding(administrator, [.. oauth.RequiredScopes])))))
            .DistinctBy(entry => entry.Identity, StringComparer.Ordinal)
            .ToDictionary(entry => entry.Identity, entry => entry.Binding, StringComparer.Ordinal);
    }

    /// <summary>Tells each bound administrator and credential the things only the section it came from knows.</summary>
    /// <param name="endpointSection">The endpoint section the administrators were bound from.</param>
    /// <param name="administrators">The bound administrators, in configuration order.</param>
    /// <exception cref="ArgumentNullException">Thrown when either argument is <see langword="null" />.</exception>
    /// <remarks>
    /// <para>
    /// An absent grant and an emptied one bind identically and mean opposite things — the whole surface against nothing
    /// — so the two are told apart by asking configuration whether the key exists at all.
    /// </para>
    /// <para>
    /// Each entry is paired with the configuration child it was bound from rather than with its position in the bound
    /// list, because the two come apart the moment a source numbers its entries with a gap: the binder appends a child
    /// per key it finds and keeps no record of which key that was, so an environment-variable configuration writing
    /// <c>…__0__…</c> and <c>…__2__…</c> binds two entries at positions 0 and 1. Reading a grant by position there
    /// would hand the second administrator the whole surface it had narrowed away from, and every refusal against it
    /// would name a path the operator's configuration does not contain.
    /// </para>
    /// <para>
    /// The pairing is positional, so it says nothing at all once the two lists are different lengths, and the read then
    /// leaves every administrator on the grant that reaches nothing. Refusing to read is the direction to be wrong in
    /// where what is read is a grant.
    /// </para>
    /// </remarks>
    internal static void ReadWhatTheBinderCannotSay(
        IConfigurationSection endpointSection,
        IReadOnlyList<AdministratorOptions> administrators)
    {
        ArgumentNullException.ThrowIfNull(endpointSection);
        ArgumentNullException.ThrowIfNull(administrators);

        var administratorSections = endpointSection.GetSection(SettingName).GetChildren().ToArray();

        if (administratorSections.Length != administrators.Count)
        {
            return;
        }

        foreach (var (administratorSection, administrator) in administratorSections.Zip(administrators))
        {
            administrator.RecordConfigurationKey(administratorSection.Key);

            if (!administratorSection.GetSection(nameof(AdministratorOptions.Permissions)).Exists())
            {
                administrator.GrantTheWholeSurface();
            }

            RecordCredentialKeys(administratorSection, administrator);
        }
    }

    /// <summary>Finds everything an operator must fix before the configured administrators can guard the endpoint.</summary>
    /// <param name="sectionName">The endpoint section the list was bound from, which every message is written against.</param>
    /// <param name="administrators">The configured administrators, in configuration order.</param>
    /// <returns>One message per faulty setting, each naming its configuration path, empty when the settings are usable.</returns>
    /// <exception cref="ArgumentNullException">Thrown when either argument is <see langword="null" />.</exception>
    /// <remarks>
    /// An empty list is not one of the faults. Accepting no credential is a posture rather than a mistake, and it is
    /// reported by the startup warning the endpoint carries rather than refused here. A value written where the list
    /// belongs needs no rule either: the binder cannot convert one into a list and raises while the section is read.
    /// </remarks>
    internal static IReadOnlyList<string> FindConfigurationErrors(
        string sectionName,
        IReadOnlyList<AdministratorOptions> administrators)
    {
        ArgumentNullException.ThrowIfNull(sectionName);
        ArgumentNullException.ThrowIfNull(administrators);

        var errors = administrators
            .Index()
            .SelectMany(indexed => indexed.Item.FindConfigurationErrors(SettingPathOf(sectionName, indexed.Item, indexed.Index)))
            .ToList();

        // The rules below read validated values, so they run only once every administrator is usable on its own. Asking
        // a malformed issuer for its canonical form would raise here instead of adding to the report that names it.
        if (errors.Count > 0)
        {
            return errors;
        }

        errors.AddRange(FindRepeatedNameErrors(sectionName, administrators));
        errors.AddRange(FindResourceAgreementErrors(sectionName, administrators));
        errors.AddRange(FindAuthorizationServerDisagreementErrors(sectionName, administrators));
        errors.AddRange(FindRepeatedTokenIdentityErrors(sectionName, administrators));

        return errors;
    }

    /// <summary>Composes the configuration path one administrator is named by, everywhere an operator is told to go and edit it.</summary>
    /// <param name="sectionName">The endpoint section the administrators were bound from.</param>
    /// <param name="administrator">The administrator the path names.</param>
    /// <param name="boundPosition">The position the administrator bound at, which names it where no read established its key.</param>
    /// <returns>The configuration path of the administrator.</returns>
    internal static string SettingPathOf(string sectionName, AdministratorOptions administrator, int boundPosition)
    {
        ArgumentNullException.ThrowIfNull(administrator);

        return $"{sectionName}:{SettingName}:{administrator.ConfigurationKey ?? boundPosition.ToString(CultureInfo.InvariantCulture)}";
    }

    /// <summary>Reports every credential across every administrator, each with the configuration path it was written at.</summary>
    /// <param name="sectionName">The endpoint section the administrators were bound from.</param>
    /// <param name="administrators">The configured administrators, in configuration order.</param>
    /// <returns>Each credential paired with its path, in configuration order.</returns>
    /// <exception cref="ArgumentNullException">Thrown when either argument is <see langword="null" />.</exception>
    internal static IEnumerable<(string SettingPath, AdministratorCredentialOptions Credential)> CredentialsWithPathsIn(
        string sectionName,
        IEnumerable<AdministratorOptions> administrators)
    {
        ArgumentNullException.ThrowIfNull(sectionName);
        ArgumentNullException.ThrowIfNull(administrators);

        return administrators
            .Index()
            .SelectMany(indexedAdministrator => indexedAdministrator.Item.Credentials
                .Index()
                .Select(indexedCredential => (
                    AdministratorOptions.CredentialPathOf(
                        SettingPathOf(sectionName, indexedAdministrator.Item, indexedAdministrator.Index),
                        indexedCredential.Item,
                        indexedCredential.Index),
                    indexedCredential.Item)));
    }

    private static IEnumerable<AdministratorCredentialOptions> CredentialsIn(IEnumerable<AdministratorOptions> administrators)
    {
        ArgumentNullException.ThrowIfNull(administrators);

        return administrators.SelectMany(administrator => administrator.Credentials);
    }

    private static void RecordCredentialKeys(IConfigurationSection administratorSection, AdministratorOptions administrator)
    {
        var credentialSections = administratorSection
            .GetSection(nameof(AdministratorOptions.Credentials))
            .GetChildren()
            .ToArray();

        if (credentialSections.Length != administrator.Credentials.Count)
        {
            return;
        }

        foreach (var (credentialSection, credential) in credentialSections.Zip(administrator.Credentials))
        {
            credential.RecordConfigurationKey(credentialSection.Key);
        }
    }

    /// <summary>Maps one kind of configured credential onto the administrator it sits under.</summary>
    /// <remarks>
    /// The first administrator claiming a name wins rather than the last, and neither is a decision worth having: two
    /// credentials named identically are refused at startup by the secret validator, which is where an operator reads
    /// about it. Composing the map is not the place to discover it, because raising here would replace that message
    /// with a dictionary fault naming nothing an operator configured.
    /// </remarks>
    private static Dictionary<string, AdministratorOptions> AdministratorsByCredentialName(
        IEnumerable<AdministratorOptions> administrators,
        Func<AdministratorCredentialOptions, ConfiguredSecret?> credentialIn)
    {
        ArgumentNullException.ThrowIfNull(administrators);

        return administrators
            .SelectMany(administrator => administrator.Credentials
                .Select(credentialIn)
                .OfType<ConfiguredSecret>()
                .Select(secret => (secret.Name, Administrator: administrator)))
            .DistinctBy(entry => entry.Name, StringComparer.Ordinal)
            .ToDictionary(entry => entry.Name, entry => entry.Administrator, StringComparer.Ordinal);
    }

    /// <summary>Reports the second and later administrators carrying a name an earlier one already carries.</summary>
    /// <remarks>Compared without regard to case, because two records reading <c>Alice</c> and <c>alice</c> would be read as one person by anybody auditing them.</remarks>
    private static IEnumerable<string> FindRepeatedNameErrors(
        string sectionName,
        IReadOnlyList<AdministratorOptions> administrators)
    {
        var claimedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (index, administrator) in administrators.Index())
        {
            if (!claimedNames.Add(administrator.ValidatedName()))
            {
                yield return $"{SettingPathOf(sectionName, administrator, index)}:{nameof(AdministratorOptions.Name)} — '{administrator.ValidatedName()}' repeats a name an earlier administrator already carries, and every administrative act is attributed by it; give each administrator its own name, and write a second credential under one administrator where the same holder needs two.";
            }
        }
    }

    /// <summary>Reports the OAuth credentials that name a different resource from the first one.</summary>
    /// <remarks>
    /// The scopes and the subjects are each credential's own; the resource cannot be, because the endpoint publishes
    /// exactly one protected resource metadata document and publishes it at an address derived from that identifier.
    /// </remarks>
    private static IEnumerable<string> FindResourceAgreementErrors(
        string sectionName,
        IReadOnlyList<AdministratorOptions> administrators)
    {
        string? firstResource = null;

        foreach (var (settingPath, credential) in CredentialsWithPathsIn(sectionName, administrators))
        {
            if (credential.OAuth is not { } oauth)
            {
                continue;
            }

            var resource = oauth.CanonicalResource();

            if (firstResource is null)
            {
                firstResource = resource;

                continue;
            }

            if (!string.Equals(resource, firstResource, StringComparison.Ordinal))
            {
                yield return $"{settingPath}:{nameof(AdministratorCredentialOptions.OAuth)}:{nameof(OAuthValidationOptions.Resource)} — every OAuth entry names the same resource, because the endpoint publishes one protected resource metadata document and publishes it at an address derived from that identifier. An earlier entry names '{firstResource}'; write that, or move this entry to an endpoint of its own.";
            }
        }
    }

    /// <summary>Reports the authorization servers two credentials describe differently.</summary>
    /// <remarks>
    /// A server may be written under as many administrators as sign in through it, and each repetition has to describe
    /// the same server: one validator is registered per name, and a token is routed to it by the issuer it names. A name
    /// carried by two issuers would register two validators under one scheme, an issuer carried by two names would leave
    /// the key set a token is trusted against decided by configuration order, and two discovery documents for one server
    /// would leave which one is fetched decided the same way.
    /// </remarks>
    private static IEnumerable<string> FindAuthorizationServerDisagreementErrors(
        string sectionName,
        IReadOnlyList<AdministratorOptions> administrators)
    {
        var issuerByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var nameByIssuer = new Dictionary<string, string>(StringComparer.Ordinal);
        var metadataAddressesByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (settingPath, credential) in CredentialsWithPathsIn(sectionName, administrators))
        {
            if (credential.OAuth is not { } oauth)
            {
                continue;
            }

            foreach (var (serverIndex, server) in oauth.AuthorizationServers.Index())
            {
                var serverPath =
                    $"{settingPath}:{nameof(AdministratorCredentialOptions.OAuth)}:{nameof(OAuthValidationOptions.AuthorizationServers)}:{serverIndex}";
                var name = server.PublishedName();
                var issuer = server.ValidatedIssuer();
                var metadataAddresses = string.Join(' ', server.MetadataAddresses());

                if (issuerByName.TryGetValue(name, out var knownIssuer)
                    && !string.Equals(knownIssuer, issuer, StringComparison.Ordinal))
                {
                    yield return $"{serverPath}:{nameof(AuthorizationServerOptions.Name)} — '{name}' repeats a name another authorization server already carries, and the two would register under one scheme.";
                }
                else if (nameByIssuer.TryGetValue(issuer, out var knownName)
                    && !string.Equals(knownName, name, StringComparison.OrdinalIgnoreCase))
                {
                    yield return $"{serverPath}:{nameof(AuthorizationServerOptions.Issuer)} — this issuer repeats one another authorization server already carries, which would leave the key set a token is trusted against decided by configuration order.";
                }
                else if (metadataAddressesByName.TryGetValue(name, out var knownMetadataAddresses)
                    && !string.Equals(knownMetadataAddresses, metadataAddresses, StringComparison.Ordinal))
                {
                    yield return $"{serverPath}:{nameof(AuthorizationServerOptions.MetadataAddress)} — '{name}' is written elsewhere with a different discovery document, and one server is read from one document; write the same '{nameof(AuthorizationServerOptions.MetadataAddress)}' everywhere this server appears, or none.";
                }

                issuerByName.TryAdd(name, issuer);
                nameByIssuer.TryAdd(issuer, name);
                metadataAddressesByName.TryAdd(name, metadataAddresses);
            }
        }
    }

    /// <summary>Reports the subjects an earlier credential already names under the same issuer.</summary>
    /// <remarks>
    /// A token is bound to exactly one administrator, so an issuer and subject written twice — under two administrators
    /// or twice under one — would leave which name an act is attributed to, and which scopes a token is asked for,
    /// decided by configuration order.
    /// </remarks>
    private static IEnumerable<string> FindRepeatedTokenIdentityErrors(
        string sectionName,
        IReadOnlyList<AdministratorOptions> administrators)
    {
        var claimedIdentities = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (settingPath, credential) in CredentialsWithPathsIn(sectionName, administrators))
        {
            if (credential.OAuth is not { } oauth)
            {
                continue;
            }

            foreach (var (serverIndex, server) in oauth.AuthorizationServers.Index())
            {
                foreach (var (subjectIndex, subject) in server.AuthorizedSubjects.Index())
                {
                    var identity = OAuthIdentity.IdentityOf(server.ValidatedIssuer(), subject.Trim());

                    if (!claimedIdentities.Add(identity))
                    {
                        yield return $"{settingPath}:{nameof(AdministratorCredentialOptions.OAuth)}:{nameof(OAuthValidationOptions.AuthorizationServers)}:{serverIndex}:{nameof(AuthorizationServerOptions.AuthorizedSubjects)}:{subjectIndex} — '{subject.Trim()}' is already an administrator's subject at this authorization server, and a token is bound to exactly one administrator; write each subject once.";
                    }
                }
            }
        }
    }
}
