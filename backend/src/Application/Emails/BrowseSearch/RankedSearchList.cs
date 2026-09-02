// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Emails.Search;

namespace MailFathom.Application.Emails.BrowseSearch;

/// <summary>The one ranked list a paged search walks: what the query is, what the filters admit, and the fingerprint tying a cursor to both.</summary>
/// <remarks>
/// <para>
/// It is to a search what <see cref="EmailTimelineFilter" /> is to a timeline. A timeline's identity is its filters and
/// the end it is read from; a ranked list's is its filters and the text it is ranked against, because changing either
/// produces a different sequence and a boundary taken in one names nothing in the other.
/// </para>
/// <para>
/// The filters are held apart from the query text rather than folded into it, which is the whole of what constraining
/// rather than ranking means here: <see cref="Selection" /> reaches the query as a predicate, so a message it excludes
/// cannot be returned however well it matches, and <see cref="QueryText" /> reaches it as the text the remaining mail is
/// ordered by. Nothing in this type can turn one into the other.
/// </para>
/// <para>
/// The list is bounded at <see cref="MaximumRankedDepth" /> results, and paging walks inside that bound. A ranked order
/// has no total ordering of its own the way a timeline has — indexing one message re-ranks everything — so what a
/// cursor into it names is a place in a list of fixed depth rather than a position in an order that will still exist
/// tomorrow. <see cref="RankedSearchCursor" /> states what that costs a client.
/// </para>
/// </remarks>
public sealed record RankedSearchList
{
    /// <summary>How many ranked results paging can reach before the list ends.</summary>
    /// <remarks>
    /// <para>
    /// The depth is fixed rather than grown as a caller pages, because a fused order is computed from where each ranking
    /// placed a message and therefore changes with how deep each ranking reached. A list re-ranked to a greater depth
    /// for a later page would be a different list, and the cursor a client was holding would name a place in the one it
    /// no longer receives — rows repeated at one boundary and skipped at the next. One depth for every page is what
    /// makes the sequence one sequence.
    /// </para>
    /// <para>
    /// Two hundred is what the MCP search already pays for its deepest window: fifty results ranked four candidates deep
    /// per side. So a page of this list costs what that surface's worst case costs, and costs it once per page rather
    /// than more the further somebody has paged. It is also a bound on how much of a mailbox one query can walk out of
    /// the deployment — a caller who has read two hundred results without finding what they wanted narrows the filters,
    /// which is the thing that would have found it sooner anyway.
    /// </para>
    /// </remarks>
    public const int MaximumRankedDepth = 200;

    /// <summary>How many octets of the hash the fingerprint keeps.</summary>
    /// <remarks>Sixteen, as the timeline's is, and for the reason stated there: it detects a cursor presented against a different list rather than resisting a search for a collision.</remarks>
    private const int FingerprintOctets = 16;

    private RankedSearchList(MailboxEmailSelection selection, EmailSearchQueryText queryText)
    {
        this.Selection = selection;
        this.QueryText = queryText;
        this.Fingerprint = ComputeFingerprint(selection, queryText);
    }

    /// <summary>Gets the validated filters that decide which mail is eligible before anything is ranked.</summary>
    public MailboxEmailSelection Selection { get; }

    /// <summary>Gets the validated free text the eligible mail is ranked against.</summary>
    public EmailSearchQueryText QueryText { get; }

    /// <summary>Gets the fingerprint identifying this list to a continuation cursor.</summary>
    /// <remarks>
    /// Two requests that select the same mail and search it for the same text produce the same fingerprint, including
    /// when they name the same accounts in a different order. The page size is deliberately not part of it: asking for a
    /// larger or a smaller page moves no boundary in the ranking.
    /// </remarks>
    public string Fingerprint { get; }

    /// <summary>Validates and normalizes what a search asked for.</summary>
    /// <param name="scope">The accounts and folders to restrict the search to.</param>
    /// <param name="queryText">The free text to rank the eligible mail against.</param>
    /// <param name="senderAddress">The address the sender must carry, in any case, or <see langword="null" /> for any sender.</param>
    /// <param name="recipientAddress">The address a <c>To</c> or <c>Cc</c> recipient must carry, or <see langword="null" /> for any recipient.</param>
    /// <param name="receivedOnOrAfter">The inclusive start of the received range, or <see langword="null" /> for no start.</param>
    /// <param name="receivedBefore">The exclusive end of the received range, or <see langword="null" /> for no end.</param>
    /// <param name="isRemotelySeen">The remote <c>\Seen</c> state to require, or <see langword="null" /> for either.</param>
    /// <param name="isRemotelyFlagged">The remote <c>\Flagged</c> state to require, or <see langword="null" /> for either.</param>
    /// <param name="hasAttachments">Whether attachments are required, or <see langword="null" /> for either.</param>
    /// <returns>The validated list.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="scope" /> is <see langword="null" />.</exception>
    /// <exception cref="MailboxQueryFilterInvalidException">Thrown when the query text is blank or unusable, an address is unusable or over-long, or the received range can select nothing.</exception>
    public static RankedSearchList Create(
        MailboxScope scope,
        string? queryText,
        string? senderAddress,
        string? recipientAddress,
        DateTimeOffset? receivedOnOrAfter,
        DateTimeOffset? receivedBefore,
        bool? isRemotelySeen,
        bool? isRemotelyFlagged,
        bool? hasAttachments) => new(
        MailboxEmailSelection.Create(
            scope,
            senderAddress,
            recipientAddress,
            subjectFragment: null,
            receivedOnOrAfter,
            receivedBefore,
            isRemotelySeen,
            isRemotelyFlagged,
            keyword: null,
            hasAttachments),
        EmailSearchQueryText.Create(queryText));

    /// <summary>Hashes the canonical text of the filters and the query, so a cursor can tell which list it belongs to.</summary>
    /// <remarks>
    /// The selection writes every one of its fields, absent ones included, which is what keeps a filter added in a later
    /// build from producing the text an older build produced. The query text is appended with its own length prefix, for
    /// the reason every value inside that text carries one, and in the form the full-text parser receives rather than
    /// folded: what two spellings of a query have in common is the database's decision, and a cursor cannot anticipate it.
    /// </remarks>
    private static string ComputeFingerprint(MailboxEmailSelection selection, EmailSearchQueryText queryText)
    {
        var canonicalText = string.Concat(
            selection.CanonicalText,
            MailboxEmailSelection.LengthPrefixed(queryText.Value));

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonicalText));

        return Base64Url.EncodeToString(hash.AsSpan(0, FingerprintOctets));
    }
}
