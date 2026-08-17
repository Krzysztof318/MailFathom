// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Buffers;
using System.Collections.ObjectModel;
using MailFathom.Domain.Emails;

namespace MailFathom.Domain.Delivery;

/// <summary>Places one outgoing message in the conversation it answers, in the two headers every client threads by.</summary>
/// <remarks>
/// <para>
/// A reply that names the wrong ancestors is not a message with a cosmetic defect. Every mail client threads from
/// <c>In-Reply-To</c> and <c>References</c> and from nothing else, so a guessed value puts the reply in a conversation
/// of its own in every recipient's mailbox, and no later correction reaches the copies already delivered. This
/// deployment pays that cost twice: its own threads are assembled from the same three identifiers, so a reply whose
/// headers are wrong comes back from <c>Sent</c> as a conversation of one here as well.
/// </para>
/// <para>
/// The placement is derived from what the original message carried rather than supplied by whoever authored the
/// answer. That is what makes it correct by construction: an authoring boundary names the stored email it is answering,
/// and the identifiers come out of that message's own headers.
/// </para>
/// </remarks>
public sealed record OutgoingThreadPlacement
{
    /// <summary>The greatest number of ancestors a composed message writes into <c>References</c>.</summary>
    /// <remarks>
    /// <para>
    /// A conversation grows without bound and the header grows with it, so a long exchange would eventually carry more
    /// path than message. The bound is deliberately far below the
    /// <see cref="EmailThreadReferences.MaximumReferences" /> a parse keeps, because the two answer different
    /// questions: a parse publishes what somebody else wrote, and this decides what this system writes and will be
    /// judged on by every receiving client.
    /// </para>
    /// <para>
    /// What a bounded path loses is its middle. The root identifier names the conversation and the recent end is what a
    /// client walks to attach the reply, so both survive and the ancestors between them are given up.
    /// </para>
    /// </remarks>
    public const int MaximumReferences = 32;

    /// <summary>The placement of a message answering nothing, declared before the value that publishes it.</summary>
    private static readonly ReadOnlyCollection<string> NoReferences = new List<string>().AsReadOnly();

    private OutgoingThreadPlacement(string? inReplyTo, IReadOnlyList<string> references)
    {
        this.InReplyTo = inReplyTo;
        this.References = references;
    }

    /// <summary>Gets the placement of a message that answers nothing, which writes neither header.</summary>
    public static OutgoingThreadPlacement None { get; } = new(inReplyTo: null, NoReferences);

    /// <summary>Gets the identifier of the message this one answers, without its angle brackets, or <see langword="null" /> when it answers none.</summary>
    public string? InReplyTo { get; }

    /// <summary>Gets the path back to the root of the conversation, in header order, with the answered message last.</summary>
    public IReadOnlyList<string> References { get; }

    /// <summary>Gets whether this placement writes anything into a composed message.</summary>
    public bool IsThreaded => this.InReplyTo is not null || this.References.Count > 0;

    /// <summary>Places a message as the answer to one this deployment already holds.</summary>
    /// <param name="original">The threading headers the answered message carried.</param>
    /// <returns>The placement a composed answer writes, which is <see cref="None" /> when nothing usable can be written.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="original" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// <para>
    /// <c>In-Reply-To</c> is the answered message's own identity and <c>References</c> is its path with that identity
    /// appended, which is what RFC 5322 § 3.6.4 asks for and what every client implements. The answered identifier is
    /// last because that is where a client looks for the immediate parent.
    /// </para>
    /// <para>
    /// A message that carried no <c>Message-ID</c> can be answered but cannot be pointed at, so the answer inherits the
    /// path it carried and names no parent. That is the honest reading rather than a degraded one: naming an ancestor
    /// as the parent would attach the reply to the wrong message in the same conversation.
    /// </para>
    /// <para>
    /// An identifier that cannot be written into a header is dropped rather than repaired or written through. What
    /// arrives here has already been normalized as a parse publishes it, so this drops what remains — an angle bracket,
    /// a comma, or whitespace inside the value — which no mail server mints and which would end the header early or
    /// split one identifier into two.
    /// </para>
    /// </remarks>
    public static OutgoingThreadPlacement Answering(EmailThreadReferences original)
    {
        ArgumentNullException.ThrowIfNull(original);

        var answeredIdentifier = WritableIdentifier(original.MessageId);

        var path = original.References
            .Select(WritableIdentifier)
            .OfType<string>()
            .Where(identifier => !string.Equals(identifier, answeredIdentifier, StringComparison.Ordinal))
            .ToList();

        if (answeredIdentifier is not null)
        {
            path.Add(answeredIdentifier);
        }

        return path.Count == 0
            ? None
            : new OutgoingThreadPlacement(answeredIdentifier, BoundedPath(path));
    }

    /// <summary>Keeps the root and the most recent ancestors, which is what a client reads either end of the path for.</summary>
    private static ReadOnlyCollection<string> BoundedPath(List<string> path) =>
        path.Count <= MaximumReferences
            ? path.AsReadOnly()
            : new List<string>([path[0], .. path.Skip(path.Count - (MaximumReferences - 1))]).AsReadOnly();

    /// <summary>The characters that delimit or structure a header, and which therefore end an identifier written into one.</summary>
    /// <remarks>
    /// The angle brackets and the comma separate one identifier from the next, and the rest open a comment, a quoted
    /// string, or a group that a composed header would have to balance. No mail server mints an identifier carrying
    /// any of them, so refusing to write one costs a message that could not have threaded anywhere.
    /// </remarks>
    private static readonly SearchValues<char> HeaderStructureCharacters = SearchValues.Create("<>,\"();:\\");

    /// <summary>Reports one identifier in the form a composed header carries, or nothing when it cannot carry it.</summary>
    /// <remarks>
    /// What arrives has already been normalized by the parse that published it, so this is the narrower question of
    /// whether the value can be written back out: an identifier is one addr-spec, so it carries exactly one at-sign
    /// with something on each side of it and nothing that would end the header early. Everything else is dropped in
    /// silence, because a message this system could not point at is not a failure of the answer being composed.
    /// </remarks>
    private static string? WritableIdentifier(string? identifier)
    {
        if (identifier is null
            || identifier.Length == 0
            || identifier.Length > EmailThreadReferences.MaximumIdentifierLength)
        {
            return null;
        }

        if (identifier.AsSpan().ContainsAny(HeaderStructureCharacters)
            || identifier.Any(static character => char.IsWhiteSpace(character) || char.IsControl(character)))
        {
            return null;
        }

        var domainSeparatorIndex = identifier.IndexOf('@');

        return domainSeparatorIndex > 0
            && domainSeparatorIndex == identifier.LastIndexOf('@')
            && domainSeparatorIndex < identifier.Length - 1
                ? identifier
                : null;
    }
}
