// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Domain.Access;
using MailFathom.Infrastructure.Secrets.Discovery;

namespace MailFathom.Host.Configuration.Access;

/// <summary>One credential a protected surface accepts, written as the block of whichever method judges it.</summary>
/// <remarks>
/// <para>
/// An entry states a method by carrying that method's block, and a surface accepts as many credentials as it carries
/// entries. Nothing names a method a second time, so a method cannot be selected without being configured or configured
/// without being selected — the two arrangements a separate list of method names would leave a validator to refuse by
/// hand, and refusing them by hand is the arrangement in which one of them is eventually missed.
/// </para>
/// <para>
/// One key per entry rather than a list inside one, because an entry is a credential. Rotation is then an ordinary
/// second entry rather than a nested list with its own shape, and a key is named in a refusal by the position it was
/// written at.
/// </para>
/// <para>
/// An entry may carry several blocks. Nothing about the methods conflicts — they judge different credentials on
/// different requests — so which entry a block sits in is a matter of how an operator groups what they wrote, and the
/// endpoint accepts every block across every entry. The one shape that says nothing is an entry carrying none, which is
/// refused.
/// </para>
/// <para>
/// The entry is also where a grant is written down, which is what makes that grouping consequential for anybody who
/// writes one: <see cref="Permissions" /> is the ceiling on what every credential this entry admits may do, so two
/// credentials to be granted differently are two entries. Nothing already configured changes meaning, because an entry
/// that writes no grant holds what it always held.
/// </para>
/// <para>
/// A method arriving later — a client certificate, say — is a block beside these. Nothing else moves, because nothing
/// outside an entry records which methods exist.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The options framework materializes this type during configuration binding.")]
internal sealed class TransportAuthenticationOptions
{
    /// <summary>Gets or sets one key a client may present, as a named secret with its own lifetime.</summary>
    /// <remarks><see langword="null" /> when this entry states no API key, because the block's presence is what selects the method.</remarks>
    public ConfiguredSecret? ApiKey { get; set; }

    /// <summary>Gets or sets what this deployment is called in OAuth terms and which authorization servers may speak for it.</summary>
    /// <remarks><see langword="null" /> when this entry states no OAuth, because the block's presence is what selects the method.</remarks>
    public OAuthValidationOptions? OAuth { get; set; }

    /// <summary>Gets or sets one client's public key, which a signed assertion is verified against.</summary>
    /// <remarks>
    /// <para>
    /// <see langword="null" /> when this entry states no key pair, because the block's presence is what selects the
    /// method. This is the half of the pair the deployment holds, and the whole point of the method is that it is the
    /// only half it holds: nothing behind this reference is worth stealing from the host, from a backup of it, or from
    /// the configuration an operator hands to a deployment tool.
    /// </para>
    /// <para>
    /// It binds to the same secret-bearing shape a key or a password does, which is what reaches it through every
    /// reference scheme the deployment already has — a file, an environment variable, a systemd credential — and what
    /// gives it a name to be refused by and a lifetime to be retired at. That the material is not itself secret changes
    /// none of those, and handling it under the same erasure discipline as everything else costs nothing.
    /// </para>
    /// </remarks>
    public ConfiguredSecret? PublicKey { get; set; }

    /// <summary>Gets the published permission names every credential this entry admits may hold, which is the ceiling on what one may do.</summary>
    /// <remarks>
    /// <para>
    /// An absent key and an empty list are opposite statements and the binder cannot tell them apart, so the property's
    /// own default is the restrictive one — nothing — and the permissive default belongs to the read: the endpoint's
    /// own read calls <see cref="GrantTheWholeSurface" /> on an entry whose key configuration never carried. An
    /// operator who wrote <c>[]</c> has reached this setting and narrowed all the way, which is how a credential is
    /// retired without its entry being deleted. An entry constructed anywhere but from configuration therefore starts
    /// from the grant that reaches nothing, which is why the permissive case is the one recorded rather than the
    /// narrowed one.
    /// </para>
    /// <para>
    /// Every name is one <see cref="MailFathomPermission" /> publishes and belongs to this entry's own surface;
    /// startup refuses anything else rather than accepting a grant nothing enforces.
    /// </para>
    /// </remarks>
    public IList<string> Permissions { get; } = [];

    /// <summary>Gets or sets whether a token's own scopes narrow the ceiling above, instead of every credential holding all of it.</summary>
    /// <remarks>
    /// With it, a token holds the published names its scopes carry <em>and</em> this entry lists, so the authorization
    /// server decides per subject within a bound the deployment fixed. Available only to an entry whose sole block is
    /// <see cref="OAuth" />, because neither a key nor a public key can carry anything the deployment did not write, so
    /// startup refuses it beside either rather than asking a credential a question it cannot answer.
    /// </remarks>
    public bool PermissionsFromTokenScopes { get; set; }

    /// <summary>Gets whether this entry left its grant unstated and therefore reaches everything its surface publishes.</summary>
    /// <remarks>
    /// It is what tells a grant narrowed to nothing from a grant nobody narrowed, which is the difference between a
    /// credential that is refused everything and one that reaches the whole surface. The binder cannot answer it — an
    /// absent list and an empty one bind identically — so it is read from configuration by the endpoint that owns the
    /// section, exactly as the origin list and the clear-text redirect are. It is the permissive case that is recorded
    /// rather than the narrowed one, so an entry no read ever reached grants nothing instead of everything.
    /// </remarks>
    internal bool GrantsTheWholeSurface { get; private set; }

