// Copyright © 2026 Krzysztof Kasprowicz

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
    /// <summary>Gets or sets the <c>&lt;scheme&gt;:&lt;target&gt;</c> reference the host resolves, or the material itself under an inline interpretation mode.</summary>
    public string SecretReference { get; set; } = string.Empty;

    /// <summary>Gets or sets the password protecting the referenced material, when the material is itself protected.</summary>
    /// <remarks>
    /// Discovery descends into this block like any other, so a bundle password is bound, validated, resolved, and erased
    /// by exactly the machinery every other secret uses.
    /// </remarks>
    public ConfiguredSecret? Password { get; set; }
}
