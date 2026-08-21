// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text;
using MailFathom.Infrastructure.Secrets.Resolution;

namespace MailFathom.Infrastructure.Secrets.Sources;

/// <summary>The explicit bounds every secret retrieval enforces.</summary>
/// <remarks>
/// A mistaken <c>file:</c> target can name a log or a database file. Reading one whole and then allocating an equally
/// large pinned copy would exhaust memory or stall the host before validation finishes — at startup and again per
/// operation, because material is resolved per use rather than cached. The ceiling is generous enough for a
/// certificate bundle and far below anything that threatens the process. A target that is not a file at all is a
/// different failure and is refused as <see cref="SecretResolutionFailure.TargetNotRegularFile" /> instead.
/// </remarks>
internal static class SecretMaterialLimits
{
    /// <summary>The maximum size of one secret's material.</summary>
    internal const int MaximumMaterialByteCount = 1024 * 1024;

    /// <summary>How many retrievals may hold a platform call in flight at once.</summary>
    /// <remarks>
    /// Opening a target the kernel refuses to return from cannot be cancelled, so the thread that entered the call
    /// stays in it until the storage answers or the process ends. This is the ceiling on how many such threads can
    /// accumulate: each one keeps its permit, so once this many are stuck every further retrieval reports
    /// <see cref="SecretResolutionFailure.RetrievalTimedOut" /> without entering the platform at all. Four is above
    /// anything a healthy deployment needs — startup resolves references one after another — and low enough that a
    /// dead mount costs a bounded number of threads rather than one per configured secret.
    /// </remarks>
    internal const int MaximumConcurrentRetrievalCount = 4;

    /// <summary>The deadline one retrieval — the open and the read together — is given.</summary>
    /// <remarks>
    /// Reading a provisioned file is a sub-millisecond operation on every storage a deployment should be using, so
    /// this is patience for a mount recovering rather than a budget anything legitimate consumes. It is what turns a
    /// stalled target from a host that never starts and never explains itself into a named startup failure.
    /// </remarks>
    internal static readonly TimeSpan RetrievalDeadline = TimeSpan.FromSeconds(5);

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
