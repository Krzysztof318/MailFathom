// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Synchronization.Administration;

/// <summary>Reads the durable synchronization progress of every folder this deployment has ever synchronized.</summary>
/// <remarks>
/// It exists beside <see cref="Checkpoints.ISynchronizationFreshnessReader" /> rather than replacing it, because the two
/// answer different questions for different callers. Freshness is attached to every mailbox query and is bounded by what
/// that caller may read, so it says only how current an alias is; this one is an administrative read that also reports
/// how far the progress has come, and it is bounded by nothing, because an operator administering the deployment is
/// asking about folders rather than about mail.
/// </remarks>
public interface IMailFolderSynchronizationProgressReader
{
    /// <summary>Reads the most recent progress of every alias that has any.</summary>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>One entry per alias whose progress has moved, ordered ordinally by account and then by alias.</returns>
    /// <exception cref="OperationCanceledException">Thrown when the caller cancels.</exception>
    /// <remarks>
    /// An alias no run has ever committed progress for is absent rather than reported empty, because the folders a
    /// deployment has are what configuration names and not what this store happens to hold: a caller composes the folder
    /// list from configuration and reads an absence here as a folder synchronization has not reached.
    /// <para>
    /// It takes no page, because one entry per alias is already the bound. An alias bound to several remote folders over
    /// time contributes one entry however many bindings it has, so the size follows the folders an operator has
    /// configured over the deployment's life rather than anything a mail server can grow by recreating one.
    /// </para>
    /// </remarks>
    Task<IReadOnlyList<MailFolderSynchronizationProgress>> ReadAsync(CancellationToken cancellationToken);
}
