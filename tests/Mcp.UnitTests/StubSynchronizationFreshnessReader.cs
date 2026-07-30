// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.Application.Emails;
using MailMcp.Application.Synchronization;

namespace MailMcp.Mcp.UnitTests;

/// <summary>Answers a freshness read with fixed entries and records the scope it was asked about.</summary>
internal sealed class StubSynchronizationFreshnessReader(params MailboxFolderFreshness[] entries)
    : ISynchronizationFreshnessReader
{
    /// <summary>Gets the scope the last read was issued for, or <see langword="null" /> when nothing was read.</summary>
    public MailboxScope? LastScope { get; private set; }

    /// <inheritdoc />
    public Task<IReadOnlyList<MailboxFolderFreshness>> ReadAsync(
        MailboxScope scope,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        this.LastScope = scope;

        return Task.FromResult<IReadOnlyList<MailboxFolderFreshness>>(entries);
    }
}
