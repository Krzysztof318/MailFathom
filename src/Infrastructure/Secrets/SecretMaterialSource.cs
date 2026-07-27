// Copyright © 2026 Krzysztof Kasprowicz

namespace MailMcp.Infrastructure.Secrets;

/// <summary>Records where resolved material came from.</summary>
/// <remarks>
/// The provenance cannot be derived from <see cref="SecretValueInterpretation" /> alone, because
/// <see cref="SecretValueInterpretation.ReferenceOrInline" /> mixes both within one deployment. Startup logging needs it
/// to name the settings that resolved inline, and a consumer that accepts binary material only from a provisioned
/// source needs it to reject the same bytes supplied inline.
/// </remarks>
public enum SecretMaterialSource
{
    /// <summary>A registered scheme adapter retrieved the material through a reference.</summary>
    SchemeAdapter = 0,

    /// <summary>The configured value was itself the material.</summary>
    InlineValue = 1,
}
