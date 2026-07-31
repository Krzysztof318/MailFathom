// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

namespace MailMcp.Infrastructure.Secrets;

/// <summary>The bindable shape of every secret-bearing configuration setting.</summary>
/// <remarks>
/// <para>
/// A setting is secret-bearing because it binds to this type, not because someone remembered to annotate it: a marker
/// attribute can be omitted on a property added later and nothing would notice, whereas an omitted type simply does not
/// bind to the block shape. <see cref="ConfiguredSecretDiscovery" /> therefore finds every secret-bearing setting by
/// walking the bound options graph for this type, which is what makes the startup rules apply to settings that do not
/// exist yet.
/// </para>
/// <para>
/// The object rather than a bare string is the unit so that a sibling property — a bundle password, a format hint, a
/// managed-store version pin — can be added without changing the JSON type of a setting an operator already configured.
/// It is a nested object in JSON but requires no JSON provider: a flattening provider addresses it as one more
/// colon-separated path segment, for example
/// <c>MailSynchronization:Accounts:0:Secrets:Password:SecretReference</c>.
/// </para>
/// <para>
/// The type is mutable because the configuration binder requires it, and it carries no resolution logic: it is the
/// shape and the marker, nothing more.
/// </para>
/// </remarks>
public sealed class ConfiguredSecret
{
    /// <summary>Gets or sets the operator-chosen identity of this secret, which is required and unique within the configuration root it belongs to.</summary>
    /// <remarks>
    /// It is the stable non-secret handle every diagnostic, rotation, and audit record names, in place of an array
    /// position that renumbers on the next edit or a value that must never be written down. <see cref="SecretName" />
    /// states which spellings are accepted, and startup rejects a missing, malformed, or duplicated one.
    /// </remarks>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the <c>&lt;scheme&gt;:&lt;target&gt;</c> reference the host resolves, or the material itself under an inline interpretation mode.</summary>
    public string SecretReference { get; set; } = string.Empty;

    /// <summary>Gets or sets how long the secret stays usable: <see cref="SecretLifetime.NoLimitValue" />, or an ISO 8601 instant carrying an explicit offset.</summary>
    /// <remarks>
    /// The default is the spelling rather than an absent value, so a secret that names no lifetime carries one
    /// explicitly and a blank setting is a mistake rather than a second way of saying the same thing.
    /// <see cref="SecretLifetime" /> states what the value means and which consumers enforce it.
    /// </remarks>
    public string Lifetime { get; set; } = SecretLifetime.NoLimitValue;

    /// <summary>Gets or sets the password protecting the referenced material, when the material is itself protected.</summary>
    /// <remarks>
    /// Discovery descends into this block like any other, so a bundle password is bound, validated, resolved, and erased
    /// by exactly the machinery every other secret uses.
    /// </remarks>
    public ConfiguredSecret? Password { get; set; }
}
