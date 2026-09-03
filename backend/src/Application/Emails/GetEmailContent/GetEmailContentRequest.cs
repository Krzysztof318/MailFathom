// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Emails;

namespace MailFathom.Application.Emails.GetEmailContent;

/// <summary>What a caller asks for when reading emails from the local mailbox copy.</summary>
/// <remarks>
/// <para>
/// Emails are named by their domain identity rather than as text, so an adapter converts a caller's strings once, at its
/// own boundary, and a malformed identifier is refused before it reaches a use case.
/// </para>
/// <para>
/// The representation flags govern the whole call rather than one email each. A caller asking for markup or for
/// attachment links wants it for what it is about to read, and a flag per identifier would make the argument list grow
/// with the batch while answering a question no caller asks per email.
/// </para>
/// <para>
/// <see cref="RetainRemoteImageReferences" /> is the exception, and it is enforced rather than described: it carries
/// one reader's consent about one message, so a request that asks for it names exactly one email. Everything else here
/// is a preference about a call and that one is an act about a message, which is the whole difference.
/// </para>
/// </remarks>
public sealed record GetEmailContentRequest
{
    /// <summary>The greatest number of emails one read may name.</summary>
    /// <remarks>
    /// <para>
    /// A listing returns up to 100 summaries and a search up to 50 ranked matches, so a caller can always name more
    /// emails than one read serves. The bound is small deliberately: it is the count half of the control on how much
    /// mail one protocol call draws out of a mailbox, and a handful of messages is what a caller reads after a search
    /// rather than what it archives.
    /// </para>
    /// <para>
    /// It lives on the request because that is what it constrains, so the boundary that checks a caller's list before
    /// parsing it and the use case that enforces the invariant afterwards read one number.
    /// </para>
    /// </remarks>
    public const int MaximumEmails = 10;

    private GetEmailContentRequest(
        IReadOnlyList<StoredEmailId> storedEmailIds,
        EmailThreadId? threadId,
        bool includeSanitizedHtml,
        bool includeAttachmentDownloadLinks)
    {
        this.StoredEmailIds = storedEmailIds;
        this.ThreadId = threadId;
        this.IncludeSanitizedHtml = includeSanitizedHtml;
        this.IncludeAttachmentDownloadLinks = includeAttachmentDownloadLinks;
    }

    /// <summary>Gets the emails to read, in the order the caller named them, which is empty for a conversation read.</summary>
    /// <remarks>The order is the contract: results are returned in it, and the read's character budget is spent in it.</remarks>
    public IReadOnlyList<StoredEmailId> StoredEmailIds { get; }

    /// <summary>Gets the conversation to read, or <see langword="null" /> when the caller named the emails itself.</summary>
    /// <remarks>
    /// The alternative to naming emails rather than a filter over them. What the conversation resolves to is the same
    /// list of identities the other form carries, under the same bound and in the conversation's own order, so
    /// everything downstream of the resolution reads one shape.
    /// </remarks>
    public EmailThreadId? ThreadId { get; }

    /// <summary>Gets whether to also return the sanitized HTML representation of each body.</summary>
    /// <remarks>
    /// Opt-in because it costs a sanitization pass over untrusted markup and because plain text is what most callers
    /// want: a model reading mail is better served by the words than by the layout around them.
    /// </remarks>
    public bool IncludeSanitizedHtml { get; }

    /// <summary>Gets whether to also mint a link for fetching each attachment, rather than only describe it.</summary>
    /// <remarks>
    /// <para>
    /// What every read answers is what the message carries: how many attachments, what each is called, what it declares
    /// itself to be, and how large it is. A caller deciding whether a file is worth fetching needs all of that, and a
    /// read that had to ask twice to learn a name would answer the first call with a number and nothing to act on.
    /// </para>
    /// <para>
    /// The links are opt-in because each one is a short-lived bearer capability over the message's most sensitive part.
    /// They cost the response almost nothing, so what the flag buys is not size: it is that a read of bodies mints no
    /// capability nobody intended to hand out.
    /// </para>
    /// </remarks>
    public bool IncludeAttachmentDownloadLinks { get; }

