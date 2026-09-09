// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Emails;

namespace MailFathom.Application.Emails.ThreadStates;

/// <summary>One thing a conversation settled, left open, undertook, or changed between two versions of a document.</summary>
/// <remarks>
/// <para>
/// Never the sentence alone. A statement about somebody's correspondence that cannot be traced to the message that made
/// it is the thing this record exists to rule out, so <see cref="Sources" /> is a constructor argument that may not be
/// empty rather than a property a caller can leave unset.
/// </para>
/// <para>
/// The sources name messages of the conversation rather than passages inside them, which is what the screen beside a
/// thread draws and what a reader follows: the correspondence is already in front of them, so being taken to the
/// message that made the statement is the whole of the check. They are named in the same terms the run surface's own
/// citations name a message, so one resolver follows both.
/// </para>
/// <para>
/// The text, the owner, and the date are derived from mail and are personal data of the same standing as the message
/// they came from: never logged, never attached to a span, and scanned by whatever egress guard is switched on before
/// any of them leaves this deployment.
/// </para>
/// </remarks>
public sealed record ThreadStateEntry
{
    /// <summary>The greatest length a statement carries before it is shortened to it.</summary>
    /// <remarks>
    /// One card of the row a conversation is read under, which is what a statement exists to be. It shortens rather
    /// than refusing, because the value is what a producer wrote and discarding a whole derivation over a long sentence
    /// would leave the conversation with nothing to show; the bound is applied at a text-element boundary so a
    /// shortened value never ends inside a surrogate pair.
    /// </remarks>
    public const int MaximumTextLength = 240;

    /// <summary>The greatest length the name of whoever owes a commitment carries.</summary>
    /// <remarks>Shorter than the statement because it is a person rather than a sentence, and long enough for a name written with an address beside it.</remarks>
    public const int MaximumOwedByLength = 120;

    /// <summary>The greatest number of messages one statement may cite.</summary>
    /// <remarks>
    /// A statement is one sentence about a conversation, so more than a handful of sources is a producer citing the
    /// thread rather than the place its claim came from. The leading citations are kept, because a producer names what
    /// it relied on most first.
    /// </remarks>
    public const int MaximumSourceCount = 4;

    private ThreadStateEntry(
        ThreadStateAspect aspect,
        string text,
        string? owedBy,
        DateTimeOffset? dueAt,
        IReadOnlyList<StoredEmailId> sources)
    {
        this.Aspect = aspect;
        this.Text = text;
        this.OwedBy = owedBy;
        this.DueAt = dueAt;
        this.Sources = sources;
    }

    /// <summary>Gets which of the four things this statement says.</summary>
    public ThreadStateAspect Aspect { get; }

    /// <summary>Gets what the statement says, which is the sentence a card draws.</summary>
    public string Text { get; }

    /// <summary>Gets who owes the commitment, or <see langword="null" /> where nothing names one.</summary>
    /// <remarks>
    /// Optional even on a commitment, because a commitment is often made without naming who keeps it — "we will send
    /// the revised figures" — and inventing a name for it would be the assistant asserting something the mail did not.
    /// Only a <see cref="ThreadStateAspect.Commitment" /> may carry one.
    /// </remarks>
    public string? OwedBy { get; }

    /// <summary>Gets when the commitment falls due, or <see langword="null" /> where nothing says.</summary>
    /// <remarks>Only a <see cref="ThreadStateAspect.Commitment" /> may carry one, for the reason <see cref="OwedBy" /> is bounded that way.</remarks>
    public DateTimeOffset? DueAt { get; }

    /// <summary>Gets the messages of the conversation the statement rests on, in the order the producer named them.</summary>
    public IReadOnlyList<StoredEmailId> Sources { get; }

    /// <summary>Records one statement about where a conversation stands.</summary>
    /// <param name="aspect">Which of the four things the statement says.</param>
    /// <param name="text">What the statement says.</param>
    /// <param name="sources">The messages it rests on, which is never empty.</param>
    /// <param name="owedBy">Who owes the commitment, for a commitment that names somebody.</param>
    /// <param name="dueAt">When the commitment falls due, for a commitment that names a date.</param>
    /// <returns>The statement, with the text and the owner shortened to their bounds and the sources to theirs.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="sources" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when the text is blank, when the sources are empty, or when an aspect other than a commitment names an owner or a date.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="aspect" /> is not a defined member.</exception>
    public static ThreadStateEntry Create(
        ThreadStateAspect aspect,
        string text,
        IReadOnlyList<StoredEmailId> sources,
        string? owedBy = null,
        DateTimeOffset? dueAt = null)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        if (!Enum.IsDefined(aspect))
        {
            throw new ArgumentOutOfRangeException(
                nameof(aspect),
                aspect,
                "A statement carries one of the aspects this system derives.");
        }

        if (sources.Count == 0)
        {
            throw new ArgumentException(
                "A statement cites at least one message of the conversation it is about.",
                nameof(sources));
        }

        if (aspect is not ThreadStateAspect.Commitment)
        {
            if (!string.IsNullOrWhiteSpace(owedBy))
            {
                throw new ArgumentException("Only a commitment names who owes it.", nameof(owedBy));
            }

            if (dueAt is not null)
            {
                throw new ArgumentException("Only a commitment carries a date.", nameof(dueAt));
            }
        }

        return new ThreadStateEntry(
            aspect,
            Shortened(text, MaximumTextLength),
            string.IsNullOrWhiteSpace(owedBy) ? null : Shortened(owedBy, MaximumOwedByLength),
            dueAt,
            [.. sources.Take(MaximumSourceCount)]);
    }

    /// <summary>Puts a producer's sentence into the one form a record keeps it in.</summary>
    /// <remarks>
    /// Whitespace collapses first, so a value a producer wrapped across lines is bounded by what it says rather than by
    /// how it was laid out, and the cut then falls on a text-element boundary rather than on a UTF-16 index.
    /// </remarks>
    private static string Shortened(string value, int maximumLength)
    {
        var collapsed = string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

        return MailTextBounds.TruncateAtTextElementBoundary(collapsed, maximumLength);
    }
}
