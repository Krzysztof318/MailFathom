// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Secrets.Discovery;

namespace MailFathom.Host.Configuration.Access;

/// <summary>The rules an endpoint's list of accepted credentials follows, wherever that list is configured.</summary>
/// <remarks>
/// Both protected surfaces configure the same list under the same key, and a rule about it is a rule about the list
/// rather than about the endpoint holding one. Keeping them here is what stops the two endpoints from drifting into two
/// readings of one setting, each refusing the same arrangement in its own words or one of them not refusing it at all.
/// </remarks>
internal static class TransportAuthenticationConfiguration
{
    /// <summary>The key beneath an endpoint section that the credentials are configured under.</summary>
    internal const string SettingName = "Authentication";

    /// <summary>Reports every key a client may present, across every entry that states one.</summary>
    /// <param name="methods">The configured entries, in configuration order.</param>
    /// <returns>The keys, in configuration order, empty when the endpoint accepts none.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="methods" /> is <see langword="null" />.</exception>
    internal static IReadOnlyList<ConfiguredSecret> ApiKeysIn(IEnumerable<TransportAuthenticationOptions> methods)
    {
        ArgumentNullException.ThrowIfNull(methods);

        return [.. methods.Select(method => method.ApiKey).OfType<ConfiguredSecret>()];
    }

    /// <summary>Reports what a token must prove, once per entry that states OAuth.</summary>
    /// <param name="methods">The configured entries, in configuration order.</param>
    /// <returns>The OAuth blocks, in configuration order, empty when the endpoint accepts no token.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="methods" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// Several blocks rather than one, because each states its own required scopes and its own authorization servers.
    /// What they may not disagree about is the resource, for the reason <see cref="FindResourceAgreementErrors" /> gives.
    /// </remarks>
    internal static IReadOnlyList<OAuthValidationOptions> OAuthMethodsIn(IEnumerable<TransportAuthenticationOptions> methods)
    {
        ArgumentNullException.ThrowIfNull(methods);

        return [.. methods.Select(method => method.OAuth).OfType<OAuthValidationOptions>()];
    }

    /// <summary>Maps each configured issuer onto the scopes the entry that trusts it requires.</summary>
    /// <param name="oauthMethods">The configured OAuth blocks.</param>
    /// <returns>The required scopes, keyed by the issuer whose tokens they are asked of.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="oauthMethods" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// Keyed by issuer rather than held as one set, because an entry states the scopes asked of the servers *it*
    /// configures. A token is judged against what its own issuer's entry requires, which is what makes two entries
    /// independent rather than merged into whichever of them asked for least.
    /// </remarks>
    internal static IReadOnlyDictionary<string, IReadOnlyCollection<string>> RequiredScopesByIssuer(
        IEnumerable<OAuthValidationOptions> oauthMethods)
    {
        ArgumentNullException.ThrowIfNull(oauthMethods);

        return oauthMethods
            .SelectMany(oauth => oauth.AuthorizationServers.Select(server => (
                Issuer: server.ValidatedIssuer(),
                Scopes: (IReadOnlyCollection<string>)[.. oauth.RequiredScopes])))
            .ToDictionary(entry => entry.Issuer, entry => entry.Scopes, StringComparer.Ordinal);
    }

