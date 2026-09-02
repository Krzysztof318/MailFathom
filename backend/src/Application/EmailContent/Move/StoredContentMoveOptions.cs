// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.EmailContent.Move;

/// <summary>Bounds one pass of the move, which is half of what makes the move yield to ordinary work.</summary>
/// <remarks>
/// The other half is the interval between passes, which belongs to the worker that drives them. Together they are the
/// rate: a pass carries at most this much and then stops, and nothing carries the move again until the interval has
/// elapsed — so a deployment synchronizing, delivering, and answering reads keeps the database, the network, and the
/// process to itself for most of every interval.
/// </remarks>
public sealed class StoredContentMoveOptions
{
    /// <summary>Gets or sets how many payloads one pass carries before it ends.</summary>
    /// <remarks>
    /// The count bounds how many messages a pass reads and says nothing about how large they are, which is why the byte
    /// ceiling below exists beside it. What an interrupted pass loses is at most the payload it was carrying, because
    /// every payload is repointed on its own.
    /// </remarks>
    public int PayloadsPerPass { get; set; } = 20;

    /// <summary>Gets or sets how many bytes of raw MIME one pass carries before it ends, whatever the count says.</summary>
    /// <remarks>
    /// A mailbox of one-kilobyte notifications and one of messages carrying video differ by three orders of magnitude
    /// for the same twenty rows, and it is the bytes rather than the rows that decide what the move costs the endpoint
    /// and the network. The pass ends on whichever ceiling it reaches first, and what it left behind is the next pass's.
    /// </remarks>
    public long MaxBytesPerPass { get; set; } = 64L * 1024 * 1024;
}
