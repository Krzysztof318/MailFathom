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
    /// A value is one name <see cref="MailFathomPermission" /> publishes, or a <see cref="PermissionSubtree" /> naming
    /// several of them at once, and either way it has to reach this entry's own surface; startup refuses anything else
    /// rather than accepting a grant nothing enforces. A pattern may name permissions of both surfaces without naming
    /// every one of them, and it then grants this surface's half, since the other half names checks this endpoint
    /// never makes.
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

    /// <summary>Gets the configuration key this entry was written under, or <see langword="null" /> where no read established one.</summary>
    /// <remarks>A source may number its entries with a gap, so the key an operator wrote and the position the binder appended the entry at are different numbers. A refusal names this one wherever it is known, because a path nobody wrote is a path nobody can go and correct.</remarks>
    internal string? ConfigurationKey { get; private set; }

    /// <summary>Gets whether this entry states any credential at all.</summary>
    public bool StatesAMethod => this.ApiKey is not null || this.OAuth is not null || this.PublicKey is not null;

    /// <summary>Gets whether this entry states a credential the deployment itself configured, which can carry no scope of its own.</summary>
    public bool StatesAConfiguredCredential => this.ApiKey is not null || this.PublicKey is not null;

    /// <summary>Records that the deployment stated no grant on this entry, which is what leaves it reaching the whole surface.</summary>
    /// <remarks>Called by the endpoint's own read, which is the only place that holds the configuration section this was bound from.</remarks>
    internal void GrantTheWholeSurface() => this.GrantsTheWholeSurface = true;

    /// <summary>Records the configuration key this entry was written under, which every refusal against it is named by.</summary>
    /// <param name="key">The key of the configuration child the entry was bound from.</param>
    /// <remarks>The bound position is what a refusal falls back to, and the two are the same number only while the source numbers its entries without a gap — so an operator reading a refusal is given the path they wrote wherever the read could establish it.</remarks>
    internal void RecordConfigurationKey(string key) => this.ConfigurationKey = key;

    /// <summary>Reports the permissions this entry grants, resolved once from what the operator wrote.</summary>
    /// <param name="surface">The surface this entry guards, which decides which half of the vocabulary an unwritten grant reaches.</param>
    /// <returns>The granted permissions, in the published order.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the settings have not passed <see cref="FindConfigurationErrors" />, which is what proves every written name is one this repository publishes.</exception>
    /// <remarks>
    /// <para>
    /// This is the ceiling rather than what a particular caller holds. Where
    /// <see cref="PermissionsFromTokenScopes" /> is set, a token holds this narrowed by its own scopes, which is a
    /// question about the token and is asked where one is validated.
    /// </para>
    /// <para>
    /// A grant is a set, so a written one is returned in the published order rather than the order it was typed in.
    /// The order is what the startup line reads in and what the claims are stamped in, and two entries granting the
    /// same permissions differently ordered are the same grant — reporting them as two would invite an operator to
    /// look for a difference that is not there. A subtree resolves here rather than being carried any further, so
    /// nothing downstream — a claim, a startup line, a session response, a metadata document — has to expand one.
    /// </para>
    /// <para>
    /// A subtree may reach across both surfaces without reaching everything — <c>mailfathom.*.read</c> names two
    /// permissions on each — and what it grants here is this surface's half of that. An entry guards one surface, so
    /// the other half is a name this entry could not enforce and must not be reported as one it holds; validation has
    /// already refused a pattern whose whole reach is elsewhere, which is the case where the narrowing would leave
    /// nothing behind.
    /// </para>
    /// </remarks>
    public IReadOnlyList<MailFathomPermission> GrantedPermissions(ProtectedSurface surface)
    {
        if (this.GrantsTheWholeSurface)
        {
            return MailFathomPermission.PublishedFor(surface);
        }

        var granted = this.Permissions
            .SelectMany(configuredPermission => ResolveOrThrow(configuredPermission, surface))
            .ToHashSet();

        return [.. MailFathomPermission.All.Where(granted.Contains)];
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
    /// than a mistake. What is refused is a value naming nothing this repository publishes, one naming only the other
    /// surface — which would otherwise sit there granting nothing while an operator believed they had granted
    /// something — one naming the whole vocabulary, one whose permissions the grant already carries, and asking a
    /// credential this deployment configured to bring scopes it can never carry.
    /// </remarks>
    private IEnumerable<string> FindGrantErrors(string settingPath, ProtectedSurface surface)
    {
        if (this.PermissionsFromTokenScopes && this.StatesAConfiguredCredential)
        {
            yield return $"{settingPath}:{nameof(this.PermissionsFromTokenScopes)} — a token's scopes can narrow a grant and a credential this deployment configured carries none, so this entry cannot be read both ways. Move the '{nameof(this.OAuth)}' block to an entry of its own, or remove this setting and write the grant in '{nameof(this.Permissions)}'.";
        }

        var claimedPermissions = new HashSet<MailFathomPermission>();

        // The list rather than a position inside it, because the position is where the binder appended the string and
        // the operator may have written it under another key. Each message quotes the value, which is what they search
        // their own file for.
        var permissionPath = $"{settingPath}:{nameof(this.Permissions)}";

        foreach (var configuredPermission in this.Permissions)
        {
            var covered = CoveredBy(configuredPermission);
            var grantedHere = OnSurface(covered, surface);
            var refusal = RefusalOf(permissionPath, configuredPermission, surface, covered, grantedHere, claimedPermissions);

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
    /// reads back as one message against one line of an operator's file. The four refusals a subtree can draw are
    /// worded apart from the four a name can, since an operator who wrote a pattern is looking for what the pattern
    /// did rather than for a name they did not write.
    /// <para>
    /// Two of them read the reach and two read what is left of it on this surface, which is the distinction a pattern
    /// spanning both surfaces introduced: reaching nothing published and reaching everything published are answered
    /// against the whole vocabulary, while granting nothing here and repeating something already carried are answered
    /// against this entry's own half — a value granting two names here and two on the other surface is an ordinary
    /// grant of the two, and only its own half can repeat what an earlier value in the same list gave.
    /// </para>
    /// </remarks>
    private static string? RefusalOf(
        string permissionPath,
        string configuredPermission,
        ProtectedSurface surface,
        IReadOnlyList<MailFathomPermission> covered,
        IReadOnlyList<MailFathomPermission> grantedHere,
        IReadOnlySet<MailFathomPermission> claimedPermissions)
    {
        var namesASubtree = PermissionSubtree.TryParse(configuredPermission, out var subtree);

        if (covered.Count == 0)
        {
            return namesASubtree
                ? $"{permissionPath} — '{configuredPermission}' matches no permission MailFathom publishes; a '*' segment stands for one or more whole segments of a published name, so write a pattern reaching one of {PublishedNamesFor(surface)}, or the name itself."
                : $"{permissionPath} — '{configuredPermission}' is not a permission MailFathom publishes; write one of {PublishedNamesFor(surface)}, or a pattern over them writing '*' in place of one or more whole segments.";
        }

        if (namesASubtree && subtree.ReachesEveryPublishedPermission())
        {
            return $"{permissionPath} — '{configuredPermission}' reaches both protected surfaces entirely, so it is no shorthand for a part of this one, and what it would grant here is what leaving the '{nameof(Permissions)}' key out already grants. Remove the key, or write a pattern reaching one of {PublishedNamesFor(surface)}.";
        }

        if (grantedHere.Count == 0)
        {
            return namesASubtree
                ? $"{permissionPath} — '{configuredPermission}' matches only permissions of the other protected surface and grants nothing here; write a pattern reaching one of {PublishedNamesFor(surface)}, or move the entry to the endpoint that serves it."
                : $"{permissionPath} — '{configuredPermission}' belongs to the other protected surface and grants nothing here; write one of {PublishedNamesFor(surface)}, or move the entry to the endpoint that serves it.";
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
    /// <returns>The published permissions the value names, empty when it names none.</returns>
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

    /// <summary>Reports what one written grant value grants on one surface, once validation has established that it reaches something.</summary>
    /// <exception cref="InvalidOperationException">Thrown when the value reaches nothing, which is what validation exists to have refused already.</exception>
    private static IReadOnlyList<MailFathomPermission> ResolveOrThrow(string configuredPermission, ProtectedSurface surface)
    {
        var covered = CoveredBy(configuredPermission);

        return covered.Count > 0
            ? OnSurface(covered, surface)
            : throw new InvalidOperationException(
                "The grant was resolved before it was validated, so at least one written value names neither a permission this repository publishes nor a subtree covering one.");
    }

    /// <summary>Narrows what a written value reaches to the surface the entry guards.</summary>
    /// <remarks>
    /// A pattern whose wildcard sits before its last segment can name permissions of both surfaces, and an entry
    /// guards one. What the other half would grant here is nothing — no check on this endpoint reads a name of the
    /// other surface — so it is dropped rather than reported as held; a value whose whole reach is over there is
    /// refused instead, because an operator who wrote one meant something this entry cannot do.
    /// </remarks>
    private static IReadOnlyList<MailFathomPermission> OnSurface(
        IReadOnlyList<MailFathomPermission> covered,
        ProtectedSurface surface) =>
        [.. covered.Where(permission => permission.Surface == surface)];

    private static string PublishedNamesFor(ProtectedSurface surface) =>
        string.Join(", ", MailFathomPermission.PublishedFor(surface).Select(permission => $"'{permission.Name}'"));
}
