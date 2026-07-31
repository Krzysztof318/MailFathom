// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

using System.Text;

namespace MailMcp.Infrastructure.Secrets;

/// <summary>The explicit bounds every secret retrieval enforces.</summary>
/// <remarks>
/// A mistaken <c>file:</c> target can name a log, a database file, or a device-backed pseudo-file. Reading one whole and
/// then allocating an equally large pinned copy would exhaust memory or stall the host before validation finishes — at
/// startup and again per operation, because material is resolved per use rather than cached. The ceiling is generous
/// enough for a certificate bundle and far below anything that threatens the process.
/// </remarks>
internal static class SecretMaterialLimits
{
    /// <summary>The maximum size of one secret's material.</summary>
    internal const int MaximumMaterialByteCount = 1024 * 1024;

    /// <summary>Reports whether a configured value is too large to become material.</summary>
    /// <param name="configuredValue">The literal that would be copied into a pinned buffer.</param>
    /// <returns><see langword="true" /> when the value exceeds <see cref="MaximumMaterialByteCount" />; otherwise <see langword="false" />.</returns>
    /// <remarks>
    /// The ceiling binds inline material as well as retrieved material. A configured value arrives as a
    /// <see cref="string" /> the host already holds, so nothing here prevents that allocation; what it prevents is
    /// pinning a second multi-megabyte copy for the process lifetime because a whole document was pasted where a
    /// credential belongs.
    /// </remarks>
    internal static bool ExceedsMaximumByteCount(string configuredValue) =>
        Encoding.UTF8.GetByteCount(configuredValue) > MaximumMaterialByteCount;
}
