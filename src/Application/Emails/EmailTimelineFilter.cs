// Copyright © 2026 Krzysztof Kasprowicz

using System.Buffers.Text;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using MailMcp.Domain.Emails;

namespace MailMcp.Application.Emails;

/// <summary>Selects which stored emails a mailbox timeline query returns, and from which end it reads them.</summary>
/// <remarks>
/// <para>
/// Every value here is already validated and already normalized, so the reader that turns it into a query decides
/// nothing and no filter is normalized twice. Addresses arrive in the comparison form the persistence layer indexes,
/// the scope is deduplicated and ordered, and a range that could select nothing was refused rather than passed on.
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
    /// <summary>The greatest number of characters a subject fragment may carry.</summary>
    /// <remarks>Longer than any subject worth searching for a part of, and short enough that the pattern reaching the database stays bounded.</remarks>
    public const int MaximumSubjectFragmentLength = 256;

    /// <summary>The greatest number of characters an address filter may carry.</summary>
    /// <remarks>
    /// The longest forward path RFC 5321 accepts, which is also what bounds every stored address column. Nothing in the
    /// address grammar bounds a length on its own, so a caller could otherwise send a megabyte of local part that no
    /// stored address could ever equal and have it reach a query parameter regardless.
    /// </remarks>
    public const int MaximumAddressFilterLength = 320;

    /// <summary>How many octets of the filter hash the fingerprint keeps.</summary>
    /// <remarks>
    /// The fingerprint detects a cursor presented against different filters; it is not a security control, and the
    /// documentation on <see cref="EmailTimelineCursor" /> says why one is not needed. Sixteen octets make an accidental
    /// collision between two filter sets impossible in practice while keeping the encoded cursor short.
    /// </remarks>
    private const int FingerprintOctets = 16;

    /// <summary>Separates the fields of the text the fingerprint is computed over.</summary>
    /// <remarks>
    /// The separator makes the text readable rather than unambiguous. Every value written beside it carries its own
    /// length in front of it, because an account identifier and a folder alias may contain any character a separator
    /// could be chosen from — a comma, this one — and two scopes whose joined text agreed would share one cursor.
    /// </remarks>
    private const char CanonicalFieldSeparator = '\u001f';

    /// <summary>Marks a filter nobody named in the text the fingerprint is computed over.</summary>
    private const string CanonicalAbsentValue = "-";

    private EmailTimelineFilter(
        MailboxScope scope,
        string? senderNormalizedAddress,
        string? recipientNormalizedAddress,
        string? subjectFragment,
        DateTimeOffset? receivedOnOrAfter,
        DateTimeOffset? receivedBefore,
        bool? isRemotelySeen,
        bool? hasAttachments,
        EmailTimelineDirection direction)
    {
        this.Scope = scope;
        this.SenderNormalizedAddress = senderNormalizedAddress;
        this.RecipientNormalizedAddress = recipientNormalizedAddress;
        this.SubjectFragment = subjectFragment;
        this.ReceivedOnOrAfter = receivedOnOrAfter;
        this.ReceivedBefore = receivedBefore;
        this.IsRemotelySeen = isRemotelySeen;
        this.HasAttachments = hasAttachments;
        this.Direction = direction;
        this.Fingerprint = this.ComputeFingerprint();
    }

    /// <summary>Gets the accounts and folders the query is restricted to.</summary>
    public MailboxScope Scope { get; }

    /// <summary>Gets the comparison form of the address the sender must carry, or <see langword="null" /> when any sender matches.</summary>
    public string? SenderNormalizedAddress { get; }

    /// <summary>Gets the comparison form of the address a recipient must carry, or <see langword="null" /> when any recipient matches.</summary>
    /// <remarks>
    /// A recipient is an addressee: the filter matches an email whose <c>To</c> or <c>Cc</c> header names the address.
    /// <c>Reply-To</c> is deliberately not a recipient role — it names where an answer should go rather than who received
    /// the message — so no filter here reaches it.
    /// </remarks>
    public string? RecipientNormalizedAddress { get; }

    /// <summary>Gets the fragment the subject must contain, compared without regard to case, or <see langword="null" /> when any subject matches.</summary>
    public string? SubjectFragment { get; }

    /// <summary>Gets the inclusive start of the received range, or <see langword="null" /> when the range has no start.</summary>
    /// <remarks>An email whose received timestamp is unknown matches neither bound, so naming either one excludes undated mail.</remarks>
    public DateTimeOffset? ReceivedOnOrAfter { get; }

    /// <summary>Gets the exclusive end of the received range, or <see langword="null" /> when the range has no end.</summary>
    /// <remarks>The end is exclusive so consecutive ranges tile a timeline without overlapping on the instant they meet.</remarks>
    public DateTimeOffset? ReceivedBefore { get; }

    /// <summary>Gets the remote <c>\Seen</c> state an email must have, or <see langword="null" /> when either matches.</summary>
    /// <remarks>
    /// The filter compares the last observed flag snapshot. An email whose flags no run has read yet carries them
    /// unset, so it matches the unseen side of this filter; <see cref="EmailSummary.RemoteFlags" /> reports when the
    /// snapshot was taken, which is how a caller tells an unseen email from one nobody has looked at.
    /// </remarks>
    public bool? IsRemotelySeen { get; }

    /// <summary>Gets whether an email must carry attachments, or <see langword="null" /> when either matches.</summary>
    /// <remarks>
    /// Attachment presence is the classification rule the MIME extraction applies, not a disposition header: a message
    /// whose only non-body parts are inline resources or a cryptographic signature carries no attachments and does not
    /// match. This is what keeps a filter for mail with attachments from returning every signed message and every
    /// message with a logo in its signature block.
    /// </remarks>
    public bool? HasAttachments { get; }

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
        EmailTimelineDirection direction)
    {
        ArgumentNullException.ThrowIfNull(scope);

        if (!Enum.IsDefined(direction))
        {
            throw new ArgumentOutOfRangeException(
                nameof(direction),
                direction,
                "The timeline is read from one of its two ends, and no other value names a direction.");
        }

        if (receivedOnOrAfter is { } start && receivedBefore is { } end && start >= end)
        {
            throw MailboxQueryFilterInvalidException.EmptyRange("received date range");
        }

        return new EmailTimelineFilter(
            scope,
            NormalizedAddress(senderAddress, "sender address"),
            NormalizedAddress(recipientAddress, "recipient address"),
            BoundedSubjectFragment(subjectFragment),
            receivedOnOrAfter,
            receivedBefore,
            isRemotelySeen,
            hasAttachments,
            direction);
    }

    /// <summary>Puts an address filter into the one form anything compares addresses in.</summary>
    /// <remarks>
    /// The domain type does the normalizing, so a filter and a stored participant are compared in the same form by
    /// construction rather than by two call sites agreeing. It also decides what an address is, which is why an
    /// unusable one is refused here instead of becoming a value that matches no row.
    /// </remarks>
    private static string? NormalizedAddress(string? address, string filterName)
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            return null;
        }

        MailboxQueryFilterInvalidException.ThrowIfLengthExceeded(
            address.Trim().Length,
            MaximumAddressFilterLength,
            filterName);

        return EmailAddress.TryCreate(displayName: null, address, out var emailAddress)
            ? emailAddress.NormalizedAddress
            : throw MailboxQueryFilterInvalidException.NotAnAddress(filterName);
    }

    private static string? BoundedSubjectFragment(string? subjectFragment)
    {
        if (string.IsNullOrWhiteSpace(subjectFragment))
        {
            return null;
        }

        var trimmed = subjectFragment.Trim();

        MailboxQueryFilterInvalidException.ThrowIfLengthExceeded(
            trimmed.Length,
            MaximumSubjectFragmentLength,
            "subject fragment");

        if (trimmed.Any(char.IsControl))
        {
            throw MailboxQueryFilterInvalidException.ContainsControlCharacter("subject fragment");
        }

        return trimmed;
    }

    /// <summary>Hashes the canonical text of every field, so a cursor can tell which filter set it belongs to.</summary>
    /// <remarks>
    /// Every field is written, absent ones included, so a filter added to this type in future changes the fingerprint of
    /// requests that do not name it. Omitting absent fields would let a new filter's default produce the text an older
    /// build produced, and a cursor from that build would be accepted against a query it never described.
    /// </remarks>
    private string ComputeFingerprint()
    {
        var canonicalText = string.Join(
            CanonicalFieldSeparator,
            LengthPrefixed("f1"),
            CanonicalList(this.Scope.AccountIds.Select(static accountId => accountId.Value)),
            CanonicalList(this.Scope.FolderAliases.Select(static alias => alias.Value)),
            LengthPrefixed(this.SenderNormalizedAddress ?? CanonicalAbsentValue),
            LengthPrefixed(this.RecipientNormalizedAddress ?? CanonicalAbsentValue),
            // Upper-cased because the subject filter is case-insensitive: two requests that differ only in the case they
            // wrote a fragment in select the same emails, so they must not be two walks with incompatible cursors.
            LengthPrefixed(this.SubjectFragment?.ToUpperInvariant() ?? CanonicalAbsentValue),
            LengthPrefixed(CanonicalInstant(this.ReceivedOnOrAfter)),
            LengthPrefixed(CanonicalInstant(this.ReceivedBefore)),
            LengthPrefixed(CanonicalFlag(this.IsRemotelySeen)),
            LengthPrefixed(CanonicalFlag(this.HasAttachments)),
            LengthPrefixed(this.Direction.ToString()));

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonicalText));

        return Base64Url.EncodeToString(hash.AsSpan(0, FingerprintOctets));
    }

    private static string CanonicalList(IEnumerable<string> values) =>
        LengthPrefixed(string.Join(CanonicalFieldSeparator, values.Select(LengthPrefixed)));

    /// <summary>Writes one value with its own length in front of it, so nothing a caller supplies can imitate a separator.</summary>
    /// <remarks>
    /// Without it the scopes <c>["a,b", "c"]</c> and <c>["a", "b,c"]</c> produce one text and therefore one fingerprint,
    /// and a cursor issued for either is accepted against the other — which names a real row and an arbitrary window
    /// around it instead of raising the mismatch this fingerprint exists to raise. A length prefix makes the encoding
    /// injective whatever characters the values carry, which is what a separator alone cannot do while an account
    /// identifier is free text.
    /// </remarks>
    private static string LengthPrefixed(string value) =>
        string.Concat(value.Length.ToString(CultureInfo.InvariantCulture), ":", value);

    private static string CanonicalInstant(DateTimeOffset? instant) => instant is { } value
        ? value.UtcTicks.ToString(CultureInfo.InvariantCulture)
        : CanonicalAbsentValue;

    private static string CanonicalFlag(bool? flag) => flag is { } value
        ? value.ToString(CultureInfo.InvariantCulture)
        : CanonicalAbsentValue;
}
