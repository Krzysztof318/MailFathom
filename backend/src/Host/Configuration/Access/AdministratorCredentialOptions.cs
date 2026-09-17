// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Infrastructure.Secrets.Discovery;

namespace MailFathom.Host.Configuration.Access;

/// <summary>One credential an administrator presents, written as the block of whichever method judges it.</summary>
/// <remarks>
/// <para>
/// A credential states a method by carrying that method's block. Nothing names a method a second time, so a method
/// cannot be selected without being configured or configured without being selected — the two arrangements a separate
/// list of method names would leave a validator to refuse by hand, and refusing them by hand is the arrangement in which
/// one of them is eventually missed.
/// </para>
/// <para>
/// One key per credential rather than a list inside one. Rotation is an ordinary second credential under the same
/// administrator rather than a nested list with its own shape, and a key is named in a refusal by the position it was
/// written at.
/// </para>
/// <para>
/// A credential may carry several blocks. Nothing about the methods conflicts — they judge different credentials on
/// different requests — and every block admits the same administrator with the same grant, so which credential a block
/// sits in is a matter of how an operator groups what they wrote. The one shape that says nothing is a credential
/// carrying none, which is refused.
/// </para>
/// <para>
/// What the credential admits somebody to do is not written here. The grant and the networks belong to the
/// <see cref="AdministratorOptions" /> the credential sits under, because they describe a person or a system rather than
/// a key: rotating a key never changes what its holder may do.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The options framework materializes this type during configuration binding.")]
internal sealed class AdministratorCredentialOptions
{
    /// <summary>Gets or sets one key the administrator may present, as a named secret with its own lifetime.</summary>
    /// <remarks><see langword="null" /> when this credential states no API key, because the block's presence is what selects the method.</remarks>
    public ConfiguredSecret? ApiKey { get; set; }

    /// <summary>Gets or sets a user's username and password, which this endpoint refuses.</summary>
    /// <remarks>
    /// Bound only so it can be refused in words rather than as an unknown key. The administrative surface answers for
    /// the deployment rather than for a person's mail, and this method authenticates one mail user — a credential
    /// admitted here would carry a user the surface has nowhere to put and no use for.
    /// </remarks>
    public BasicAuthenticationOptions? Basic { get; set; }

    /// <summary>Gets or sets what this deployment is called in OAuth terms, which authorization servers may speak for it, and which of their subjects are this administrator.</summary>
    /// <remarks><see langword="null" /> when this credential states no OAuth, because the block's presence is what selects the method.</remarks>
    public OAuthValidationOptions? OAuth { get; set; }

    /// <summary>Gets or sets one client's public key, which a signed assertion is verified against.</summary>
    /// <remarks>
    /// <para>
    /// <see langword="null" /> when this credential states no key pair, because the block's presence is what selects the
    /// method. This is the half of the pair the deployment holds, and the whole point of the method is that it is the
    /// only half it holds: nothing behind this reference is worth stealing from the host, from a backup of it, or from
    /// the configuration an operator hands to a deployment tool.
    /// </para>
    /// <para>
    /// It binds to the same secret-bearing shape a key does, which is what reaches it through every reference scheme the
    /// deployment already has — a file, an environment variable, a systemd credential — and what gives it a name to be
    /// refused by and a lifetime to be retired at.
    /// </para>
    /// </remarks>
    public ConfiguredSecret? PublicKey { get; set; }

    /// <summary>Gets the configuration key this credential was written under, or <see langword="null" /> where no read established one.</summary>
    /// <remarks>A source may number its entries with a gap, so the key an operator wrote and the position the binder appended the entry at are different numbers. A refusal names this one wherever it is known.</remarks>
    internal string? ConfigurationKey { get; private set; }

    /// <summary>Gets whether this credential states any method at all.</summary>
    public bool StatesAMethod =>
        this.ApiKey is not null || this.OAuth is not null || this.PublicKey is not null || this.Basic is not null;

    /// <summary>Gets whether this credential states a method that can carry no scope of its own.</summary>
    /// <remarks>
    /// A key and a public key are material the deployment configured, and a username and password are a record it
    /// provisioned — none of the three has an authorization server behind it to narrow a grant — which is what <see cref="AdministratorOptions.PermissionsFromTokenScopes" /> asks for.
    /// </remarks>
    public bool StatesAConfiguredCredential =>
        this.ApiKey is not null || this.PublicKey is not null || this.Basic is not null;

    /// <summary>Records the configuration key this credential was written under, which every refusal against it is named by.</summary>
    /// <param name="key">The key of the configuration child the credential was bound from.</param>
    internal void RecordConfigurationKey(string key) => this.ConfigurationKey = key;

    /// <summary>Finds everything an operator must fix before this credential can admit anyone.</summary>
    /// <param name="settingPath">The configuration path this credential was bound from, which every message is written against.</param>
    /// <returns>One message per faulty setting, each naming its configuration path, empty when the credential is usable.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="settingPath" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// Whether a configured key names itself usably, and whether the material behind it can be retrieved, are the secret
    /// machinery's questions and are answered by <see cref="SecretConfigurationValidator" /> against the same section.
    /// </remarks>
    public IReadOnlyList<string> FindConfigurationErrors(string settingPath)
    {
        ArgumentNullException.ThrowIfNull(settingPath);

        if (!this.StatesAMethod)
        {
            return
            [
                $"{settingPath} — this entry states no credential; write an '{nameof(this.ApiKey)}' block naming one key, a '{nameof(this.PublicKey)}' block naming one client's public key, or an '{nameof(this.OAuth)}' block naming the resource and its authorization servers.",
            ];
        }

        var errors = new List<string>();

        if (this.OAuth is { } oauth)
        {
            errors.AddRange(oauth.FindConfigurationErrors(OAuthSubjectAdmission.ConfiguredSubjects).Select(error => $"{settingPath}:{nameof(this.OAuth)}:{error}"));
        }

        if (this.Basic is not null)
        {
            errors.Add($"{settingPath}:{nameof(this.Basic)} — the administrative endpoint does not accept a user's username and password. It answers for the deployment rather than for one person, so a credential naming a user has nothing to act for here; provision an '{nameof(this.ApiKey)}', a '{nameof(this.PublicKey)}', or an '{nameof(this.OAuth)}' block instead, and write the '{nameof(this.Basic)}' block on the client or MCP endpoint the user actually signs in to.");
        }

        return errors;
    }
}
