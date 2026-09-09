// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Emails;

namespace MailFathom.Application.Emails.ReplyDrafts;

/// <summary>One thing a draft asserts, with the messages of the conversation that back it.</summary>
/// <remarks>
/// <para>
/// A reply that states a price, a date, or a commitment is stating something out of the correspondence, and a claim is
/// that statement held against the messages it came from. It is what a person checks before they put their name to the
/// message, which is the one moment checking is worth anything.
/// </para>
/// <para>
/// <b>A claim with no source is kept and marked rather than dropped.</b> That is the opposite of what a conversation's
/// derived state does with an unsourced statement, and the difference is the point: a state is a record somebody reads
/// instead of the correspondence, so an unsupported sentence in it is indistinguishable from a supported one and must
/// not be stored — while a draft is text somebody is about to send, so the sentence the mail does not back is exactly
/// the sentence they have to see. Dropping it here would leave it in the body with nothing beside it saying so.
/// </para>
/// <para>
/// The sources name messages in the same terms every other citation in this system names them, so a reader follows one
/// through the resolution a Discover answer's sources are followed through.
/// </para>
/// </remarks>
public sealed record ReplyDraftClaim
{
    /// <summary>The greatest length a claim carries before it is shortened to it.</summary>
    /// <remarks>One sentence of the draft, quoted back beside what supports it. Longer than that is a producer quoting a paragraph rather than naming what it asserted.</remarks>
    public const int MaximumTextLength = 240;

    /// <summary>The greatest number of messages one claim may cite.</summary>
    /// <remarks>The bound a derived statement cites under, for the same reason: past a handful a producer is citing the conversation rather than the place its claim came from.</remarks>
    public const int MaximumSourceCount = 4;

    private ReplyDraftClaim(string text, IReadOnlyList<StoredEmailId> sources)
    {
        this.Text = text;
        this.Sources = sources;
    }

    /// <summary>Gets what the draft asserts, as one sentence.</summary>
    public string Text { get; }

    /// <summary>Gets the messages of the conversation the claim rests on, in the order the producer named them, which is empty for a claim nothing supports.</summary>
    public IReadOnlyList<StoredEmailId> Sources { get; }

    /// <summary>Gets whether the correspondence backs the claim at all.</summary>
    /// <remarks>Derived from the sources rather than stated beside them, so the mark a client draws and the citations a reader follows can never disagree about one claim.</remarks>
    public bool IsSupported => this.Sources.Count > 0;

    /// <summary>Records one thing a draft asserts.</summary>
    /// <param name="text">What the draft asserts.</param>
    /// <param name="sources">The messages backing it, which is empty where the conversation backs none of it.</param>
    /// <returns>The claim, with the text shortened to its bound and the sources to theirs.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="text" /> or <paramref name="sources" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when the text is blank.</exception>
    public static ReplyDraftClaim Create(string text, IReadOnlyList<StoredEmailId> sources)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        ArgumentNullException.ThrowIfNull(sources);

        var collapsed = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

        return new ReplyDraftClaim(
            MailTextBounds.TruncateAtTextElementBoundary(collapsed, MaximumTextLength),
            [.. sources.Distinct().Take(MaximumSourceCount)]);
    }
}
