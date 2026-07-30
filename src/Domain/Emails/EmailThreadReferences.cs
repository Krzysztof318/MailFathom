// Copyright © 2026 Krzysztof Kasprowicz

using System.Collections.ObjectModel;

namespace MailMcp.Domain.Emails;

/// <summary>Holds the message identifiers that place one email in a conversation.</summary>
/// <remarks>
/// The three headers are kept apart because they answer different questions: <see cref="MessageId" /> is this message's
/// own identity, <see cref="InReplyTo" /> is the single message it answers, and <see cref="References" /> is the path
/// back to the root. Threading needs all three, and a client that writes only some of them is ordinary.
/// </remarks>
public sealed record EmailThreadReferences
{
    private EmailThreadReferences(string? messageId, string? inReplyTo, IReadOnlyList<string> references)
    {
        this.MessageId = messageId;
        this.InReplyTo = inReplyTo;
        this.References = references;
    }

    /// <summary>Gets the message's own identifier without its angle brackets, or <see langword="null" /> when it carried none.</summary>
    public string? MessageId { get; }

    /// <summary>Gets the identifier of the message this one answers, or <see langword="null" /> when it answers none.</summary>
    public string? InReplyTo { get; }

    /// <summary>The greatest number of ancestors a parse keeps from one <c>References</c> header.</summary>
    /// <remarks>
    /// The bound exists for the reason <see cref="EmailParticipant.MaximumPerRole" /> does: nothing between a sender and
    /// this system limits how long the header may be, and every reader of a message publishes what it found — a content
    /// read returns the path back to the root to whoever asked for the message. What a longer path loses is its middle:
    /// the root identifier names the conversation and the recent end is what a reader walks, so both are kept.
    /// <para>
    /// The persisted column carries a bound of its own, deliberately narrower. This one bounds what a parse publishes
    /// and that one bounds what a column stores, and the two would answer differently if a later schema change moved
    /// one of them.
    /// </para>
    /// </remarks>
    public const int MaximumReferences = 256;

    /// <summary>Gets the referenced ancestors in the order the header listed them, without duplicates.</summary>
    public IReadOnlyList<string> References { get; }

    /// <summary>Gets the references of a message that carried no threading headers at all.</summary>
    public static EmailThreadReferences None { get; } = new(
        messageId: null,
        inReplyTo: null,
        references: new List<string>().AsReadOnly());

    /// <summary>Builds the reference set from what the message's threading headers carried.</summary>
    /// <param name="messageId">The <c>Message-ID</c> header value.</param>
    /// <param name="inReplyTo">The <c>In-Reply-To</c> header value.</param>
    /// <param name="references">The <c>References</c> header values, in header order.</param>
    /// <returns>The normalized reference set, which is <see cref="None" /> when nothing usable was written.</returns>
    /// <remarks>
    /// Every identifier is normalized the same way, so an ancestor written with angle brackets in one message and
    /// without them in another is one identifier rather than two. Duplicates within <c>References</c> collapse while the
    /// header's order is kept, because that order is the path from the root and is what a future thread view walks.
    /// </remarks>
    public static EmailThreadReferences Create(string? messageId, string? inReplyTo, IEnumerable<string>? references)
    {
        var normalizedReferences = references is null
            ? []
            : NormalizeReferences(references);

        var normalizedMessageId = NormalizeIdentifier(messageId);
        var normalizedInReplyTo = NormalizeIdentifier(inReplyTo);

        return normalizedMessageId is null && normalizedInReplyTo is null && normalizedReferences.Count == 0
            ? None
            : new EmailThreadReferences(normalizedMessageId, normalizedInReplyTo, normalizedReferences);
    }

    /// <summary>Normalizes the referenced ancestors, keeping header order, dropping repeats, and bounding the path.</summary>
    /// <remarks>
    /// The result is wrapped rather than returned as the array a collection expression builds, because an
    /// <see cref="IReadOnlyList{T}" /> backed directly by an array can be cast back to it and written through.
    /// </remarks>
    private static ReadOnlyCollection<string> NormalizeReferences(IEnumerable<string> references)
    {
        List<string> normalizedReferences =
        [
            .. references
                .Select(NormalizeIdentifier)
                .OfType<string>()
                .Distinct(StringComparer.Ordinal),
        ];

        return normalizedReferences.Count <= MaximumReferences
            ? normalizedReferences.AsReadOnly()
            : WithinBound(normalizedReferences);
    }

    /// <summary>Reduces a path longer than the bound to its root and its most recent ancestors.</summary>
    /// <remarks>
    /// The two ranges cannot overlap, because the list is already free of duplicates and longer than the bound, so the
    /// recent end begins past the root.
    /// </remarks>
    private static ReadOnlyCollection<string> WithinBound(List<string> normalizedReferences) =>
        new List<string>([normalizedReferences[0], .. normalizedReferences.TakeLast(MaximumReferences - 1)]).AsReadOnly();

    /// <summary>Reduces one written identifier to the form everything compares on.</summary>
    /// <remarks>
    /// <para>
    /// Only what surrounds the identifier is removed — the angle brackets and the whitespace around them. What the
    /// identifier itself holds is kept: a mail parser resolves header folding long before a value reaches this type, so
    /// interior whitespace is content rather than leftover folding, and <c>"a b"@example.test</c> is an identifier a
    /// message may legitimately carry. Deleting its space would record an identifier nobody minted and merge this
    /// message into a conversation it does not belong to.
    /// </para>
    /// <para>
    /// Case is preserved on both halves, deliberately and including the domain: a message identifier is an opaque
    /// token that the mail ecosystem compares octet for octet, and a client places an ancestor in <c>References</c> by
    /// copying the identifier it received rather than by rewriting it. Case-folding the domain would therefore not
    /// repair a difference that arises in practice, while it would merge two identifiers that every other mail client
    /// keeps apart — and merging is the direction that joins unrelated conversations.
    /// </para>
    /// <para>
    /// An identifier still carrying a control character after that is refused rather than repaired. No parser produces
    /// one, so the value came from a header nothing could read, and a repaired identifier would be a thread key nobody
    /// wrote.
    /// </para>
    /// </remarks>
    private static string? NormalizeIdentifier(string? identifier)
    {
        if (identifier is null)
        {
            return null;
        }

        var withoutSurroundingTransport = identifier.Trim().Trim('<', '>').Trim();

        return withoutSurroundingTransport.Length == 0 || withoutSurroundingTransport.Any(char.IsControl)
            ? null
            : withoutSurroundingTransport;
    }
}
