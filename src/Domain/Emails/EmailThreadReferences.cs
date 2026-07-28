// Copyright © 2026 Krzysztof Kasprowicz

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

    /// <summary>Gets the referenced ancestors in the order the header listed them, without duplicates.</summary>
    public IReadOnlyList<string> References { get; }

    /// <summary>Gets the references of a message that carried no threading headers at all.</summary>
    public static EmailThreadReferences None { get; } = new(messageId: null, inReplyTo: null, references: []);

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

    private static IReadOnlyList<string> NormalizeReferences(IEnumerable<string> references) =>
    [
        .. references
            .Select(NormalizeIdentifier)
            .OfType<string>()
            .Distinct(StringComparer.Ordinal),
    ];

    /// <summary>Reduces one written identifier to the form everything compares on.</summary>
    /// <remarks>
    /// Case is preserved: RFC 5322 makes the identifier's domain part case-insensitive but its left half a token that a
    /// client is entitled to vary, and lower-casing the whole value would merge two identifiers a mail server minted as
    /// distinct.
    /// </remarks>
    private static string? NormalizeIdentifier(string? identifier)
    {
        if (identifier is null)
        {
            return null;
        }

        var withoutWhitespace = new string([.. identifier.Where(character => !char.IsWhiteSpace(character) && !char.IsControl(character))]);
        var withoutBrackets = withoutWhitespace.Trim('<', '>');

        return withoutBrackets.Length == 0 ? null : withoutBrackets;
    }
}
