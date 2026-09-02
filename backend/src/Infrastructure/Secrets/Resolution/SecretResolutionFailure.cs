// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Infrastructure.Secrets.Resolution;

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
    /// <remarks>The database adapter uses this for a transport or server failure distinct from a missing row and a timeout.</remarks>
    ProviderUnavailable = 8,

    /// <summary>The material exceeds the maximum size a secret may occupy.</summary>
    MaterialTooLarge = 9,

    /// <summary>The target names something other than a regular file, such as a FIFO, a socket, or a device.</summary>
    /// <remarks>
    /// A mistyped path can name any of them, and none holds material a credential could be read from: a FIFO blocks
    /// until a writer appears, and a character device yields bytes without end.
    /// </remarks>
    TargetNotRegularFile = 10,

    /// <summary>Retrieving the material did not finish within the deadline the adapter enforces.</summary>
    /// <remarks>
    /// This is what a stalled network mount produces. It is distinct from <see cref="ProviderUnavailable" /> because a
    /// provider that refused is a different operator problem from one that never answered.
    /// </remarks>
    RetrievalTimedOut = 11,

    /// <summary>The reference points into the database while resolving it is required to reach or open that database.</summary>
    BootstrapDependencyNotPermitted = 12,

    /// <summary>The stored ciphertext, key, or authenticated binding could not produce material.</summary>
    ProtectedMaterialUnavailable = 13,
}
