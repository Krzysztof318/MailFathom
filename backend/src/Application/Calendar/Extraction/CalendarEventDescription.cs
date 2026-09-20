// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;

namespace MailFathom.Application.Calendar.Extraction;

/// <summary>One sentence somebody typed to describe an event, and the instant they typed it at.</summary>
/// <remarks>
/// <para>
/// The instant is theirs rather than the deployment's, and it carries the offset as well as the day precisely because
/// both are needed: <em>Tuesday at three</em> names a day only against the day somebody is standing on, and an hour
/// only against the zone they are standing in. A deployment resolving either from its own clock would write an event
/// into a day and an hour nobody asked for, which is the one mistake a draft of this kind cannot recover from — the
/// person is shown a plausible time and saves it.
/// </para>
/// <para>
/// The text is bounded and otherwise unexamined. It is sent to a provider and never stored, so the rules a stored
/// value carries belong to the title that comes back rather than to the sentence that went out.
/// </para>
/// </remarks>
public sealed record CalendarEventDescription
{
    /// <summary>The greatest length a description may carry.</summary>
    /// <remarks>
    /// Generous against the sentence this field asks for and small enough that the field cannot become a way to put a
    /// message through the extraction: what reads mail is the pass, under the account's own posture, and a person
    /// pasting a thread here would send text nothing scanned it as.
    /// </remarks>
    public const int MaximumTextLength = 500;

    private CalendarEventDescription(string text, DateTimeOffset writtenAt)
    {
        this.Text = text;
        this.WrittenAt = writtenAt;
    }

    /// <summary>Gets the sentence as it was typed, trimmed.</summary>
    public string Text { get; }

    /// <summary>Gets the instant the person was standing on, which every relative day and hour is resolved against.</summary>
    public DateTimeOffset WrittenAt { get; }

    /// <summary>Reads a description, refusing one that is blank or longer than a sentence.</summary>
    /// <param name="text">The sentence as supplied.</param>
    /// <param name="writtenAt">The instant the person typed it at, with their own offset.</param>
    /// <param name="description">The description, when it is one.</param>
    /// <returns><see langword="true" /> when <paramref name="text" /> is a description.</returns>
    public static bool TryCreate(
        string? text,
        DateTimeOffset writtenAt,
        [NotNullWhen(true)] out CalendarEventDescription? description)
    {
        var trimmed = text?.Trim();

        if (string.IsNullOrEmpty(trimmed) || trimmed.Length > MaximumTextLength)
        {
            description = null;

            return false;
        }

        description = new CalendarEventDescription(trimmed, writtenAt);

        return true;
    }
}
