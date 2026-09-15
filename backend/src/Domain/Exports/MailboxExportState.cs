// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Domain.Exports;

/// <summary>Where one export of a mailbox stands, which is the whole of what a caller holding its identity can read.</summary>
/// <remarks>
/// <para>
/// The export's own state rather than the state of the job that writes it. A job is the deployment's unit of retryable
/// work and says nothing about an archive an operator may still download, cancel, or delete; the four terminal values
/// below each describe an archive rather than an attempt, which is why the two are kept apart.
/// </para>
/// <para>
/// <see cref="Completed" /> is the one state an archive can be downloaded in. Every other value answers a download with
/// a refusal, and the difference between them is what an operator does next: a failure names a code, a cancellation and
/// a deletion were asked for, and an expiry is the retention period having run out.
/// </para>
/// </remarks>
public enum MailboxExportState
{
    /// <summary>The export is recorded and its work is queued; nothing has been written to the content store yet.</summary>
    Queued = 0,

    /// <summary>The archive is being written, and the counts recorded beside it are what has reached it so far.</summary>
    Running = 1,

    /// <summary>The archive is written whole and can be downloaded until its retention period ends.</summary>
    Completed = 2,

    /// <summary>The archive could not be written; the recorded failure code says why, and nothing downloadable was left behind.</summary>
    Failed = 3,

    /// <summary>An operator asked for the export to stop; whatever had been written was deleted.</summary>
    Cancelled = 4,

    /// <summary>The retention period ended and the archive was deleted from the content store.</summary>
    Expired = 5,

    /// <summary>An operator asked for the archive to go before its retention period ended, and it did.</summary>
    Deleted = 6,
}