    /// <summary>Gets whether to also reduce each body to the document tree a reading pane draws.</summary>
    /// <remarks>
    /// An init property rather than a factory argument, because it is asked for by one caller — the client endpoint a
    /// person's reading pane reads — and every other entrypoint here would have to name it only to decline it. A model
    /// reading mail wants the words, so the tool surface leaves it alone and pays for neither the walk nor the pictures
    /// it inlines.
    /// </remarks>
    public bool IncludeMailDocument { get; init; }

    /// <summary>Gets whether to also produce the message's own markup with everything that runs or reports removed.</summary>
    /// <remarks>
    /// An init property for the same reason the document is: one caller asks for it — the surface that opens the
    /// sender's own layout in a frame beside the reading pane — and it is the most expensive representation a read can
    /// produce, since it inlines the message's own pictures as well as carrying its markup. A model reading mail wants
    /// neither, so the tool surface leaves it alone and pays for neither the parse nor the decode.
    /// </remarks>
    public bool IncludeSelfContainedHtml { get; init; }

    /// <summary>Gets whether the reduced document may carry this message's remote picture references.</summary>
    /// <remarks>
    /// <para>
    /// It is a per-message act by the reader: asking for it loads what the sender's message reaches for, which tells
    /// whoever wrote it that it was opened and from where. Nothing remembers the answer, so it is asked again the next
    /// time the message is opened — and the request refuses to carry the consent across a list, because one reader
    /// deciding about one message is exactly what it means and a read naming ten would apply it to nine they never saw.
    /// </para>
    /// <para>
    /// What it widens differs by representation, because what each one can carry differs. The reduced document has a
    /// picture's source and nothing else, so that is the whole of it there. The self-contained markup carries a
    /// stylesheet, a background, a web font, and a set of candidate sources besides, and every one of them is an
    /// address the same fetch would reveal the same thing through — so the consent restores all of them or the surface
    /// would draw a layout missing exactly the parts it was opened to see. In neither case does it restore anything
    /// that runs.
    /// </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">Thrown when it is asked for by a request naming other than one email.</exception>
    public bool RetainRemoteImageReferences
    {
        get;

        init
        {
            if (value && this.StoredEmailIds.Count != 1)
            {
                throw new InvalidOperationException(
                    "Remote picture references carry one reader's consent about one message, so a request asking for "
                        + "them names exactly one email.");
            }

            field = value;
        }
    }

    /// <summary>Creates a request from the emails a caller named.</summary>
    /// <param name="storedEmailIds">The emails to read, in the order they were named.</param>
    /// <param name="includeSanitizedHtml">Whether to also produce the sanitized HTML representation of each body.</param>
    /// <param name="includeAttachmentDownloadLinks">Whether to mint a link for each attachment rather than only describe it.</param>
    /// <returns>The validated request.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="storedEmailIds" /> is <see langword="null" />.</exception>
    /// <exception cref="EmailContentReadCountOutOfRangeException">Thrown when no email is named, or more than <see cref="MaximumEmails" /> are.</exception>
    /// <exception cref="EmailContentReadDuplicateEmailException">Thrown when the same email is named more than once.</exception>
    /// <remarks>
    /// Both refusals are the request's own invariant rather than a boundary's courtesy check, so an entrypoint added
    /// later cannot reach the use case with a list nobody counted. A caller's text is checked for length before it is
    /// parsed as well, at the boundary that holds the text.
    /// </remarks>
    public static GetEmailContentRequest Create(
        IReadOnlyList<StoredEmailId> storedEmailIds,
        bool includeSanitizedHtml = false,
        bool includeAttachmentDownloadLinks = false)
    {
        ArgumentNullException.ThrowIfNull(storedEmailIds);

        if (storedEmailIds.Count is 0 || storedEmailIds.Count > MaximumEmails)
        {
            throw new EmailContentReadCountOutOfRangeException(MaximumEmails);
        }

        if (storedEmailIds.Distinct().Count() != storedEmailIds.Count)
        {
            throw new EmailContentReadDuplicateEmailException();
        }

        return new GetEmailContentRequest(
            [.. storedEmailIds],
            threadId: null,
            includeSanitizedHtml,
            includeAttachmentDownloadLinks);
    }

