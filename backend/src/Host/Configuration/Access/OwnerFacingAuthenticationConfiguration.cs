// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using MailFathom.Domain.Access;

namespace MailFathom.Host.Configuration.Access;

/// <summary>The rules an owner-facing endpoint's list of accepted methods follows, wherever that list is configured.</summary>
/// <remarks>
/// Both mail-serving surfaces configure the same list under the same key, and a rule about it is a rule about the list
/// rather than about the endpoint holding one. Keeping them here is what stops the two endpoints from drifting into two
/// readings of one setting, each refusing the same arrangement in its own words or one of them not refusing it at all.
/// </remarks>
internal static class OwnerFacingAuthenticationConfiguration
{
    /// <summary>The key beneath an endpoint section that the accepted methods are configured under.</summary>
    /// <remarks>The same key the administrative endpoint's own list is written under, because it answers the same question — which credentials does this endpoint accept — and an operator moving between two sections should not have to learn a second name for it.</remarks>
    internal const string SettingName = TransportAuthenticationConfiguration.SettingName;

    /// <summary>The settings an entry used to carry, anywhere beneath the list, and what replaced each of them.</summary>
    /// <remarks>
    /// A key written here no longer says anything, and the binder ignores what it does not know — so a deployment
    /// upgrading into the owner axis would come up admitting nobody it used to admit and reporting nothing about it.
    /// Each is refused by name and by the path it was written at, with the credential to provision in its place, which
    /// is the whole of what an operator has to do about the break.
    /// </remarks>
    private static readonly Dictionary<string, string> RetiredSettings = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ApiKey"] =
            "a key an owner's client presents is provisioned for that owner rather than configured here, because it "
            + "names whose mail it reaches. Write an entry accepting 'api-key' and provision the key with "
            + "'mfctl credential create --method api-key'; the deployment mints it and reports it once.",
        ["PublicKey"] =
            "a client public key is registered against the owner whose mail its assertions reach. Write an entry "
            + "accepting 'public-key' and register the key with 'mfctl credential create --method public-key "
            + "--public-key-file <path>'; the deployment reports the fingerprint the client's assertions must name.",
        ["Permissions"] =
            "what a credential may do is recorded on the credential, beside the owner it resolves. Provision it with "
            + "'mfctl credential create', naming '--permission' once for each name it holds, or naming none for "
            + "everything this surface publishes.",
        ["AuthorizedSubjects"] =
            "which subjects this deployment serves is one credential record per person, because a subject now resolves "
            + "an owner rather than being admitted for whoever the deployment serves. Map each with "
            + "'mfctl credential create --method oauth-subject --issuer <issuer> --subject <subject>'.",
    };

    /// <summary>Reports the entry that accepts an owner's username and password, where the endpoint accepts one.</summary>
    /// <param name="methods">The configured entries, in configuration order.</param>
    /// <returns>The entry naming the password method, or <see langword="null" /> when the endpoint accepts no password.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="methods" /> is <see langword="null" />.</exception>
    /// <remarks><see cref="FindConfigurationErrors" /> refuses a repeated method, so the first entry this finds is the only one there is.</remarks>
    internal static OwnerFacingAuthenticationOptions? BasicMethodIn(
        IEnumerable<OwnerFacingAuthenticationOptions> methods)
    {
        ArgumentNullException.ThrowIfNull(methods);

        return methods.FirstOrDefault(method => method.AcceptedMethod == OwnerCredentialMethod.Password);
    }

    /// <summary>Reports whether the endpoint accepts one method.</summary>
    /// <param name="methods">The configured entries, in configuration order.</param>
    /// <param name="method">The method being asked about.</param>
    /// <returns><see langword="true" /> when an entry names it.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="methods" /> is <see langword="null" />.</exception>
    internal static bool Accepts(
        IEnumerable<OwnerFacingAuthenticationOptions> methods,
        OwnerCredentialMethod method)
    {
        ArgumentNullException.ThrowIfNull(methods);

        return methods.Any(entry => entry.AcceptedMethod == method);
    }

    /// <summary>Reports what a token must prove, once per entry that accepts one.</summary>
    /// <param name="methods">The configured entries, in configuration order.</param>
    /// <returns>The OAuth blocks, in configuration order, empty when the endpoint accepts no token.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="methods" /> is <see langword="null" />.</exception>
    /// <remarks>Several blocks rather than one, because each states its own required scopes and its own authorization servers. What they may not disagree about is the resource, for the reason <see cref="TransportAuthenticationConfiguration" /> gives about the same rule.</remarks>
    internal static IReadOnlyList<OAuthValidationOptions> OAuthMethodsIn(
        IEnumerable<OwnerFacingAuthenticationOptions> methods)
    {
        ArgumentNullException.ThrowIfNull(methods);

        return [.. methods.Select(method => method.OAuth).OfType<OAuthValidationOptions>()];
    }

    /// <summary>Tells each bound entry the one thing only the section it came from knows: the key it was written under.</summary>
    /// <param name="endpointSection">The endpoint section the entries were bound from.</param>
    /// <param name="methods">The bound entries, in configuration order.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="endpointSection" /> or <paramref name="methods" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// Each entry is paired with the configuration child it was bound from rather than with its position in the bound
    /// list, because the two come apart the moment a source numbers its entries with a gap: the binder appends a child
    /// per key it finds and keeps no record of which key that was, so an environment-variable configuration writing
    /// <c>…__0__…</c> and <c>…__2__…</c> binds two entries at positions 0 and 1, and every refusal against the second
    /// would name a path the operator's configuration does not contain. The pairing is positional, so it says nothing
    /// once the two lists are different lengths and nothing is recorded there.
    /// </remarks>
    internal static void ReadWhatTheBinderCannotSay(
        IConfigurationSection endpointSection,
        IReadOnlyList<OwnerFacingAuthenticationOptions> methods)
    {
        ArgumentNullException.ThrowIfNull(endpointSection);
        ArgumentNullException.ThrowIfNull(methods);

        var entrySections = endpointSection.GetSection(SettingName).GetChildren().ToArray();

        if (entrySections.Length != methods.Count)
        {
            return;
        }

        foreach (var (entrySection, method) in entrySections.Zip(methods))
        {
            method.RecordConfigurationKey(entrySection.Key);
        }
    }

    /// <summary>Finds the settings an entry no longer carries, anywhere beneath the configured list.</summary>
    /// <param name="sectionName">The endpoint section the list was bound from, which every message is written against.</param>
    /// <param name="endpointSection">The endpoint section as configuration holds it.</param>
    /// <returns>One message per retired setting, each naming the path it was written at, empty when none is.</returns>
    /// <exception cref="ArgumentNullException">Thrown when either argument is <see langword="null" />.</exception>
    /// <remarks>
    /// Read from configuration rather than from the bound entries, because the binder has nowhere to put a key it does
    /// not know: it either drops it in silence or refuses it with a message about an unknown property, and neither
    /// tells an operator that the credential they configured is now a credential they provision. The whole subtree is
    /// walked rather than the entry's own keys, because one of the four sits two levels down on an authorization
    /// server.
    /// </remarks>
    internal static IReadOnlyList<string> FindRetiredSettingErrors(
        string sectionName,
        IConfigurationSection endpointSection)
    {
        ArgumentNullException.ThrowIfNull(sectionName);
        ArgumentNullException.ThrowIfNull(endpointSection);

        var errors = new List<string>();

        WalkForRetiredSettings(endpointSection.GetSection(SettingName), $"{sectionName}:{SettingName}", errors);

        return errors;
    }

    /// <summary>Finds everything an operator must fix before the accepted methods can guard an endpoint.</summary>
    /// <param name="sectionName">The endpoint section the list was bound from, which every message is written against.</param>
    /// <param name="methods">The configured entries, in configuration order.</param>
    /// <returns>One message per faulty setting, each naming its configuration path, empty when the settings are usable.</returns>
    /// <exception cref="ArgumentNullException">Thrown when either argument is <see langword="null" />.</exception>
    /// <remarks>
    /// An empty list is not one of the faults. Accepting no credential is a posture rather than a mistake, and it is
    /// reported by the startup warning each endpoint carries rather than refused here.
    /// </remarks>
    internal static IReadOnlyList<string> FindConfigurationErrors(
        string sectionName,
        IReadOnlyList<OwnerFacingAuthenticationOptions> methods)
    {
        ArgumentNullException.ThrowIfNull(sectionName);
        ArgumentNullException.ThrowIfNull(methods);

        var errors = new List<string>();

        foreach (var (index, method) in methods.Index())
        {
            errors.AddRange(method.FindConfigurationErrors(SettingPathOf(sectionName, method, index)));
        }

        // The rules below read validated values, so they run only once every entry is usable on its own. Asking a
        // malformed issuer for its canonical form would raise here instead of adding to the report that already names it.
        if (errors.Count > 0)
        {
            return errors;
        }

        errors.AddRange(FindRepeatedMethodErrors(sectionName, methods));
        errors.AddRange(FindResourceAgreementErrors(sectionName, methods));
        errors.AddRange(FindAuthorizationServerCollisionErrors(sectionName, methods));

        return errors;
    }

    /// <summary>Composes the configuration path one entry is named by, everywhere an operator is told to go and edit it.</summary>
    /// <param name="sectionName">The endpoint section the entries were bound from.</param>
    /// <param name="method">The entry the path names.</param>
    /// <param name="boundPosition">The position the entry bound at, which names it where no read established its key.</param>
    /// <returns>The configuration path of the entry.</returns>
    internal static string SettingPathOf(
        string sectionName,
        OwnerFacingAuthenticationOptions method,
        int boundPosition) =>
        $"{sectionName}:{SettingName}:{method.ConfigurationKey ?? boundPosition.ToString(CultureInfo.InvariantCulture)}";

    private static void WalkForRetiredSettings(
        IConfigurationSection section,
        string path,
        List<string> errors)
    {
        foreach (var child in section.GetChildren())
        {
            var childPath = $"{path}:{child.Key}";

            if (RetiredSettings.TryGetValue(child.Key, out var replacement))
            {
                errors.Add($"{childPath} — {replacement}");

                continue;
            }

            WalkForRetiredSettings(child, childPath, errors);
        }
    }

    /// <summary>Reports the second and later entries naming a method an earlier entry already accepts.</summary>
    /// <remarks>
    /// OAuth is exempt because each of its entries carries its own authorization servers and its own required scopes,
    /// and a token is judged by the entry that trusts its issuer. Nothing else here carries anything an entry could
    /// differ about, so a second one is either a duplicate an operator meant to delete or two bounds with no rule
    /// saying which applies.
    /// </remarks>
    private static IEnumerable<string> FindRepeatedMethodErrors(
        string sectionName,
        IReadOnlyList<OwnerFacingAuthenticationOptions> methods)
    {
        var accepted = new HashSet<OwnerCredentialMethod>();

        foreach (var (index, method) in methods.Index())
        {
            var acceptedMethod = method.AcceptedMethod;

            if (acceptedMethod == OwnerCredentialMethod.OAuthSubject)
            {
                continue;
            }

            if (!accepted.Add(acceptedMethod))
            {
                yield return $"{SettingPathOf(sectionName, method, index)}:{nameof(OwnerFacingAuthenticationOptions.Method)} — an earlier entry already accepts '{acceptedMethod.Name}', and this endpoint accepts each method once. The credentials are rows rather than entries, so a second one is provisioned through the administrative endpoint rather than written here.";
            }
        }
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
        IReadOnlyList<OwnerFacingAuthenticationOptions> methods)
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
                yield return $"{SettingPathOf(sectionName, method, index)}:{nameof(OwnerFacingAuthenticationOptions.OAuth)}:{nameof(OAuthValidationOptions.Resource)} — every OAuth entry names the same resource, because the endpoint publishes one protected resource metadata document and publishes it at an address derived from that identifier. An earlier entry names '{firstResource}'; write that, or move this entry to an endpoint of its own.";
            }
        }
    }

    /// <summary>Reports the authorization servers two entries name identically.</summary>
    /// <remarks>
    /// Both halves would otherwise be decided by configuration order. A repeated name collides at registration, because
    /// the scheme a server's token validator is registered under is composed from it; and a repeated issuer would leave
    /// the key set a token is trusted against chosen by whichever entry was read first.
    /// </remarks>
    private static IEnumerable<string> FindAuthorizationServerCollisionErrors(
        string sectionName,
        IReadOnlyList<OwnerFacingAuthenticationOptions> methods)
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
                    $"{SettingPathOf(sectionName, method, index)}:{nameof(OwnerFacingAuthenticationOptions.OAuth)}:{nameof(OAuthValidationOptions.AuthorizationServers)}:{serverIndex}";

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
