// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Mail.Maintenance;

/// <summary>What is carrying the segment a re-derivation run is on, which is what decides the operator's next act.</summary>
/// <remarks>
/// The three are separate because each asks for a different one. A run being carried is one to watch; a queue at its
/// bound is one to ask for again once it has drained; a segment nothing is carrying is one to return through the
/// queue's own commands. Collapsing them into "queued or not" would send an operator to the wrong one of those, and
/// waiting on a run nothing will advance is the outcome that costs the most, because nothing about it looks wrong.
/// </remarks>
public enum StoredMailRederivationCarriage
{
    /// <summary>The segment is waiting in the queue or is being worked, so the run advances on its own.</summary>
    Carried = 0,

    /// <summary>The queue already held as much of this job type as it accepts, so nothing took the segment.</summary>
    /// <remarks>Backpressure rather than failure: the run is recorded, and asking again is what puts it in motion.</remarks>
    QueueAtCapacity = 1,

    /// <summary>The run is outstanding and nothing is carrying its segment, so it will not advance until somebody acts.</summary>
    /// <remarks>
    /// A segment that dead-lettered or was dropped is what this ordinarily means, and <c>mfctl jobs retry</c> is what
    /// returns it. It also covers the narrow race in which the segment this request read was completed underneath it;
    /// the answer is then conservative rather than wrong, because asking again reads the segment the run moved on to.
    /// </remarks>
    Stopped = 2,
}