    /// <summary>Creates a request for the messages of one conversation.</summary>
    /// <param name="threadId">The conversation whose messages to read.</param>
    /// <param name="includeSanitizedHtml">Whether to also produce the sanitized HTML representation of each body.</param>
    /// <param name="includeAttachmentDownloadLinks">Whether to mint a link for each attachment rather than only describe it.</param>
    /// <returns>The validated request.</returns>
    /// <remarks>
    /// Nothing is counted here, because nothing has been resolved yet: how many messages the conversation holds is what
    /// reading it answers. The bound is the same <see cref="MaximumEmails" /> a caller's own list is held to, applied to
    /// the conversation's order, and the identities it left out are named in the result so a second call asks for them
    /// directly.
    /// </remarks>
    public static GetEmailContentRequest CreateForThread(
        EmailThreadId threadId,
        bool includeSanitizedHtml = false,
        bool includeAttachmentDownloadLinks = false) =>
        new([], threadId, includeSanitizedHtml, includeAttachmentDownloadLinks);

    /// <summary>Creates a request from the two ways a caller may select what to read, refusing anything but one of them.</summary>
    /// <param name="namedEmails">Resolves the emails the caller named, or <see langword="null" /> when it named none.</param>
    /// <param name="namedThread">Resolves the conversation the caller named, or <see langword="null" /> when it named none.</param>
    /// <param name="includeSanitizedHtml">Whether to also produce the sanitized HTML representation of each body.</param>
    /// <param name="includeAttachmentDownloadLinks">Whether to mint a link for each attachment rather than only describe it.</param>
    /// <returns>The validated request.</returns>
    /// <exception cref="EmailContentReadSelectionInvalidException">Thrown when both selections are given, or neither is.</exception>
    /// <exception cref="EmailContentReadCountOutOfRangeException">Thrown when a named list holds no email, or more than <see cref="MaximumEmails" />.</exception>
    /// <exception cref="EmailContentReadDuplicateEmailException">Thrown when a named list holds the same email more than once.</exception>
    /// <remarks>
    /// <para>
    /// A call carrying both is refused rather than resolved by precedence, because either reading of it returns mail the
    /// caller did not ask for: honouring the list ignores a conversation somebody wanted, and honouring the conversation
    /// returns messages nobody named. Which one was meant is theirs to say.
    /// </para>
    /// <para>
    /// Both arrive unresolved, which is what puts that refusal in front of everything either of them is checked for.
    /// Whether the caller named one is knowable from the selection alone, while what they named costs a boundary a parse
    /// over text it did not choose the length of — so a call this method refuses pays for neither parse, and a caller
    /// sending both is told which of the two arguments to drop rather than that the one it never meant to use is too
    /// short or misspelled. Resolving either eagerly would put its own failure in front of the refusal, which is the
    /// same defect on whichever side it is left.
    /// </para>
    /// </remarks>
    public static GetEmailContentRequest CreateForSelection(
        Func<IReadOnlyList<StoredEmailId>>? namedEmails,
        Func<EmailThreadId>? namedThread,
        bool includeSanitizedHtml = false,
        bool includeAttachmentDownloadLinks = false)
    {
        if ((namedEmails is null) == (namedThread is null))
        {
            throw new EmailContentReadSelectionInvalidException();
        }

        return namedThread is not null
            ? CreateForThread(namedThread(), includeSanitizedHtml, includeAttachmentDownloadLinks)
            : Create(namedEmails!(), includeSanitizedHtml, includeAttachmentDownloadLinks);
    }
}
