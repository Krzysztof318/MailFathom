// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using MailMcp.Domain.Emails;

namespace MailMcp.Application.Emails;

/// <summary>Selects which stored emails a mailbox timeline query returns, and from which end it reads them.</summary>
/// <remarks>
/// <para>
/// The structured filters live on <see cref="Selection" />, which a lexical search applies too. What this type adds is
/// the part that belongs to a timeline alone: the end it is read from, and the fingerprint a continuation cursor is
/// checked against.
/// </para>
/// <para>
/// The reading direction belongs to the filter rather than sitting beside it, because a continuation cursor names a
/// boundary in one total order over one filtered set. Treating the direction as a separate concern would let a cursor
/// issued while reading newest-first be presented while reading oldest-first, which names a row and an arbitrary window
/// around it. <see cref="Fingerprint" /> therefore covers the direction along with the filters.
/// </para>
/// </remarks>
public sealed record EmailTimelineFilter
{
    /// <summary>How many octets of the filter hash the fingerprint keeps.</summary>
    /// <remarks>
    /// The fingerprint detects a cursor presented against different filters; it is not a security control, and the
    /// documentation on <see cref="EmailTimelineCursor" /> says why one is not needed. Sixteen octets make an accidental
    /// collision between two filter sets impossible in practice while keeping the encoded cursor short.
    /// </remarks>
    private const int FingerprintOctets = 16;

    private EmailTimelineFilter(MailboxEmailSelection selection, EmailTimelineDirection direction)
    {
        this.Selection = selection;
        this.Direction = direction;
        this.Fingerprint = ComputeFingerprint(selection, direction);
    }

    /// <summary>Gets the validated structural filters the timeline is narrowed by.</summary>
    public MailboxEmailSelection Selection { get; }

    /// <summary>Gets the end of the timeline the page is read from.</summary>
    public EmailTimelineDirection Direction { get; }

    /// <summary>Gets the fingerprint identifying this filter and direction to a continuation cursor.</summary>
    /// <remarks>
    /// Two requests that select the same emails in the same order produce the same fingerprint, including when they
    /// name the same accounts in a different order or write a subject fragment in a different case. The page size is
    /// deliberately not part of it: asking for a larger or smaller page moves no boundary.
    /// </remarks>
    public string Fingerprint { get; }

    /// <summary>Validates and normalizes what a request asked for.</summary>
    /// <param name="scope">The accounts and folders to restrict to.</param>
    /// <param name="senderAddress">The address the sender must carry, in any case, or <see langword="null" /> for any sender.</param>
    /// <param name="recipientAddress">The address a <c>To</c> or <c>Cc</c> recipient must carry, or <see langword="null" /> for any recipient.</param>
    /// <param name="subjectFragment">The fragment the subject must contain, or <see langword="null" /> for any subject.</param>
    /// <param name="receivedOnOrAfter">The inclusive start of the received range, or <see langword="null" /> for no start.</param>
    /// <param name="receivedBefore">The exclusive end of the received range, or <see langword="null" /> for no end.</param>
    /// <param name="isRemotelySeen">The remote <c>\Seen</c> state to require, or <see langword="null" /> for either.</param>
    /// <param name="hasAttachments">Whether attachments are required, or <see langword="null" /> for either.</param>
    /// <param name="direction">The end of the timeline to read from.</param>
    /// <returns>The validated filter.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="scope" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="direction" /> is not a defined member.</exception>
    /// <exception cref="MailboxQueryFilterInvalidException">Thrown when an address is unusable or over-long, the subject fragment is too long, or the received range can select nothing.</exception>
    public static EmailTimelineFilter Create(
        MailboxScope scope,
        string? senderAddress,
        string? recipientAddress,
        string? subjectFragment,
        DateTimeOffset? receivedOnOrAfter,
        DateTimeOffset? receivedBefore,
        bool? isRemotelySeen,
        bool? hasAttachments,
        EmailTimelineDirection direction) => ReadIn(
        MailboxEmailSelection.Create(
            scope,
            senderAddress,
            recipientAddress,
            subjectFragment,
            receivedOnOrAfter,
            receivedBefore,
            isRemotelySeen,
            hasAttachments),
        direction);

    /// <summary>Reads an already validated selection from one end of the timeline.</summary>
    /// <param name="selection">The validated structural filters.</param>
    /// <param name="direction">The end of the timeline to read from.</param>
    /// <returns>The validated filter.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="selection" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="direction" /> is not a defined member.</exception>
    public static EmailTimelineFilter ReadIn(MailboxEmailSelection selection, EmailTimelineDirection direction)
    {
        ArgumentNullException.ThrowIfNull(selection);

        if (!Enum.IsDefined(direction))
        {
            throw new ArgumentOutOfRangeException(
                nameof(direction),
                direction,
                "The timeline is read from one of its two ends, and no other value names a direction.");
        }

        return new EmailTimelineFilter(selection, direction);
    }

    /// <summary>Hashes the canonical text of the selection and the direction, so a cursor can tell which walk it belongs to.</summary>
    /// <remarks>
    /// The selection writes every one of its fields, absent ones included, which is what keeps a filter added in a later
    /// build from producing the text an older build produced. The direction is appended with its own length prefix, for
    /// the reason every value inside that text carries one.
    /// </remarks>
    private static string ComputeFingerprint(MailboxEmailSelection selection, EmailTimelineDirection direction)
    {
        var canonicalText = string.Concat(
            selection.CanonicalText,
            MailboxEmailSelection.LengthPrefixed(direction.ToString()));

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonicalText));

        return Base64Url.EncodeToString(hash.AsSpan(0, FingerprintOctets));
    }
}
