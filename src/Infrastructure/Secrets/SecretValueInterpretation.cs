// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

namespace MailFathom.Infrastructure.Secrets;

/// <summary>Selects how a configured secret-bearing value is interpreted before resolution.</summary>
/// <remarks>
/// Interpretation is an explicit deployment choice rather than an inference from the environment. A configuration
/// provider that resolved the secret before MailFathom bound it — Azure App Configuration with Key Vault references is the
/// concrete case — hands over the material itself with no scheme prefix, which only <see cref="InlineOnly" /> can
/// accept without guessing.
/// </remarks>
public enum SecretValueInterpretation
{
    /// <summary>Requires every value to be a well-formed <c>&lt;scheme&gt;:&lt;target&gt;</c> reference. This is the default.</summary>
    ReferenceOnly = 0,

    /// <summary>Resolves a registered scheme through its adapter and accepts every other value as the secret itself.</summary>
    ReferenceOrInline = 1,

    /// <summary>Parses nothing and accepts every value as the secret itself, for a configuration provider that pre-resolves.</summary>
    InlineOnly = 2,
}