    /// <summary>Finds everything an operator must fix before the configured credentials can guard an endpoint.</summary>
    /// <param name="sectionName">The endpoint section the list was bound from, which every message is written against.</param>
    /// <param name="methods">The configured entries, in configuration order.</param>
    /// <returns>One message per faulty setting, each naming its configuration path, empty when the settings are usable.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="sectionName" /> or <paramref name="methods" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// <para>
    /// An empty list is not one of the faults. Accepting no credential is a posture rather than a mistake, and it is
    /// reported by the startup warning each endpoint carries rather than refused here. Neither is a repeated method: an
    /// entry is a credential, so several of either kind is the ordinary shape.
    /// </para>
    /// <para>
    /// A value written where the list belongs is not one either, and needs no rule: the binder cannot convert one into
    /// a list and raises while the section is being read, which is before anything here could look at what it produced.
    /// </para>
    /// </remarks>
    internal static IReadOnlyList<string> FindConfigurationErrors(
        string sectionName,
        IReadOnlyList<TransportAuthenticationOptions> methods)
    {
        ArgumentNullException.ThrowIfNull(sectionName);
        ArgumentNullException.ThrowIfNull(methods);

        var errors = new List<string>();

        foreach (var (index, method) in methods.Index())
        {
            errors.AddRange(method.FindConfigurationErrors($"{sectionName}:{SettingName}:{index}"));
        }

        // The rules below read validated values, so they run only once every entry is usable on its own. Asking a
        // malformed issuer for its canonical form would raise here instead of adding to the report that already names it.
        if (errors.Count > 0)
        {
            return errors;
        }

        errors.AddRange(FindResourceAgreementErrors(sectionName, methods));
        errors.AddRange(FindAuthorizationServerCollisionErrors(sectionName, methods));

        return errors;
    }

    /// <summary>Reports the OAuth entries that name a different resource from the first one.</summary>
    /// <remarks>
    /// The scopes and the servers are each entry's own; the resource cannot be, because the endpoint publishes exactly
    /// one protected resource metadata document and publishes it at an address derived from that identifier. Two
    /// resources would mean one document describing a resource the other entry's clients were never told about, and a
    /// client asking its authorization server for the wrong audience — a failure that appears as a refused token rather
    /// than as anything naming the configuration.
    /// </remarks>
    private static IEnumerable<string> FindResourceAgreementErrors(
        string sectionName,
        IReadOnlyList<TransportAuthenticationOptions> methods)
    {
        string? firstResource = null;

        foreach (var (index, method) in methods.Index())
        {
            if (method.OAuth is not { } oauth)
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
                yield return $"{sectionName}:{SettingName}:{index}:{nameof(TransportAuthenticationOptions.OAuth)}:{nameof(OAuthValidationOptions.Resource)} — every OAuth entry names the same resource, because the endpoint publishes one protected resource metadata document and publishes it at an address derived from that identifier. An earlier entry names '{firstResource}'; write that, or move this entry to an endpoint of its own.";
            }
        }
    }

    /// <summary>Reports the authorization servers two entries name identically.</summary>
    /// <remarks>
    /// Both halves would otherwise be decided by configuration order. A repeated name collides at registration, because
    /// the scheme a server's token validator is registered under is composed from it; and a repeated issuer would leave
    /// the key set a token is trusted against chosen by whichever entry was read first. Each entry already refuses both
    /// within itself, so this is the same rule reaching across entries that were separately valid.
    /// </remarks>
    private static IEnumerable<string> FindAuthorizationServerCollisionErrors(
        string sectionName,
        IReadOnlyList<TransportAuthenticationOptions> methods)
    {
        var claimedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var claimedIssuers = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (index, method) in methods.Index())
        {
            if (method.OAuth is not { } oauth)
            {
                continue;
            }

            foreach (var (serverIndex, authorizationServer) in oauth.AuthorizationServers.Index())
            {
                var settingPath =
                    $"{sectionName}:{SettingName}:{index}:{nameof(TransportAuthenticationOptions.OAuth)}:{nameof(OAuthValidationOptions.AuthorizationServers)}:{serverIndex}";

                if (!claimedNames.Add(authorizationServer.Name!))
                {
                    yield return $"{settingPath}:{nameof(AuthorizationServerOptions.Name)} — '{authorizationServer.Name}' repeats a name another authorization server already carries, and the two would register under one scheme.";
                }

                if (!claimedIssuers.Add(authorizationServer.ValidatedIssuer()))
                {
                    yield return $"{settingPath}:{nameof(AuthorizationServerOptions.Issuer)} — this issuer repeats one another authorization server already carries, which would leave the key set a token is trusted against decided by configuration order.";
                }
            }
        }
    }
}
