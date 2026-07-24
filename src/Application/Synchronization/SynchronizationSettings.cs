// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.Domain.Accounts;
using MailMcp.Domain.Folders;

namespace MailMcp.Application.Synchronization;

/// <summary>Reads synchronization settings as immutable application-owned business snapshots.</summary>
public interface ISynchronizationSettingsReader
{
    /// <summary>Gets the latest validated settings snapshot for starting a new synchronization operation.</summary>
    MailSynchronizationSettings GetCurrentSettings();
}

/// <summary>Describes whether periodic mail synchronization should run and which accounts it may process.</summary>
public sealed record MailSynchronizationSettings(bool Enabled, TimeSpan Interval, MailboxSynchronizationOptions Limits, IReadOnlyList<MailSynchronizationAccountSettings> Accounts);

/// <summary>Describes one configured mail account and the folders authorized for synchronization.</summary>
public sealed record MailSynchronizationAccountSettings(MailAccountId AccountId, IReadOnlyList<MailFolderName> Folders);
