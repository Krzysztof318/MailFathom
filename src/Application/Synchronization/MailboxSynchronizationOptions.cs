// Copyright © 2026 Krzysztof Kasprowicz

namespace MailMcp.Application.Synchronization;

/// <summary>Controls bounded mailbox synchronization behavior.</summary>
public sealed class MailboxSynchronizationOptions
{
    /// <summary>Gets or sets the maximum metadata records requested from one IMAP batch.</summary>
    public int MaxMetadataBatchSize { get; set; } = 100;

    /// <summary>Gets or sets the maximum raw MIME size accepted for local storage.</summary>
    public long MaxRawMimeBytes { get; set; } = 25L * 1024L * 1024L;

    /// <summary>Gets or sets the maximum bounded UID windows inspected by one synchronization run.</summary>
    public int MaxUidWindowsPerRun { get; set; } = 10;
}
