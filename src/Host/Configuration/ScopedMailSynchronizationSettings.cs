// Copyright © 2026 Krzysztof Kasprowicz

using System.Diagnostics.CodeAnalysis;

namespace MailMcp.Host.Configuration;

/// <summary>Holds the one mail synchronization snapshot a work unit runs against.</summary>
/// <remarks>
/// <para>
/// A scope reading the published snapshot itself would still be reading it at a moment of its own choosing. A
/// synchronization run schedules its accounts and folders from the snapshot it took when the run began, and each
/// folder then opens a scope of its own; without this, a reload landing between the two would let a folder scheduled
/// from the old account list connect with the new list's endpoint, policy, limits, and credentials — or fail because
/// the account no longer exists. The enclosing operation therefore hands its snapshot down rather than letting the
/// scope re-read one.
/// </para>
/// <para>
/// A scope with no enclosing operation — an MCP request served directly, once such a path exists — has nothing to
/// inherit and falls back to the published snapshot, which is the correct boundary for it.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The dependency injection container materializes this scoped holder.")]
internal sealed class ScopedMailSynchronizationSettings(ISettingsSnapshot<MailSynchronizationOptions> publishedSettings)
{
    private MailSynchronizationOptions? snapshot;

    /// <summary>Gets the snapshot this scope runs against, taking the published one when no operation handed it down.</summary>
    internal MailSynchronizationOptions Current => this.snapshot ??= publishedSettings.Current;

    /// <summary>Hands the enclosing operation's snapshot to this scope.</summary>
    /// <param name="runSnapshot">The snapshot the enclosing run captured when it began.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="runSnapshot" /> is <see langword="null" />.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the scope has already answered with a different snapshot.</exception>
    /// <remarks>Call this before anything in the scope reads settings; a scope that has already answered cannot change its mind without making the two answers inconsistent.</remarks>
    internal void UseRunSnapshot(MailSynchronizationOptions runSnapshot)
    {
        ArgumentNullException.ThrowIfNull(runSnapshot);

        if (this.snapshot is not null && !ReferenceEquals(this.snapshot, runSnapshot))
        {
            throw new InvalidOperationException(
                "The scope already resolved mail synchronization settings, so the enclosing run's snapshot would contradict what it has already been given.");
        }

        this.snapshot = runSnapshot;
    }
}
