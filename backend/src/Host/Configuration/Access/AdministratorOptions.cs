// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net;
using MailFathom.Domain.Access;
using MailFathom.Host.Configuration.Endpoints;
using MailFathom.Host.Security.Transport;

namespace MailFathom.Host.Configuration.Access;

/// <summary>One named administrator of this deployment: the credentials they present, what they may do, and where from.</summary>
/// <remarks>
/// <para>
/// The administrative surface admits people and systems rather than keys. An administrator carries a name, which is what
/// every record of an administrative act is attributed to, and any number of credentials, each of which admits that one
/// administrator — so rotating a key is a second credential under the same name, and an audit record says who acted
/// rather than which key happened to be presented.
/// </para>
/// <para>
/// An administrator exists in configuration and nowhere else. There is no database record, no provisioning route, and no
/// personal-data lifecycle behind one: the surface answers for the deployment rather than for a mail user, and whoever
/// can change this section already holds the deployment.
/// </para>
/// <para>
/// <see cref="Permissions" /> is the ceiling on what every credential of this administrator may do, and
/// <see cref="AllowedSourceNetworks" /> confines all of them to the networks named. Both belong here rather than on a
/// credential because they describe the holder: a leaked key is refused from outside the networks its holder was meant to
/// act from, and a rotated key holds exactly what the key it replaces held.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The options framework materializes this type during configuration binding.")]
internal sealed class AdministratorOptions
{
    private static readonly ConfiguredAddressRangeWording SourceNetworkWording = new(
        "network",
        "admit",
        "An administrator is recognized by the address a request arrives from, so a DNS name cannot stand in for one.");

    /// <summary>Gets or sets the name every administrative act this administrator performs is attributed to.</summary>
    /// <remarks>
    /// Unique across the section, compared without regard to case, and never a secret: it is what the session read
    /// reports, what an audit record names, and what a log line carries. Write a person's or a system's handle —
    /// <c>alice</c>, <c>ci-pipeline</c> — rather than anything that identifies them outside this deployment.
    /// </remarks>
    public string? Name { get; set; }

    /// <summary>Gets the credentials this administrator may present, any one of which admits them.</summary>
    public IList<AdministratorCredentialOptions> Credentials { get; } = [];

    /// <summary>Gets the published permission names this administrator may hold, which is the ceiling on what any of their credentials may do.</summary>
    /// <remarks>
    /// <para>
    /// An absent key and an empty list are opposite statements and the binder cannot tell them apart, so the property's
    /// own default is the restrictive one — nothing — and the permissive default belongs to the read: the endpoint's
    /// own read calls <see cref="GrantTheWholeSurface" /> on an administrator whose key configuration never carried. An
    /// operator who wrote <c>[]</c> has reached this setting and narrowed all the way, which is how an administrator is
    /// retired without being deleted.
    /// </para>
    /// <para>
    /// A value is one name <see cref="MailFathomPermission" /> publishes, or a <see cref="PermissionSubtree" /> naming
    /// several of them at once, and either way it has to reach the administrative surface; startup refuses anything else
    /// rather than accepting a grant nothing enforces. A pattern may name permissions of both surfaces without naming
    /// every one of them, and it then grants this surface's half.
    /// </para>
    /// </remarks>
    public IList<string> Permissions { get; } = [];

    /// <summary>Gets or sets whether a token's own scopes narrow the ceiling above, instead of every credential holding all of it.</summary>
    /// <remarks>
    /// With it, a token holds the published names its scopes carry <em>and</em> this administrator lists, so the
    /// authorization server decides per issuance within a bound the deployment fixed. Available only to an administrator
    /// whose every credential is <see cref="AdministratorCredentialOptions.OAuth" />, because a key and a public key carry
    /// no claim about what they may do, so startup refuses it beside either of them.
    /// </remarks>
    public bool PermissionsFromTokenScopes { get; set; }

