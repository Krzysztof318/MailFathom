// Copyright © 2026 Krzysztof Kasprowicz

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
}
