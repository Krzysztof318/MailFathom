// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Collections.ObjectModel;
using MailFathom.Domain.Emails;

namespace MailFathom.Domain.Mutations;

/// <summary>Holds the keywords one authored change names, in the form they will be written to a mail server.</summary>
/// <remarks>
/// <para>
/// It is a separate type from <see cref="RemoteEmailKeywords" /> because the two answer opposite questions about the
/// same word. That one reads what a server reported, where an unusable keyword is dropped so that one odd flag cannot
/// stop a reconciliation window recording the rest of a mailbox. This one carries what somebody typed, where dropping
/// is the wrong answer twice over: a rule would silently do less than it says, and the operator would have no way to
/// find out which of their keywords was the problem. So an unusable keyword is refused here, before it is durable and
/// while the configuration key that carries it can still be named.
/// </para>
/// <para>
/// The written form is kept rather than the comparison form, which is the other half of the difference. Flag names are
/// case-insensitive in RFC 9051, so folding is the right answer for deciding whether two observations found the same
/// keyword; it is the wrong answer for a <c>STORE</c>, because the operator's own mail client renders whatever this
/// puts on their message and <c>$TODO</c> where they wrote <c>$Todo</c> is a surprise this system has no reason to
/// hand them. Sameness is still decided by the folded form — the set deduplicates, orders, and compares by it — so
/// nothing about matching changes and only what goes on the wire does.
/// </para>
/// <para>
/// A keyword is an IMAP atom, and <see cref="IsWritable" /> is where that grammar is enforced. The refusal that matters
/// most is the leading backslash: it is how a system flag is spelled, so refusing it is what keeps <c>\Answered</c> and
/// <c>\Draft</c> out of a keyword list and therefore refused as this system says they are.
/// </para>
/// </remarks>
public sealed record AuthoredMailKeywords
{
    /// <summary>The keywords of a change that names none, declared before the value that publishes it.</summary>
    private static readonly ReadOnlyCollection<string> NoKeywords = new List<string>().AsReadOnly();

    private AuthoredMailKeywords(IReadOnlyList<string> values) => this.Values = values;

    /// <summary>Gets the keywords of a change that names none, which is what clearing every keyword asks for.</summary>
    public static AuthoredMailKeywords None { get; } = new(NoKeywords);

    /// <summary>Gets the keywords as they were written, without duplicates, ordered by their comparison form.</summary>
    public IReadOnlyList<string> Values { get; }

    /// <summary>Gets whether the change names no keyword at all.</summary>
    public bool IsEmpty => this.Values.Count == 0;

    /// <summary>Reports whether a keyword is one MailFathom may ask a server to store.</summary>
    /// <param name="keyword">The keyword as somebody wrote it.</param>
    /// <returns><see langword="true" /> when a <c>STORE</c> may name it.</returns>
    /// <remarks>
    /// <para>
    /// The bounds <see cref="RemoteEmailKeywords.Normalized" /> already applies are applied through it rather than
    /// restated, so how long a keyword may be is one answer in one place. What this adds is the atom grammar, whose
    /// excluded characters are the atom specials <c>( ) { % * " \ ]</c>, the space, every control character, and
    /// everything above US-ASCII that the grammar's <c>CHAR</c> does not reach.
    /// </para>
    /// <para>
    /// The grammar is judged against the written form rather than the folded one, because the written form is what a
    /// <c>STORE</c> sends. Invariant upper-casing maps a few characters into US-ASCII from outside it — <c>ſ</c>
    /// becomes <c>S</c> — so reading the grammar off the folded form would accept a keyword that reaches the server as
    /// bytes an atom cannot hold.
    /// </para>
    /// </remarks>
    public static bool IsWritable(string? keyword) =>
        keyword is not null
        && RemoteEmailKeywords.Normalized(keyword) is not null
        && keyword.Trim().All(IsAtomCharacter);

    /// <summary>Builds the keyword set one authored change names.</summary>
    /// <param name="keywords">The keywords as they were written, in any order.</param>
    /// <returns>The set, which is <see cref="None" /> when nothing was named.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="keywords" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when a keyword is not one a <c>STORE</c> may name, or when more are named than one email keeps.</exception>
    public static AuthoredMailKeywords Create(IEnumerable<string> keywords) =>
        TryCreate(keywords, out var authored)
            ? authored
            : throw new ArgumentException(
                $"Every keyword an authored change names must be an IMAP atom that does not begin with a backslash, and at most {RemoteEmailKeywords.MaximumKeywords} of them may be named.",
                nameof(keywords));

    /// <summary>Reads the keyword set one authored change names, without raising over a value somebody mistyped.</summary>
    /// <param name="keywords">The keywords as they were written, in any order.</param>
    /// <param name="authored">The set when every keyword is one a <c>STORE</c> may name; otherwise <see cref="None" />.</param>
    /// <returns><see langword="true" /> when the set was read.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="keywords" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// This is what configuration reads through, so that an unusable keyword is reported against the key an operator
    /// edits rather than raised out of the reading. Two keywords differing only in case are one keyword, and the first
    /// spelling written is the one kept — an arbitrary choice between two spellings of the same thing, made
    /// deterministically so that a rule set's derived revision does not move because a list was reordered.
    /// </remarks>
    public static bool TryCreate(IEnumerable<string> keywords, out AuthoredMailKeywords authored)
    {
        ArgumentNullException.ThrowIfNull(keywords);

        authored = None;
        var kept = new SortedDictionary<string, string>(StringComparer.Ordinal);

        foreach (var written in keywords)
        {
            if (!IsWritable(written))
            {
                return false;
            }

            kept.TryAdd(RemoteEmailKeywords.Normalized(written)!, written.Trim());
        }

        if (kept.Count > RemoteEmailKeywords.MaximumKeywords)
        {
            return false;
        }

        // Copied out rather than wrapped around the dictionary's values, because an IReadOnlyList backed directly by a
        // mutable collection can be cast back to it and written through.
        authored = kept.Count == 0 ? None : new AuthoredMailKeywords([.. kept.Values]);

        return true;
    }

    /// <summary>Reports whether two authored sets name the same keywords, whichever way each was spelled.</summary>
    /// <param name="other">The set to compare with.</param>
    /// <returns><see langword="true" /> when both name the same keywords.</returns>
    /// <remarks>
    /// Written by hand for the reason <see cref="RemoteEmailKeywords.Equals(RemoteEmailKeywords)" /> is, and folded for
    /// the reason that type folds: <c>$Todo</c> and <c>$todo</c> are one keyword, so two sets naming them are one set.
    /// </remarks>
    public bool Equals(AuthoredMailKeywords? other) =>
        other is not null && this.Values.SequenceEqual(other.Values, StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = default(HashCode);

        foreach (var keyword in this.Values)
        {
            hash.Add(keyword, StringComparer.OrdinalIgnoreCase);
        }

        return hash.ToHashCode();
    }

    /// <summary>Reports whether one character may appear in an IMAP atom, which is what a keyword is.</summary>
    private static bool IsAtomCharacter(char character) =>
        character is > ' ' and < (char)0x7F && !"(){%*\"\\]".Contains(character, StringComparison.Ordinal);
}