    /// <summary>Gets the addresses and CIDR networks this administrator may act from, empty for anywhere.</summary>
    /// <remarks>
    /// <para>
    /// Written exactly as <see cref="ReverseProxyOptions.TrustedProxies" /> is and validated by the same rule. A request
    /// from outside the list is refused as an authentication failure — with no hint that the credential it presented was
    /// valid — and the refusal is recorded with this administrator's name and the address observed.
    /// </para>
    /// <para>
    /// The address compared is the client's. Behind a reverse proxy that is the address the proxy forwarded, which this
    /// process believes only from a proxy <see cref="ReverseProxyOptions.TrustedProxies" /> names, so startup refuses a
    /// restriction while that section names none: a restriction nobody could enforce is not written down as one.
    /// </para>
    /// </remarks>
    public IList<string> AllowedSourceNetworks { get; } = [];

    /// <summary>Gets whether this administrator left the grant unstated and therefore reaches everything the surface publishes.</summary>
    /// <remarks>
    /// The binder cannot answer it — an absent list and an empty one bind identically — so it is read from configuration
    /// by the endpoint that owns the section. It is the permissive case that is recorded rather than the narrowed one, so
    /// an administrator no read ever reached grants nothing instead of everything.
    /// </remarks>
    internal bool GrantsTheWholeSurface { get; private set; }

    /// <summary>Gets the configuration key this administrator was written under, or <see langword="null" /> where no read established one.</summary>
    internal string? ConfigurationKey { get; private set; }

    /// <summary>Gets whether this administrator is confined to named networks.</summary>
    public bool RestrictsSourceNetworks => this.AllowedSourceNetworks.Count > 0;

    /// <summary>Reports the name, trimmed, that this administrator is attributed by.</summary>
    /// <returns>The configured name.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the settings have not passed <see cref="FindConfigurationErrors" />.</exception>
    public string ValidatedName() =>
        string.IsNullOrWhiteSpace(this.Name)
            ? throw new InvalidOperationException(
                "The administrator's name was read before it was validated, so there is nothing to attribute an act to.")
            : this.Name.Trim();

    /// <summary>Records that the deployment stated no grant on this administrator, which is what leaves it reaching the whole surface.</summary>
    internal void GrantTheWholeSurface() => this.GrantsTheWholeSurface = true;

    /// <summary>Records the configuration key this administrator was written under, which every refusal against it is named by.</summary>
    /// <param name="key">The key of the configuration child the administrator was bound from.</param>
    internal void RecordConfigurationKey(string key) => this.ConfigurationKey = key;

    /// <summary>Reports the networks this administrator may act from, a single address being a network of one.</summary>
    /// <returns>The networks, in configuration order, empty when the administrator is unrestricted.</returns>
    /// <exception cref="FormatException">Thrown when the settings have not passed <see cref="FindConfigurationErrors" />.</exception>
    public IReadOnlyList<IPNetwork> SourceNetworks() => ConfiguredAddressRanges.ToNetworks(this.AllowedSourceNetworks);

    /// <summary>Reports the permissions this administrator holds, resolved once from what the operator wrote.</summary>
    /// <returns>The granted permissions, in the published order.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the settings have not passed <see cref="FindConfigurationErrors" />, which is what proves every written name is one this repository publishes.</exception>
    /// <remarks>
    /// <para>
    /// This is the ceiling rather than what a particular request holds. Where <see cref="PermissionsFromTokenScopes" />
    /// is set, a token holds this narrowed by its own scopes, which is a question about the token and is asked where one
    /// is validated.
    /// </para>
    /// <para>
    /// A grant is a set, so a written one is returned in the published order rather than the order it was typed in. A
    /// subtree resolves here rather than being carried any further, so nothing downstream — a claim, a startup line, a
    /// session response, a metadata document — has to expand one, and a subtree reaching across both surfaces grants
    /// this surface's half.
    /// </para>
    /// </remarks>
    public IReadOnlyList<MailFathomPermission> GrantedPermissions()
    {
        if (this.GrantsTheWholeSurface)
        {
            return MailFathomPermission.PublishedFor(ProtectedSurface.Administration);
        }

        var granted = this.Permissions
            .SelectMany(ResolveOrThrow)
            .ToHashSet();

        return [.. MailFathomPermission.All.Where(granted.Contains)];
    }

