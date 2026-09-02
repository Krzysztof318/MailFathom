// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Mail.Mutations.Authoring.Failures;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Mutations;

namespace MailFathom.Application.Mail.Mutations.Authoring;

/// <summary>One caller's request to write <c>\Seen</c>, <c>\Flagged</c>, or keywords onto one email this deployment holds.</summary>
/// <remarks>
/// <para>
/// The three values travel in one request because they are one act against one message: a caller triaging mail decides
/// all of it at once, and a server writes each with the same command against the same UID. What it is not is one
/// mutation — each value asked for becomes a durable record of its own, because that is the unit convergence resumes,
/// abandons, and attributes an observation back to.
/// </para>
/// <para>
/// Every field is optional and the whole request is refused when they all are, which is the one invariant this type
/// enforces. A call that named an email and asked for nothing is a client mistake rather than a change of nothing, and
/// answering it as a success would report a record identity nothing wrote.
/// </para>
/// </remarks>
public sealed record AuthoredMailFlagChange
{
    private AuthoredMailFlagChange(
        StoredEmailId storedEmailId,
        bool? seen,
        bool? flagged,
        MailKeywordChangeDirection? keywordDirection,
        AuthoredMailKeywords? keywords)
    {
        this.StoredEmailId = storedEmailId;
        this.Seen = seen;
        this.Flagged = flagged;
        this.KeywordDirection = keywordDirection;
        this.Keywords = keywords;
    }

    /// <summary>Gets the email the change is asked for, as a listing or a search returned it.</summary>
    public StoredEmailId StoredEmailId { get; }

    /// <summary>Gets where the caller wants the <c>\Seen</c> flag left, or <see langword="null" /> to leave it alone.</summary>
    public bool? Seen { get; }

    /// <summary>Gets where the caller wants the <c>\Flagged</c> flag left, or <see langword="null" /> to leave it alone.</summary>
    public bool? Flagged { get; }

    /// <summary>Gets what the caller wants done with the keywords it listed, or <see langword="null" /> to leave them alone.</summary>
    public MailKeywordChangeDirection? KeywordDirection { get; }

    /// <summary>Gets the keywords the change names, or <see langword="null" /> when it names no keyword change at all.</summary>
    public AuthoredMailKeywords? Keywords { get; }

    /// <summary>Reads one request, refusing anything that does not state a change a server could be asked for.</summary>
    /// <param name="storedEmailId">The email the change is asked for.</param>
    /// <param name="seen">Where to leave the <c>\Seen</c> flag, or <see langword="null" /> to leave it alone.</param>
    /// <param name="flagged">Where to leave the <c>\Flagged</c> flag, or <see langword="null" /> to leave it alone.</param>
    /// <param name="keywordDirection">What to do with <paramref name="keywords" />, or <see langword="null" /> to change no keyword.</param>
    /// <param name="keywords">The keywords the change names, as the caller wrote them.</param>
    /// <returns>The request, with every value it names usable.</returns>
    /// <exception cref="MailFlagChangeInvalidException">
    /// Thrown when the request names no change at all, when a keyword list arrives without a direction or a direction
    /// without a list, when an addition or a removal names no keyword, or when a keyword is not one a <c>STORE</c> may
    /// carry.
    /// </exception>
    /// <remarks>
    /// The keyword list is validated here rather than dropped, which is the rule
    /// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0007-remote-mailbox-mutation-boundary-and-write-session.md">ADR 0007</see>
    /// states for anything authored: reading a server's answer drops a keyword it cannot use, and writing refuses one,
    /// because a caller that asked for a label needs to learn it was unusable rather than to be told the change
    /// succeeded without it.
    /// </remarks>
    public static AuthoredMailFlagChange Create(
        StoredEmailId storedEmailId,
        bool? seen,
        bool? flagged,
        MailKeywordChangeDirection? keywordDirection,
        IReadOnlyList<string>? keywords)
    {
        var authoredKeywords = ReadKeywords(keywordDirection, keywords);

        if (seen is null && flagged is null && authoredKeywords is null)
        {
            throw MailFlagChangeInvalidException.NothingAsked();
        }

        return new AuthoredMailFlagChange(storedEmailId, seen, flagged, keywordDirection, authoredKeywords);
    }

