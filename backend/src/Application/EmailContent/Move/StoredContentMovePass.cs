// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.EmailContent.Move;

/// <summary>What one bounded pass of the move carried, and whether the database still holds payloads behind it.</summary>
/// <remarks>
/// The counts are this pass's rather than the move's, which the run record accumulates. They are separate because the
/// two are read by different readers: an operator watches the run, and the worker driving the pass reads only whether
/// there is more to do.
/// </remarks>
/// <param name="CopiedPayloadCount">How many payloads this pass copied, verified, and repointed.</param>
/// <param name="FailedPayloadCount">How many payloads this pass refused to repoint, each left in the database.</param>
/// <param name="MovedByteCount">How many bytes of raw MIME the copied payloads carried.</param>
/// <param name="PayloadsRemain">Whether the move has payloads left to reach.</param>
public sealed record StoredContentMovePass(
    long CopiedPayloadCount,
    long FailedPayloadCount,
    long MovedByteCount,
    bool PayloadsRemain)
{
    /// <summary>The pass of a deployment with no move to carry, which copied nothing and leaves nothing to resume.</summary>
    /// <remarks>
    /// What a deployment that was never asked for a move reports, and what a paused or finished one reports. The three
    /// are one answer here because the pass has the same nothing to do in each, and which of them it was is the run
    /// record's to say rather than this one's.
    /// </remarks>
    public static StoredContentMovePass Idle { get; } = new(0, 0, 0, PayloadsRemain: false);
}
