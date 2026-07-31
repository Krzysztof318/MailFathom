// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

using System.Globalization;
using MailFathom.Domain.Failures;

namespace MailFathom.Application.Emails;

/// <summary>The failure raised when one filter of a mailbox query carries something the query does not accept.</summary>
/// <remarks>
/// <para>
/// A filter a caller supplies reaches a database predicate, so each one states what it accepts and anything else is
/// refused rather than absorbed. Truncating an over-long filter, or keeping a value that can match no row, would be
/// worse than failing: the query would run against a filter nobody wrote and the caller would read its result as the
/// answer to the request they sent.
/// </para>
/// <para>
/// The message names the filter and, where there is one, its limit. It never repeats the value that was refused,
/// because an address and a subject fragment are mail content; both the filter name and the limit come from this
/// assembly rather than from the request.
/// </para>
/// </remarks>
public sealed class MailboxQueryFilterInvalidException : MailFathomException
{
    private MailboxQueryFilterInvalidException(string operatorSafeMessage, string filterName)
        : base(operatorSafeMessage) => this.FilterName = filterName;

    private MailboxQueryFilterInvalidException(string operatorSafeMessage, string filterName, Exception cause)
        : base(operatorSafeMessage, cause) => this.FilterName = filterName;

    /// <summary>Gets the filter that was refused, named as this assembly names it.</summary>
    public string FilterName { get; }

    /// <inheritdoc />
    public override MailFathomErrorCode ErrorCode => MailFathomErrorCode.MailboxQueryFilterInvalid;

    /// <summary>Refuses a filter whose value is not an address any stored participant could carry.</summary>
    /// <param name="filterName">How this assembly names the filter, for example <c>sender address</c>.</param>
    /// <returns>The failure to raise.</returns>
    /// <remarks>
    /// An unusable address is refused rather than kept as a value that matches nothing, so a caller learns that their
    /// input was rejected instead of concluding that the mailbox holds no such mail.
    /// </remarks>
    public static MailboxQueryFilterInvalidException NotAnAddress(string filterName) => new(
        string.Format(
            CultureInfo.InvariantCulture,
            "The mailbox query {0} filter is not a usable mail address.",
            filterName),
        filterName);

    /// <summary>Refuses text that names no identity this system issues, such as an account identifier or a folder alias.</summary>
    /// <param name="filterName">How this assembly names the filter, for example <c>folder aliases</c>.</param>
    /// <param name="cause">The validation failure the domain identity raised, kept as the inner exception.</param>
    /// <returns>The failure to raise.</returns>
    /// <remarks>
    /// A protocol adapter converts a caller's text into the domain identities a query is expressed in, and text that is
    /// neither — blank, or carrying a control character — is refused there. It reports the refusal through this failure so
    /// an adapter needs no failure of its own: the code is already the one allocated for a filter the query cannot accept,
    /// and a second one would mean two codes for one answer. The cause travels as the inner exception, where an operator
    /// reads it and no client does.
    /// </remarks>
    public static MailboxQueryFilterInvalidException NotAUsableIdentifier(string filterName, Exception cause) => new(
        NotAUsableIdentifierMessage(filterName),
        filterName,
        cause);

    /// <summary>Refuses text that names no identity this system issues, where no other failure explains why.</summary>
    /// <param name="filterName">How this assembly names the filter, for example <c>folder aliases</c>.</param>
    /// <returns>The failure to raise.</returns>
    /// <remarks>
    /// The overload without a cause exists for the checks an adapter makes itself, before a domain type is asked to read
    /// the text: a length or a character class it refuses outright raises no exception of its own to carry.
    /// </remarks>
    public static MailboxQueryFilterInvalidException NotAUsableIdentifier(string filterName) =>
        new(NotAUsableIdentifierMessage(filterName), filterName);

    private static string NotAUsableIdentifierMessage(string filterName) => string.Format(
        CultureInfo.InvariantCulture,
        "The mailbox query {0} filter names a value this system does not issue.",
        filterName);

    /// <summary>Refuses a text filter that carries a control character.</summary>
    /// <param name="filterName">How this assembly names the filter, for example <c>subject fragment</c>.</param>
    /// <returns>The failure to raise.</returns>
    /// <remarks>
    /// A control character is refused at this boundary rather than sent onward, because PostgreSQL text cannot hold a
    /// zero byte at all: a fragment carrying one would leave the query as a provider exception instead of the stable
    /// failure this boundary publishes. The refusal covers the whole class rather than that one character, since no
    /// subject a caller could be looking for part of contains any of them.
    /// </remarks>
    public static MailboxQueryFilterInvalidException ContainsControlCharacter(string filterName) => new(
        string.Format(
            CultureInfo.InvariantCulture,
            "The mailbox query {0} filter contains a control character.",
            filterName),
        filterName);

    /// <summary>Refuses a filter that carries no text where the query has no meaning without one.</summary>
    /// <param name="filterName">How this assembly names the filter, for example <c>search query</c>.</param>
    /// <returns>The failure to raise.</returns>
    /// <remarks>
    /// Only a filter whose absence changes what the operation is uses this. Most filters are optional and an absent one
    /// simply widens the result, so the refusal exists for the case where a blank value would silently turn the request
    /// into a different query than the caller wrote.
    /// </remarks>
    public static MailboxQueryFilterInvalidException Blank(string filterName) => new(
        string.Format(
            CultureInfo.InvariantCulture,
            "The mailbox query {0} carries no text, and the query has no meaning without one.",
            filterName),
        filterName);

    /// <summary>Refuses a range filter whose end is not after its start.</summary>
    /// <param name="filterName">How this assembly names the filter, for example <c>received date range</c>.</param>
    /// <returns>The failure to raise.</returns>
    /// <remarks>
    /// A range that ends where it starts, or before it, selects nothing. It is refused for the reason an unusable
    /// address is: the caller wrote two bounds and a page of nothing would read as an answer about the mailbox.
    /// </remarks>
    public static MailboxQueryFilterInvalidException EmptyRange(string filterName) => new(
        string.Format(
            CultureInfo.InvariantCulture,
            "The mailbox query {0} ends before it starts and can select nothing.",
            filterName),
        filterName);

    /// <summary>Refuses a filter that names more distinct values than the query accepts.</summary>
    /// <param name="count">How many distinct values the filter names.</param>
    /// <param name="limit">The greatest number of values the filter accepts.</param>
    /// <param name="filterName">How this assembly names the filter, for example <c>accounts</c>.</param>
    /// <exception cref="MailboxQueryFilterInvalidException">Thrown when <paramref name="count" /> exceeds <paramref name="limit" />.</exception>
    public static void ThrowIfCountExceeded(int count, int limit, string filterName)
    {
        if (count > limit)
        {
            throw new MailboxQueryFilterInvalidException(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "A mailbox query may name at most {0} {1}.",
                    limit,
                    filterName),
                filterName);
        }
    }

    /// <summary>Refuses a filter whose text is longer than the query accepts.</summary>
    /// <param name="length">How many characters the filter text carries.</param>
    /// <param name="limit">The greatest number of characters the filter accepts.</param>
    /// <param name="filterName">How this assembly names the filter, for example <c>subject fragment</c>.</param>
    /// <exception cref="MailboxQueryFilterInvalidException">Thrown when <paramref name="length" /> exceeds <paramref name="limit" />.</exception>
    public static void ThrowIfLengthExceeded(int length, int limit, string filterName)
    {
        if (length > limit)
        {
            throw new MailboxQueryFilterInvalidException(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "A mailbox query {0} is limited to {1} characters.",
                    filterName,
                    limit),
                filterName);
        }
    }
}
