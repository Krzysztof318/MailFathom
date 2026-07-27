// Copyright © 2026 Krzysztof Kasprowicz

namespace MailMcp.Infrastructure.Secrets;

/// <summary>Identifies why a secret-bearing configuration value produced no material.</summary>
/// <remarks>
/// The identity is the whole failure vocabulary a diagnostic may carry. Neither the reference target, nor the
/// environment variable value, nor any part of the material may accompany it.
/// </remarks>
public enum SecretResolutionFailure
{
    /// <summary>No value was configured at all.</summary>
    ReferenceMissing = 0,

    /// <summary>The value carries no scheme separator, so it is not a reference.</summary>
    SchemeMissing = 1,

    /// <summary>The scheme is well formed but no adapter serving it is registered.</summary>
    SchemeNotSupported = 2,

    /// <summary>Nothing follows the scheme separator.</summary>
    TargetMissing = 3,

    /// <summary>The material was supplied inline where the consumer accepts referenced material only.</summary>
    /// <remarks>Reserved for a consumer that narrows the permitted provenance below the interpretation mode, such as binary certificate material.</remarks>
    InlineValueNotPermittedByInterpretationMode = 4,

    /// <summary>The process was not started with a systemd credentials directory.</summary>
    CredentialsDirectoryUnavailable = 5,

    /// <summary>The target names nothing the adapter could read.</summary>
    MaterialNotFound = 6,

    /// <summary>The target exists but holds no bytes.</summary>
    MaterialEmpty = 7,

    /// <summary>The provider backing the scheme could not be reached.</summary>
    /// <remarks>No adapter shipped today produces this; it exists so a network-backed provider can distinguish an outage from a missing secret.</remarks>
    ProviderUnavailable = 8,

    /// <summary>The material exceeds the maximum size a secret may occupy.</summary>
    MaterialTooLarge = 9,
}