    /// <summary>Finds everything an operator must fix in this administrator on its own, without comparing it to the others.</summary>
    /// <param name="settingPath">The configuration path this administrator was bound from, which every message is written against.</param>
    /// <returns>One message per faulty setting, each naming its configuration path, empty when the administrator is usable.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="settingPath" /> is <see langword="null" />.</exception>
    public IReadOnlyList<string> FindConfigurationErrors(string settingPath)
    {
        ArgumentNullException.ThrowIfNull(settingPath);

        return
        [
            .. this.FindNameErrors(settingPath),
            .. this.FindCredentialErrors(settingPath),
            .. this.FindGrantErrors(settingPath),
            .. ConfiguredAddressRanges.FindErrors(
                this.AllowedSourceNetworks,
                $"{settingPath}:{nameof(this.AllowedSourceNetworks)}",
                SourceNetworkWording),
        ];
    }

    /// <summary>Composes the configuration path one of this administrator's credentials is named by.</summary>
    /// <param name="settingPath">This administrator's own configuration path.</param>
    /// <param name="credential">The credential the path names.</param>
    /// <param name="boundPosition">The position the credential bound at, which names it where no read established its key.</param>
    /// <returns>The configuration path of the credential.</returns>
    internal static string CredentialPathOf(string settingPath, AdministratorCredentialOptions credential, int boundPosition) =>
        $"{settingPath}:{nameof(Credentials)}:{credential.ConfigurationKey ?? boundPosition.ToString(CultureInfo.InvariantCulture)}";

    private IEnumerable<string> FindNameErrors(string settingPath)
    {
        var namePath = $"{settingPath}:{nameof(this.Name)}";

        if (string.IsNullOrWhiteSpace(this.Name))
        {
            yield return $"{namePath} — every administrator needs a name, because every administrative act is attributed to it; write a handle for the person or system this is, such as 'alice' or 'ci-pipeline'.";

            yield break;
        }

        // A name reaches log lines and audit records verbatim, so a control character in it could forge a line.
        if (this.Name.Any(char.IsControl))
        {
            yield return $"{namePath} — the name carries a control character, and it is written into log lines and audit records verbatim; write it with printable characters only.";
        }
        else if (string.Equals(this.Name.Trim(), TransportCallerIdentity.AnonymousCaller, StringComparison.OrdinalIgnoreCase))
        {
            yield return $"{namePath} — '{TransportCallerIdentity.AnonymousCaller}' is what a caller is named where nothing authenticated, so an administrator carrying it would be indistinguishable from one in every record; choose another name.";
        }
    }

    private IEnumerable<string> FindCredentialErrors(string settingPath)
    {
        if (this.Credentials.Count == 0)
        {
            return [$"{settingPath}:{nameof(this.Credentials)} — this administrator states no credential and could never be admitted; write at least one entry carrying an '{nameof(AdministratorCredentialOptions.ApiKey)}', a '{nameof(AdministratorCredentialOptions.PublicKey)}', or an '{nameof(AdministratorCredentialOptions.OAuth)}' block, or remove the administrator."];
        }

        return this.Credentials
            .Index()
            .SelectMany(indexed => indexed.Item.FindConfigurationErrors(CredentialPathOf(settingPath, indexed.Item, indexed.Index)));
    }

    private IEnumerable<string> FindGrantErrors(string settingPath)
    {
        if (this.PermissionsFromTokenScopes && this.Credentials.Any(credential => credential.StatesAConfiguredCredential))
        {
            yield return $"{settingPath}:{nameof(this.PermissionsFromTokenScopes)} — a token's scopes can narrow a grant and a key, a public key, or a user's password carries none, so this administrator cannot be read both ways. Move the '{nameof(AdministratorCredentialOptions.OAuth)}' block to an administrator of its own, or remove this setting and write the grant in '{nameof(this.Permissions)}'.";
        }

        var claimedPermissions = new HashSet<MailFathomPermission>();

        // The list rather than a position inside it, because the position is where the binder appended the string and
        // the operator may have written it under another key. Each message quotes the value, which is what they search
        // their own file for.
        var permissionPath = $"{settingPath}:{nameof(this.Permissions)}";

        foreach (var configuredPermission in this.Permissions)
        {
            var covered = CoveredBy(configuredPermission);
            var grantedHere = OnThisSurface(covered);
            var refusal = RefusalOf(permissionPath, configuredPermission, covered, grantedHere, claimedPermissions);

            if (refusal is not null)
            {
                yield return refusal;

                continue;
            }

            claimedPermissions.UnionWith(grantedHere);
        }
    }

