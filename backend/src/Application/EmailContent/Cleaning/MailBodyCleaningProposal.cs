// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.EmailContent.Cleaning;

/// <summary>One contiguous range of block indices, and whether a cleaning keeps it or drops it.</summary>
/// <param name="From">The first block index the range covers.</param>
/// <param name="To">The last block index the range covers, which equals <paramref name="From" /> for a range of one.</param>
/// <param name="Keep">Whether the blocks in the range are drawn.</param>
/// <remarks>
/// Nothing here is believed. It is what a producer wrote, and <see cref="MailBodyCleaningSegments" /> is what decides
/// whether a set of them describes the document at all.
/// </remarks>
public sealed record MailBodyCleaningSegment(int From, int To, bool Keep);

/// <summary>Why a cleaning was not proposed at all, which is never anything about the message.</summary>
public enum MailBodyCleaningWithholding
{
    /// <summary>Nothing was withheld: the producer answered.</summary>
    None = 0,

    /// <summary>This deployment declared no chat endpoint, or its operator left the pass off.</summary>
    NotActivated = 1,

    /// <summary>The deployment has spent what it allows a provider for the current period.</summary>
    AllowanceExhausted = 2,

    /// <summary>The endpoint did not answer, which covers a refused request, a timeout, and an unresolvable credential.</summary>
    ProviderUnavailable = 3,
}

/// <summary>What a producer answered when it was asked which blocks of one body to keep.</summary>
/// <remarks>
/// <para>
/// Two states and no third: either a set of ranges arrived, whatever it turns out to say, or nothing arrived and the
/// reason is one the message had no part in. That separation is what lets the use case report a spent allowance
/// differently from an answer it could not use, which are the two things an operator reads differently.
/// </para>
/// <para>
/// A producer that answered with ranges has said everything it is going to say. Whether those ranges describe the
/// document is decided above this type rather than inside it, because the document is what they have to be checked
/// against and the producer is not the thing holding it.
/// </para>
/// </remarks>
public sealed record MailBodyCleaningProposal
{
    private MailBodyCleaningProposal(
        IReadOnlyList<MailBodyCleaningSegment> segments,
        MailBodyCleaningWithholding withholding)
    {
        this.Segments = segments;
        this.Withholding = withholding;
    }

    /// <summary>Gets the ranges the producer wrote, which is empty for a withheld proposal.</summary>
    public IReadOnlyList<MailBodyCleaningSegment> Segments { get; }

    /// <summary>Gets why nothing was proposed, or <see cref="MailBodyCleaningWithholding.None" /> where something was.</summary>
    public MailBodyCleaningWithholding Withholding { get; }

    /// <summary>States the ranges a producer answered with.</summary>
    /// <param name="segments">The ranges, in the order they were written.</param>
    /// <returns>The proposal.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="segments" /> is <see langword="null" />.</exception>
    public static MailBodyCleaningProposal Proposing(IReadOnlyList<MailBodyCleaningSegment> segments)
    {
        ArgumentNullException.ThrowIfNull(segments);

        return new MailBodyCleaningProposal(segments, MailBodyCleaningWithholding.None);
    }

    /// <summary>States that nothing was proposed, and why.</summary>
    /// <param name="withholding">The reason, which is never anything about the message.</param>
    /// <returns>The proposal.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the reason given is that nothing was withheld.</exception>
    public static MailBodyCleaningProposal Withheld(MailBodyCleaningWithholding withholding)
    {
        ArgumentOutOfRangeException.ThrowIfEqual((int)withholding, (int)MailBodyCleaningWithholding.None);

        return new MailBodyCleaningProposal([], withholding);
    }
}
