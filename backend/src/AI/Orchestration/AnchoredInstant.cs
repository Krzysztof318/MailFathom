// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;

namespace MailFathom.AI.Orchestration;

/// <summary>Reads an instant a model wrote back against the anchor its turn stated.</summary>
/// <remarks>
/// <para>
/// The other half of <see cref="AgentTimeAnchor" />, and it exists because that half is only correct with this one.
/// The anchor states a wall clock without an offset, so what a model writes back is a wall clock too — and a wall
/// clock read by anything that supplies a zone of its own is a different instant. <c>System.Text.Json</c> supplies
/// one: an offset-less value bound into a <see cref="DateTimeOffset" /> is not the asking person's, whatever it is,
/// because nothing in the binder has ever heard of them. So every path that lets a model write an instant reads it
/// here instead of trusting what a binder produced.
/// </para>
/// <para>
/// What that buys is the whole point of stating an anchor at all: <em>this week</em> resolved by the model against the
/// wall clock it was handed comes back as the same wall clock and is turned into the instant that person's day
/// actually began at. Read any other way it is the same numbers against somebody else's day, which is the drift the
/// anchor exists to remove and is invisible in a diff, in a log, and in the answer itself.
/// </para>
/// <para>
/// The form is the one the anchor is written in, with seconds admitted because writing <c>:00</c> onto an instant says
/// nothing about whether the model understood the date. Anything else — a <c>Z</c>, an offset, a day alone, a month
/// name — is a reading that failed, and it answers nothing rather than a guess: a model that wrote a zone it was never
/// given has invented one, and an instant invented with authority is worse than a filter the caller can report back.
/// </para>
/// </remarks>
internal static class AnchoredInstant
{
    /// <summary>The forms a written instant is accepted in, all of them local and none carrying a zone.</summary>
    private static readonly string[] WrittenFormats = ["yyyy-MM-dd'T'HH:mm", "yyyy-MM-dd'T'HH:mm:ss"];

    /// <summary>How the form is named where a model is told which one to write.</summary>
    /// <remarks>
    /// Stated once and quoted by every description and instruction that asks for an instant, so the form a model is
    /// asked for and the form <see cref="Read" /> accepts cannot come to differ.
    /// </remarks>
    internal const string WrittenForm = "yyyy-MM-ddTHH:mm";

    /// <summary>Reads one written wall clock into the instant it names where the turn was written.</summary>
    /// <param name="written">What the model wrote, which may be absent, blank, or not an instant at all.</param>
    /// <param name="anchor">The instant the turn stated, whose offset the written wall clock belongs to.</param>
    /// <returns>The instant, or <see langword="null" /> where what was written is not one in the stated form.</returns>
    /// <remarks>
    /// The two guarded days at the ends of the range are what applying an offset can push past: a local time within a
    /// day of either bound of <see cref="DateTime" /> has no instant in some zones, and constructing one raises rather
    /// than answering. A model writing a year one date has misread its turn, so refusing it is the accurate reading as
    /// well as the safe one.
    /// </remarks>
    internal static DateTimeOffset? Read(string? written, DateTimeOffset anchor)
    {
        if (!DateTime.TryParseExact(
            written,
            WrittenFormats,
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var local))
        {
            return null;
        }

        return local >= DateTime.MinValue.AddDays(1) && local <= DateTime.MaxValue.AddDays(-1)
            ? new DateTimeOffset(local, anchor.Offset)
            : null;
    }
}
