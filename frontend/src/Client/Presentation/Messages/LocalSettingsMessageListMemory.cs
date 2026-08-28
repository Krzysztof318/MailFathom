// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Client.Backend.Timeline;

namespace MailFathom.Client.Presentation.Messages;

/// <summary>Keeps a list's position for the run, and the one somebody is in for the next one.</summary>
/// <remarks>
/// <para>
/// <see cref="ApplicationData.LocalSettings" /> for the place that outlives the process, for the reason the mailbox
/// tree's arrangement is kept there: every head already has one and each maps it to what that platform actually uses —
/// a per-user preferences store on a desktop and the browser's own storage for the page's origin in the browser head.
/// </para>
/// <para>
/// Every other place a run visited is kept in <see cref="VisitedPlaces" /> and nowhere else. Moving between two
/// folders has to return to each of them, which needs several places; starting the client again returns to the one the
/// tree reopens on, which needs one. That split is the whole reason this type composes the two rather than being one
/// of them.
/// </para>
/// <para>
/// The persisted values are written as plain text rather than as anything serialized, because a settings store holds
/// simple values and the browser head publishes trimmed. An entry that is no longer readable is treated as nothing
/// having been remembered, which is the same answer a first run gives and is never a failure to start — and a cursor a
/// deployment no longer honours is refused by that deployment rather than judged here, which is why nothing below
/// inspects one.
/// </para>
/// </remarks>
internal sealed class LocalSettingsMessageListMemory : IMessageListMemory
{
    /// <summary>The name the place the persisted entry belongs to is kept under.</summary>
    /// <remarks>Qualified, because the container is shared with every other setting this application and its framework keep.</remarks>
    internal const string PlaceSettingName = "MailFathom.Messages.Place";

    /// <summary>The name the cursor the list reopens at is kept under.</summary>
    internal const string CursorSettingName = "MailFathom.Messages.Cursor";

    /// <summary>The name the direction that cursor is asked in is kept under.</summary>
    internal const string DirectionSettingName = "MailFathom.Messages.Direction";

    /// <summary>The name the order the list is read in is kept under.</summary>
    internal const string OrderSettingName = "MailFathom.Messages.Order";

    /// <summary>The name the filters the list keeps are kept under, as one entry.</summary>
    internal const string KeepsSettingName = "MailFathom.Messages.Keeps";

    private const string NewestFirst = "newestFirst";
    private const string OldestFirst = "oldestFirst";
    private const string Forward = "forward";
    private const string Backward = "backward";
    private const string KeepsUnread = "unread";
    private const string KeepsFlagged = "flagged";
    private const string KeepsAttachments = "attachments";
    private const string KeepsJunk = "junk";
    private const char KeepsSeparator = ' ';

    private readonly VisitedPlaces visited = new();

    /// <inheritdoc />
    public RememberedMessageList Read(string placeKey)
    {
        ArgumentException.ThrowIfNullOrEmpty(placeKey);

        return this.visited.Read(placeKey) ?? Persisted(placeKey) ?? RememberedMessageList.Nothing(placeKey);
    }

    /// <inheritdoc />
    public void Write(RememberedMessageList remembered)
    {
        ArgumentNullException.ThrowIfNull(remembered);

        this.visited.Keep(remembered);

        Persist(remembered);
    }

    /// <summary>Writes the filters as one value, in a form a reader of the store can tell apart from a cursor.</summary>
    /// <param name="arrangement">What the list keeps.</param>
    /// <returns>The filters as one entry, empty where the list keeps everything.</returns>
    internal static string JoinedKeeps(MessageListArrangement arrangement)
    {
        ArgumentNullException.ThrowIfNull(arrangement);

        var kept = new List<string>(4);

        if (arrangement.UnreadOnly)
        {
            kept.Add(KeepsUnread);
        }

        if (arrangement.FlaggedOnly)
        {
            kept.Add(KeepsFlagged);
        }

        if (arrangement.WithAttachmentsOnly)
        {
            kept.Add(KeepsAttachments);
        }

        if (arrangement.IncludeJunk)
        {
            kept.Add(KeepsJunk);
        }

        return string.Join(KeepsSeparator, kept);
    }

    /// <summary>Reads the arrangement back out of the two values it was written as.</summary>
    /// <param name="order">What the order was written as, or <see langword="null" /> where nothing was written.</param>
    /// <param name="keeps">What the filters were written as, or <see langword="null" /> where nothing was.</param>
    /// <returns>The arrangement, taking the default for anything the store did not answer.</returns>
    /// <remarks>
    /// A closed mapping rather than <see cref="Enum.TryParse{TEnum}(string, bool, out TEnum)" />, which would also read
    /// a number and a comma-separated list as members — neither of which anything here ever wrote.
    /// </remarks>
    internal static MessageListArrangement ArrangementOf(string? order, string? keeps)
    {
        var kept = keeps?.Split(KeepsSeparator, StringSplitOptions.RemoveEmptyEntries) ?? [];

        return new MessageListArrangement
        {
            Order = string.Equals(order, OldestFirst, StringComparison.Ordinal)
                ? MailTimelineOrder.OldestFirst
                : MailTimelineOrder.NewestFirst,
            UnreadOnly = kept.Contains(KeepsUnread, StringComparer.Ordinal),
            FlaggedOnly = kept.Contains(KeepsFlagged, StringComparer.Ordinal),
            WithAttachmentsOnly = kept.Contains(KeepsAttachments, StringComparer.Ordinal),
            IncludeJunk = kept.Contains(KeepsJunk, StringComparer.Ordinal),
        };
    }

    /// <summary>Reads what the store holds, where it holds it for the place being asked about.</summary>
    private static RememberedMessageList? Persisted(string placeKey) =>
        string.Equals(Kept(PlaceSettingName), placeKey, StringComparison.Ordinal)
            ? new RememberedMessageList(
                placeKey,
                Kept(CursorSettingName),
                string.Equals(Kept(DirectionSettingName), Backward, StringComparison.Ordinal)
                    ? MailTimelinePageDirection.Backward
                    : MailTimelinePageDirection.Forward,
                ArrangementOf(Kept(OrderSettingName), Kept(KeepsSettingName)))
            : null;

    private static void Persist(RememberedMessageList remembered)
    {
        Keep(PlaceSettingName, remembered.PlaceKey);
        Keep(CursorSettingName, remembered.Cursor);
        Keep(
            DirectionSettingName,
            remembered.Direction is MailTimelinePageDirection.Backward ? Backward : Forward);
        Keep(
            OrderSettingName,
            remembered.Arrangement.Order is MailTimelineOrder.OldestFirst ? OldestFirst : NewestFirst);
        Keep(KeepsSettingName, JoinedKeeps(remembered.Arrangement));
    }

    private static string? Kept(string name) =>
        ApplicationData.Current.LocalSettings.Values.TryGetValue(name, out var kept) && kept is string written
        && !string.IsNullOrEmpty(written)
            ? written
            : null;

    /// <summary>Keeps a value, removing the entry rather than writing an empty one where there is nothing to keep.</summary>
    private static void Keep(string name, string? value)
    {
        var settings = ApplicationData.Current.LocalSettings.Values;

        if (string.IsNullOrEmpty(value))
        {
            settings.Remove(name);

            return;
        }

        settings[name] = value;
    }
}
