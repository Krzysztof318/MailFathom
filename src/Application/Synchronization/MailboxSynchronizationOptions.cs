// Copyright © 2026 Krzysztof Kasprowicz

namespace MailMcp.Application.Synchronization;

/// <summary>Controls bounded mailbox synchronization behavior.</summary>
public sealed class MailboxSynchronizationOptions
{
    /// <summary>Gets or sets the maximum number of emails requested from one IMAP metadata batch.</summary>
    public int MaxMetadataBatchSize { get; set; } = 100;

    /// <summary>Gets or sets the maximum raw MIME size accepted for local storage.</summary>
    public long MaxRawMimeBytes { get; set; } = 25L * 1024L * 1024L;

    /// <summary>Gets or sets the maximum number of bounded metadata batches processed by one synchronization run.</summary>
    public int MaxMetadataBatchesPerRun { get; set; } = 10;

    /// <summary>Gets or sets how many already-stored emails one run re-checks against the server.</summary>
    /// <remarks>
    /// It bounds the backward pass the way <see cref="MaxMetadataBatchSize" /> bounds the forward one. A folder holding
    /// more than this reconciles the rest on later runs, because the window is ordered by how long ago each email was
    /// last observed and every observation moves an email to the back of that queue.
    /// </remarks>
    public int MaxReconciledEmailsPerRun { get; set; } = 500;
}
