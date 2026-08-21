// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Collections.ObjectModel;

namespace MailFathom.Domain.Emails;

/// <summary>Holds the keywords a mail server reported for one email, in the one form everything compares them in.</summary>
/// <remarks>
/// <para>
/// A keyword is a flag the IMAP specification leaves to whoever set it — <c>$Junk</c>, <c>$Forwarded</c>, a mail client's
/// own label — as opposed to the five system flags <see cref="RemoteEmailFlagSnapshot" /> carries as booleans. There is
/// no vocabulary to validate against, so this type decides nothing about what a keyword means and only about what makes
/// two of them the same one and how many of them a message may bring with it.
/// </para>
/// <para>
/// RFC 9051 states that flag names are compared without regard to case, so the same keyword written two ways is one
/// keyword and is held here in one case. MailKit reports what the server wrote instead, octet for octet, which is why
/// the folding happens on the way in rather than being left to whoever compares: an unfolded set would store
/// <c>$Junk</c> and <c>$junk</c> as two values, and a filter matching one of them would miss mail carrying the other.
/// The case is upper, which is the comparison form <see cref="EmailAddress.NormalizedAddress" /> and
/// <see cref="Folders.MailFolderAlias" /> already publish, so nothing here is a second answer to what folding means.
/// </para>
/// <para>
/// The kept keywords are ordered and deduplicated, which makes the value a set rather than a reading of one. Two
/// observations that found the same keywords therefore produce equal values whatever order the server listed them in,
/// and the stored mirror stops being rewritten by a server that reorders its own answer.
/// </para>
/// </remarks>
public sealed record RemoteEmailKeywords
{
    /// <summary>The keywords of an email that carried none, declared before the value that publishes it.</summary>
    private static readonly ReadOnlyCollection<string> NoKeywords = new List<string>().AsReadOnly();

    private RemoteEmailKeywords(IReadOnlyList<string> values) => this.Values = values;

    /// <summary>The greatest number of keywords one email keeps.</summary>
    /// <remarks>
    /// Nothing in the protocol bounds how many keywords a server may report for a message, and the answer is stored on
    /// the email's own row, so an unbounded set is an unbounded column. The number is far above what a mail client's
    /// labelling produces and far below what a server would have to be misbehaving to send; the excess is discarded
    /// rather than refused, because a reconciliation window exists to record what the server said about mail that is
    /// already stored and failing it over a flag would stop the window recording anything at all.
    /// </remarks>
    public const int MaximumKeywords = 64;

    /// <summary>The greatest number of characters one keyword may carry.</summary>
    /// <remarks>
    /// A keyword is an IMAP atom, which no server mints at this length, while nothing between a mail client and this
    /// system enforces that. A longer one is dropped rather than truncated, for the reason a message identifier is: a
    /// prefix of a label is a label somebody else may legitimately use, and matching on it would return mail carrying a
    /// keyword nobody set.
    /// </remarks>
    public const int MaximumKeywordLength = 64;

    /// <summary>Gets the keywords, folded to upper case, without duplicates, in ordinal order.</summary>
    public IReadOnlyList<string> Values { get; }

    /// <summary>Gets the keywords of an email that carries none, which is also what an unobserved email carries.</summary>
    public static RemoteEmailKeywords None { get; } = new(NoKeywords);

    /// <summary>Builds the keyword set from what a server reported.</summary>
    /// <param name="keywords">The keywords as the server wrote them, in any order, or <see langword="null" /> when it reported none.</param>
    /// <returns>The normalized set, which is <see cref="None" /> when nothing usable was reported.</returns>
    /// <remarks>
    /// <para>
    /// A keyword that is empty, carries a control character, or is longer than <see cref="MaximumKeywordLength" /> is
    /// dropped, and so is everything past <see cref="MaximumKeywords" /> once the rest are ordered. Which keywords the
    /// bound gives up is therefore decided by the value rather than by the order one server happened to answer in, so
    /// two runs against the same message keep the same ones.
    /// </para>
    /// <para>
    /// The bound is applied while the answer is read rather than to the set it produced, which is what makes it a
    /// bound: the kept keywords are held in an ordered set that gives up its greatest value as soon as it holds one
    /// too many, so a server reporting a million flags costs this parse the memory of the sixty-four it keeps rather
    /// than the memory of the million it sent. Deduplication comes from the same set, so nothing is collected twice
    /// either.
    /// </para>
    /// </remarks>
    public static RemoteEmailKeywords Create(IEnumerable<string>? keywords)
    {
        if (keywords is null)
        {
            return None;
        }

        var kept = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var reported in keywords)
        {
            if (Normalized(reported) is not { } keyword || !kept.Add(keyword))
            {
                continue;
            }

            if (kept.Count > MaximumKeywords)
            {
                kept.Remove(kept.Max!);
            }
        }

        // Copied out rather than wrapped around the set, because an IReadOnlyList backed directly by a mutable
        // collection can be cast back to it and written through.
        return kept.Count == 0 ? None : new RemoteEmailKeywords(new List<string>(kept).AsReadOnly());
    }

    /// <summary>Reduces one written keyword to the form everything compares keywords in.</summary>
    /// <param name="keyword">The keyword as somebody wrote it.</param>
    /// <returns>The comparison form, or <see langword="null" /> when the value is not a keyword this system keeps.</returns>
    /// <remarks>
    /// This is the same reduction <see cref="Create" /> applies, published so that a caller filtering on a keyword folds
    /// its own value the way the stored ones were folded. Two implementations of it would be two answers to whether
    /// <c>$Junk</c> matches <c>$junk</c>, and only one of them would be the stored one.
    /// </remarks>
    public static string? Normalized(string? keyword)
    {
        if (keyword is null)
        {
            return null;
        }

        var trimmed = keyword.Trim();

        return trimmed.Length == 0 || trimmed.Length > MaximumKeywordLength || trimmed.Any(char.IsControl)
            ? null
            : trimmed.ToUpperInvariant();
    }

    /// <summary>Reports whether two keyword sets hold the same keywords.</summary>
    /// <param name="other">The set to compare with.</param>
    /// <returns><see langword="true" /> when both hold the same keywords.</returns>
    /// <remarks>
    /// Written by hand because the compiler's own record equality compares <see cref="Values" /> by reference, which
    /// would make two sets built from one server answer unequal. The values are ordered and deduplicated on the way in,
    /// so comparing them in sequence is comparing the sets.
    /// </remarks>
    public bool Equals(RemoteEmailKeywords? other) =>
        other is not null && this.Values.SequenceEqual(other.Values, StringComparer.Ordinal);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = default(HashCode);

        foreach (var keyword in this.Values)
        {
            hash.Add(keyword, StringComparer.Ordinal);
        }

        return hash.ToHashCode();
    }
}
