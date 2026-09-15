// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Synchronization;

/// <summary>Controls bounded mailbox synchronization behavior.</summary>
public sealed class MailboxSynchronizationOptions
{
    /// <summary>Gets or sets the maximum number of emails requested from one IMAP metadata batch.</summary>
    public int MaxMetadataBatchSize { get; set; } = 100;

    /// <summary>Gets or sets the maximum raw MIME size accepted for local storage.</summary>
    public long MaxRawMimeBytes { get; set; } = 25L * 1024L * 1024L;

    /// <summary>Gets or sets the maximum number of bounded metadata batches processed by one synchronization run.</summary>
    public int MaxMetadataBatchesPerRun { get; set; } = 10;

    /// <summary>Gets or sets how many raw MIME bytes one folder run may fetch before it ends at its checkpoint.</summary>
    /// <remarks>
    /// It bounds the volume a run ingests, which the batch settings above bound only in messages: a thousand
    /// occurrences of <see cref="MaxRawMimeBytes" /> each is a legal run under them and gigabytes of content in
    /// practice. A run that spends it commits what it stored, ends at the occurrence it reached, and the next run
    /// resumes there, so a mailbox full of large attachments fills storage gradually instead of in one pass. It must
    /// be at least <see cref="MaxRawMimeBytes" />, or the first large message would end every run before it.
    /// </remarks>
    public long MaxContentBytesPerRun { get; set; } = 1024L * 1024L * 1024L;

    /// <summary>Gets or sets how many already-stored emails one run re-checks against the server.</summary>
    /// <remarks>
    /// It bounds the backward pass the way <see cref="MaxMetadataBatchSize" /> bounds the forward one. A folder holding
    /// more than this reconciles the rest on later runs, because the window is ordered by how long ago each email was
    /// last observed and every observation moves an email to the back of that queue.
    /// </remarks>
    public int MaxReconciledEmailsPerRun { get; set; } = 500;

    /// <summary>Gets or sets how many messages one run of a held account takes off its source server.</summary>
    /// <remarks>
    /// The drain runs at the end of a synchronization run, so this is what keeps emptying a mailbox of years from
    /// crowding out the synchronization it sits behind: a run takes this many in hand once and ends, and the next run
    /// takes the next lot. Two hundred is roughly a minute of a source server's work.
    /// </remarks>
    public int MaxDrainedEmailsPerRun { get; set; } = 200;

    /// <summary>Gets or sets how many UIDs one <c>UID STORE</c> and <c>UID EXPUNGE</c> pair names.</summary>
    /// <remarks>
    /// A separate bound from the one above because it bounds a different thing: not how much work a run does, but how
    /// long one command's UID set is, which is what keeps a command inside what a server will accept on one line.
    /// </remarks>
    public int MaxDrainedEmailsPerCommand { get; set; } = 50;
}