    /// <summary>Reports why one written grant value cannot be accepted, or that it can.</summary>
    /// <remarks>
    /// A value refused for what it names is not also refused for overlapping something, because each written value
    /// reads back as one message against one line of an operator's file. Two of the refusals read the value's whole
    /// reach and two read what is left of it on this surface, which is the distinction a pattern spanning both surfaces
    /// introduced.
    /// </remarks>
    private static string? RefusalOf(
        string permissionPath,
        string configuredPermission,
        IReadOnlyList<MailFathomPermission> covered,
        IReadOnlyList<MailFathomPermission> grantedHere,
        IReadOnlySet<MailFathomPermission> claimedPermissions)
    {
        var namesASubtree = PermissionSubtree.TryParse(configuredPermission, out var subtree);

        if (covered.Count == 0)
        {
            return namesASubtree
                ? $"{permissionPath} — '{configuredPermission}' matches no permission MailFathom publishes; a '*' segment stands for one or more whole segments of a published name, so write a pattern reaching one of {PublishedNames()}, or the name itself."
                : $"{permissionPath} — '{configuredPermission}' is not a permission MailFathom publishes; write one of {PublishedNames()}, or a pattern over them writing '*' in place of one or more whole segments.";
        }

        if (namesASubtree && subtree.ReachesEveryPublishedPermission())
        {
            return $"{permissionPath} — '{configuredPermission}' reaches both protected surfaces entirely, so it is no shorthand for a part of this one, and what it would grant here is what leaving the '{nameof(Permissions)}' key out already grants. Remove the key, or write a pattern reaching one of {PublishedNames()}.";
        }

        if (grantedHere.Count == 0)
        {
            return namesASubtree
                ? $"{permissionPath} — '{configuredPermission}' matches only permissions of the other protected surface and grants nothing here; write a pattern reaching one of {PublishedNames()}, or move the entry to the endpoint that serves it."
                : $"{permissionPath} — '{configuredPermission}' belongs to the other protected surface and grants nothing here; write one of {PublishedNames()}, or move the entry to the endpoint that serves it.";
        }

        if (grantedHere.FirstOrDefault(claimedPermissions.Contains) is { IsSpecified: true } alreadyCarried)
        {
            return namesASubtree
                ? $"{permissionPath} — '{configuredPermission}' covers '{alreadyCarried.Name}', which the grant already carries."
                : $"{permissionPath} — '{configuredPermission}' repeats a permission the grant already carries.";
        }

        return null;
    }

    /// <summary>Reports what one written grant value reaches, without saying whether it may.</summary>
    private static IReadOnlyList<MailFathomPermission> CoveredBy(string configuredPermission)
    {
        if (MailFathomPermission.TryParse(configuredPermission, out var permission))
        {
            return [permission];
        }

        return PermissionSubtree.TryParse(configuredPermission, out var subtree)
            ? subtree.CoveredPermissions()
            : [];
    }

    /// <summary>Reports what one written grant value grants here, once validation has established that it reaches something.</summary>
    /// <exception cref="InvalidOperationException">Thrown when the value reaches nothing, which is what validation exists to have refused already.</exception>
    private static IReadOnlyList<MailFathomPermission> ResolveOrThrow(string configuredPermission)
    {
        var covered = CoveredBy(configuredPermission);

        return covered.Count > 0
            ? OnThisSurface(covered)
            : throw new InvalidOperationException(
                "The grant was resolved before it was validated, so at least one written value names neither a permission this repository publishes nor a subtree covering one.");
    }

    /// <summary>Narrows what a written value reaches to the administrative surface, since no check here reads a name of the other one.</summary>
    private static IReadOnlyList<MailFathomPermission> OnThisSurface(IReadOnlyList<MailFathomPermission> covered) =>
        [.. covered.Where(permission => permission.Surface == ProtectedSurface.Administration)];

    private static string PublishedNames() =>
        string.Join(
            ", ",
            MailFathomPermission.PublishedFor(ProtectedSurface.Administration).Select(permission => $"'{permission.Name}'"));
}