    /// <summary>Names every mutation this request asks for, in the order they are recorded.</summary>
    /// <returns>One mutation per value the request named, each with the parameters that mutation needs.</returns>
    /// <remarks>
    /// The order is <c>\Seen</c>, then <c>\Flagged</c>, then the keywords, and it decides nothing observable: the three
    /// values are independent bits of one message, so no ordering of them leaves the mailbox different. It is fixed so
    /// that the records a call opens are stable to read and stable to test, rather than following whichever field a
    /// caller happened to send first.
    /// </remarks>
    public IReadOnlyList<AuthoredMailFlagMutation> Mutations()
    {
        var mutations = new List<AuthoredMailFlagMutation>(3);

        if (this.Seen is { } seen)
        {
            mutations.Add(new AuthoredMailFlagMutation(MailboxMutation.SetSeen, seen, null, null));
        }

        if (this.Flagged is { } flagged)
        {
            mutations.Add(new AuthoredMailFlagMutation(MailboxMutation.SetFlagged, null, flagged, null));
        }

        if (this.KeywordDirection is { } direction && this.Keywords is { } keywords)
        {
            mutations.Add(new AuthoredMailFlagMutation(KeywordMutationOf(direction), null, null, keywords));
        }

        return mutations;
    }

    /// <summary>Names the mutation one keyword direction asks for.</summary>
    private static MailboxMutation KeywordMutationOf(MailKeywordChangeDirection direction) => direction switch
    {
        MailKeywordChangeDirection.Add => MailboxMutation.AddKeywords,
        MailKeywordChangeDirection.Remove => MailboxMutation.RemoveKeywords,
        MailKeywordChangeDirection.Replace => MailboxMutation.SetKeywords,
        _ => throw new ArgumentOutOfRangeException(
            nameof(direction),
            direction,
            "The keyword change direction is not one this system declares."),
    };

    /// <summary>Reads the keyword half of a request, refusing every way of stating half of one.</summary>
    /// <exception cref="MailFlagChangeInvalidException">Thrown when the two halves disagree or a keyword is unusable.</exception>
    private static AuthoredMailKeywords? ReadKeywords(
        MailKeywordChangeDirection? direction,
        IReadOnlyList<string>? keywords)
    {
        if (direction is null && keywords is null)
        {
            return null;
        }

        if (direction is not { } keywordDirection || keywords is null)
        {
            throw MailFlagChangeInvalidException.IncompleteKeywordChange();
        }

        if (!Enum.IsDefined(keywordDirection))
        {
            throw MailFlagChangeInvalidException.UnknownKeywordDirection();
        }

        if (!AuthoredMailKeywords.TryCreate(keywords, out var authored))
        {
            throw MailFlagChangeInvalidException.KeywordNotWritable();
        }

        // Only a replacement means anything by an empty list: it clears every keyword. The other two would ask a server
        // for a STORE naming no flag, which RFC 9051 has no form of, so the request is refused rather than recorded as
        // a mutation nothing could carry.
        if (authored.IsEmpty && keywordDirection != MailKeywordChangeDirection.Replace)
        {
            throw MailFlagChangeInvalidException.NoKeywordNamed();
        }

        return authored;
    }
}

/// <summary>One mutation an authored change asks for, with the parameters that mutation needs and no others.</summary>
/// <param name="Mutation">The change to record.</param>
/// <param name="DesiredSeenState">Where to leave <c>\Seen</c>, for the mutation that writes it.</param>
/// <param name="DesiredFlaggedState">Where to leave <c>\Flagged</c>, for the mutation that writes it.</param>
/// <param name="Keywords">The keywords named, for the three mutations that write them.</param>
/// <remarks>
/// It exists so the request can be walked once rather than branched over three times, and it carries the parameters
/// unvalidated against the mutation deliberately: <see cref="MailboxMutationRequest" /> is where a
/// mutation and its parameters are checked against each other, and restating that check here would give the system two
/// answers to keep in step.
/// </remarks>
public sealed record AuthoredMailFlagMutation(
    MailboxMutation Mutation,
    bool? DesiredSeenState,
    bool? DesiredFlaggedState,
    AuthoredMailKeywords? Keywords);