    /// <summary>Gets whether this entry states any credential at all.</summary>
    public bool StatesAMethod => this.ApiKey is not null || this.OAuth is not null || this.PublicKey is not null;

    /// <summary>Gets whether this entry states a credential the deployment itself configured, which can carry no scope of its own.</summary>
    public bool StatesAConfiguredCredential => this.ApiKey is not null || this.PublicKey is not null;

    /// <summary>Records that the deployment stated no grant on this entry, which is what leaves it reaching the whole surface.</summary>
    /// <remarks>Called by the endpoint's own read, which is the only place that holds the configuration section this was bound from.</remarks>
    internal void GrantTheWholeSurface() => this.GrantsTheWholeSurface = true;

    /// <summary>Reports the permissions this entry grants, resolved once from what the operator wrote.</summary>
    /// <param name="surface">The surface this entry guards, which decides which half of the vocabulary an unwritten grant reaches.</param>
    /// <returns>The granted permissions, in the published order.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the settings have not passed <see cref="FindConfigurationErrors" />, which is what proves every written name is one this repository publishes.</exception>
    /// <remarks>
    /// This is the ceiling rather than what a particular caller holds. Where
    /// <see cref="PermissionsFromTokenScopes" /> is set, a token holds this narrowed by its own scopes, which is a
    /// question about the token and is asked where one is validated.
    /// </remarks>
    public IReadOnlyList<MailFathomPermission> GrantedPermissions(ProtectedSurface surface)
    {
        if (this.GrantsTheWholeSurface)
        {
            return MailFathomPermission.PublishedFor(surface);
        }

        return
        [
            .. this.Permissions.Select(configuredPermission =>
                MailFathomPermission.TryParse(configuredPermission, out var permission)
                    ? permission
                    : throw new InvalidOperationException(
                        "The grant was resolved before it was validated, so at least one written name is not a permission this repository publishes.")),
        ];
    }

    /// <summary>Finds everything an operator must fix before this entry can accept a credential.</summary>
    /// <param name="settingPath">The configuration path this entry was bound from, which every message is written against.</param>
    /// <param name="surface">The surface this entry guards, which decides which half of the published vocabulary its grant may name.</param>
    /// <returns>One message per faulty setting, each naming its configuration path, empty when the settings are usable.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="settingPath" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// The path arrives as an argument rather than being composed by the caller, because the message about an entry
    /// stating nothing is about the entry itself and reads as a path followed by prose. A caller prefixing it would have
    /// to know which of the two shapes it had been handed.
    /// <para>
    /// Whether a configured key names itself usably, and whether the material behind it can be retrieved, are the secret
    /// machinery's questions and are answered by <see cref="SecretConfigurationValidator" /> against the same section.
    /// </para>
    /// </remarks>
    public IReadOnlyList<string> FindConfigurationErrors(string settingPath, ProtectedSurface surface)
    {
        ArgumentNullException.ThrowIfNull(settingPath);

        if (!this.StatesAMethod)
        {
            return
            [
                $"{settingPath} — this entry states no credential; write an '{nameof(this.ApiKey)}' block naming one key, a '{nameof(this.PublicKey)}' block naming one client's public key, or an '{nameof(this.OAuth)}' block naming the resource and its authorization servers.",
            ];
        }

        var errors = new List<string>(this.FindGrantErrors(settingPath, surface));

        if (this.OAuth is { } oauth)
        {
            errors.AddRange(oauth.FindConfigurationErrors().Select(error => $"{settingPath}:{nameof(this.OAuth)}:{error}"));
        }

        return errors;
    }

    /// <summary>Reports the grant an operator wrote that says something impossible.</summary>
    /// <remarks>
    /// An emptied list is deliberately not among them: it is a grant narrowed all the way, which is a posture rather
    /// than a mistake. What is refused is a name nothing publishes, a name belonging to the other surface — which would
    /// otherwise sit there granting nothing while an operator believed they had granted something — a name written
    /// twice, and asking a credential this deployment configured to bring scopes it can never carry.
    /// </remarks>
    private IEnumerable<string> FindGrantErrors(string settingPath, ProtectedSurface surface)
    {
        if (this.PermissionsFromTokenScopes && this.StatesAConfiguredCredential)
        {
            yield return $"{settingPath}:{nameof(this.PermissionsFromTokenScopes)} — a token's scopes can narrow a grant and a credential this deployment configured carries none, so this entry cannot be read both ways. Move the '{nameof(this.OAuth)}' block to an entry of its own, or remove this setting and write the grant in '{nameof(this.Permissions)}'.";
        }

        var claimedPermissions = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (index, configuredPermission) in this.Permissions.Index())
        {
            var permissionPath = $"{settingPath}:{nameof(this.Permissions)}:{index}";

            if (!MailFathomPermission.TryParse(configuredPermission, out var permission))
            {
                yield return $"{permissionPath} — '{configuredPermission}' is not a permission MailFathom publishes; write one of {PublishedNamesFor(surface)}.";
            }
            else if (permission.Surface != surface)
            {
                yield return $"{permissionPath} — '{configuredPermission}' belongs to the other protected surface and grants nothing here; write one of {PublishedNamesFor(surface)}, or move the entry to the endpoint that serves it.";
            }
            else if (!claimedPermissions.Add(configuredPermission))
            {
                yield return $"{permissionPath} — '{configuredPermission}' repeats a permission the grant already carries.";
            }
        }
    }

    private static string PublishedNamesFor(ProtectedSurface surface) =>
        string.Join(", ", MailFathomPermission.PublishedFor(surface).Select(permission => $"'{permission.Name}'"));
}
