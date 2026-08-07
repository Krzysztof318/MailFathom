// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
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

    /// <summary>Gets whether this entry states any credential at all.</summary>
    public bool StatesAMethod => this.ApiKey is not null || this.OAuth is not null || this.PublicKey is not null;

    /// <summary>Finds everything an operator must fix before this entry can accept a credential.</summary>
    /// <param name="settingPath">The configuration path this entry was bound from, which every message is written against.</param>
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

        return this.OAuth is { } oauth
            ? [.. oauth.FindConfigurationErrors().Select(error => $"{settingPath}:{nameof(this.OAuth)}:{error}")]
            : [];
    }
}
