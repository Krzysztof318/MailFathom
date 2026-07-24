// Copyright © 2026 Krzysztof Kasprowicz

using MailKit;
using MailMcp.Domain.Messages;

namespace MailMcp.Infrastructure.Mail.MailKit;

/// <summary>Calculates safe bounded UID windows without advancing checkpoints into future UID space.</summary>
internal static class MailKitUidWindowPlanner
{
    /// <summary>Creates the durable cursor for a searched UID window based on the opened folder UIDNEXT state and returned UIDs.</summary>
    internal static MailKitUidBatchCursor CreateBatchCursor(ImapUid? lastSeenUid, int maxMessageCount, uint uidNext, uint inclusiveWindowEnd, IReadOnlyCollection<uint> returnedUids)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxMessageCount);
        ArgumentOutOfRangeException.ThrowIfZero(uidNext);
        ArgumentNullException.ThrowIfNull(returnedUids);

        var currentHighWater = uidNext - 1U;
        if (currentHighWater == 0U)
        {
            return new MailKitUidBatchCursor(lastSeenUid, HasMore: false);
        }

        var safeWindowEnd = inclusiveWindowEnd > currentHighWater ? currentHighWater : inclusiveWindowEnd;
        if (returnedUids.Count == 0)
        {
            var emptyWindowCursor = lastSeenUid is { } uid && safeWindowEnd <= uid.Value ? lastSeenUid : ImapUid.Create(safeWindowEnd);
            return new MailKitUidBatchCursor(emptyWindowCursor, safeWindowEnd < currentHighWater);
        }

        var highestReturnedUid = returnedUids.Max();
        var inspectedThroughUid = highestReturnedUid > safeWindowEnd ? safeWindowEnd : highestReturnedUid;
        var hasMoreWithinKnownMailbox = inspectedThroughUid < currentHighWater;
        return new MailKitUidBatchCursor(ImapUid.Create(inspectedThroughUid), hasMoreWithinKnownMailbox);
    }

    /// <summary>Calculates the inclusive search window end capped to the opened folder's known high-water UID.</summary>
    internal static uint CalculateInclusiveWindowEnd(ImapUid? lastSeenUid, int maxMessageCount, uint uidNext)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxMessageCount);
        ArgumentOutOfRangeException.ThrowIfZero(uidNext);
        var minValue = lastSeenUid is { } uid ? uid.Value + 1U : 1U;
        var currentHighWater = uidNext - 1U;
        var requestedMaxValue = (ulong)minValue + (uint)maxMessageCount - 1UL;
        var boundedWindowEnd = requestedMaxValue > currentHighWater ? currentHighWater : (uint)requestedMaxValue;
        return boundedWindowEnd > UniqueId.MaxValue.Id ? UniqueId.MaxValue.Id : boundedWindowEnd;
    }
}

/// <summary>Describes how far a bounded MailKit UID query can safely advance and whether more known UID space remains.</summary>
internal sealed record MailKitUidBatchCursor(ImapUid? InspectedThroughUid, bool HasMore);
