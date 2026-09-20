// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;

namespace MailFathom.Domain.Reminders;

/// <summary>States how long before something a person is to be reminded of it.</summary>
/// <remarks>
/// <para>
/// A lead rather than an instant, which is what makes a reminder survive the thing it is about moving: what a person
/// chose is <em>a quarter of an hour beforehand</em>, and the instant that falls at is derived from that thing every
/// time it is asked for. The derivation lives on whatever carries the reminder, because the anchor an all-day event
/// or a task's due date is measured back from is that record's own affair rather than the lead's.
/// </para>
/// <para>
/// It is shared by every kind of thing that carries reminders rather than restated per kind. What a lead is — its
/// unit, its bounds, and what zero means — is one rule, and a second copy of it under another name is the way a
/// calendar and a task list come to disagree about what somebody set.
/// </para>
/// <para>
/// Zero is a lead like any other and means the moment the thing falls at. It is not the absence of a reminder:
/// something nobody wants to be told about carries no reminder at all.
/// </para>
/// <para>
/// The lead is whole minutes because that is the coarsest unit the client offers and the finest anything here acts on:
/// a run looking for what has come due cannot be more precise than the interval it runs on, and a reminder stated in
/// seconds would promise a precision no producer keeps.
/// </para>
/// </remarks>
public readonly record struct Reminder
{
    /// <summary>The longest lead a reminder may state, which is four weeks.</summary>
    /// <remarks>
    /// Wide enough for every lead the client offers and for the far end of what somebody would set by hand on a
    /// quarterly commitment, and bounded at all because the lead decides how far ahead of now a run has to look: an
    /// unbounded one would make every pass read every calendar and every task list rather than the days around them.
    /// </remarks>
    public const int MaximumMinutesBefore = 28 * 24 * 60;

    /// <summary>The most reminders one thing may carry.</summary>
    /// <remarks>
    /// Above every preset the client offers together, so nothing a person can press reaches it, and bounded at all
    /// because each reminder is a row a run reads and a notification it may write: one record carrying a thousand of
    /// them would be one person's way of filling somebody's notification centre. It is stated here rather than per
    /// kind because the reason is the producer's rather than the calendar's or the task list's.
    /// </remarks>
    public const int MaximumCount = 16;

    private Reminder(int minutesBefore) => this.MinutesBefore = minutesBefore;

    /// <summary>Gets how many minutes before the thing this reminder falls, which is zero at the thing itself.</summary>
    public int MinutesBefore { get; }

    /// <summary>Gets how long before the thing this reminder falls.</summary>
    public TimeSpan Lead => TimeSpan.FromMinutes(this.MinutesBefore);

    /// <summary>Creates a reminder from the lead a person stated.</summary>
    /// <param name="minutesBefore">How many minutes beforehand to remind them, which may be zero.</param>
    /// <returns>A validated reminder.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="minutesBefore" /> is negative or longer than <see cref="MaximumMinutesBefore" />.</exception>
    public static Reminder Create(int minutesBefore)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(minutesBefore);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(minutesBefore, MaximumMinutesBefore);

        return new Reminder(minutesBefore);
    }

    /// <summary>Reports whether a stated lead is one a reminder can be created from.</summary>
    /// <param name="minutesBefore">The lead as supplied.</param>
    /// <returns><see langword="true" /> when <see cref="Create" /> would accept it.</returns>
    /// <remarks>
    /// A surface reading what somebody typed asks this rather than catching the refusal, so a lead nobody can set is
    /// reported to them as a lead rather than as a fault in the deployment.
    /// </remarks>
    public static bool IsStatable(int minutesBefore) => minutesBefore is >= 0 and <= MaximumMinutesBefore;

    /// <summary>Puts a stated set of leads into the order reminders are kept in, refusing a set nothing may hold.</summary>
    /// <param name="reminders">The leads as stated, in any order.</param>
    /// <returns>The leads, longest first.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="reminders" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when more than <see cref="MaximumCount" /> leads are supplied.</exception>
    /// <exception cref="ArgumentException">Thrown when one lead is supplied twice.</exception>
    /// <remarks>
    /// Longest lead first, because that is the order a person reads their own reminders in — the earliest warning is
    /// the one furthest from the thing. The order belongs to the record rather than to the writer so that two writers
    /// stating the same leads produce the same record, and a repeated lead is refused rather than folded away: it is
    /// a caller stating one reminder twice, and answering as though it had asked for one would hide the mistake.
    /// </remarks>
    public static IReadOnlyList<Reminder> Ordered(IReadOnlyCollection<Reminder> reminders)
    {
        ArgumentNullException.ThrowIfNull(reminders);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(reminders.Count, MaximumCount, nameof(reminders));

        var ordered = reminders.OrderByDescending(reminder => reminder.MinutesBefore).ToArray();

        if (ordered.Distinct().Count() != ordered.Length)
        {
            throw new ArgumentException("A set of reminders states each lead once.", nameof(reminders));
        }

        return ordered;
    }

    /// <inheritdoc />
    public override string ToString() => this.MinutesBefore.ToString(CultureInfo.InvariantCulture);
}
