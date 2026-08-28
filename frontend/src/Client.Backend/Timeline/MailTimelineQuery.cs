// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;

namespace MailFathom.Client.Backend.Timeline;

/// <summary>One page of the message list, as this client asks for it.</summary>
/// <remarks>
/// <para>
/// The place, what the list keeps, the order it is read in, and where the page continues from, in the one record a
/// screen composes. A page is asked for by cursor rather than by number, so reading message forty thousand costs what
/// reading message one costs and a screen that was left and returned to continues rather than starting over.
/// </para>
/// <para>
/// A cursor names the row it was taken at <em>and</em> the list it was taken from, so a request that carries a cursor
/// has to carry the same place, filters, and order the cursor was issued under — the deployment refuses the pair
/// rather than quietly answering with the leading page. That is why all of it is one record: a screen that changed a
/// filter and kept a cursor would be composing a request that cannot be served.
/// </para>
/// <para>
/// Nothing here names what the list is sorted by. The route accepts one column and takes it where a request names
/// none, so stating it would be this client asserting a value it has no second choice for.
/// </para>
/// </remarks>
public sealed record MailTimelineQuery
{
    /// <summary>The account the list is drawn from, or <see langword="null" /> for every account the owner owns.</summary>
    public string? Account { get; init; }

    /// <summary>
    /// The folder the list is drawn from — an alias, or a special-use role written as <c>role:Inbox</c> — or
    /// <see langword="null" /> for every folder.
    /// </summary>
    /// <remarks>
    /// The role form is what makes <em>sent</em> mean sent from any mailbox rather than one mailbox's own sent folder,
    /// which is a narrowing the mailbox tree offers and no single alias expresses.
    /// </remarks>
    public string? Folder { get; init; }

    /// <summary>Whether the junk folder takes part, which it does not unless a request asks.</summary>
    public bool IncludeJunk { get; init; }

    /// <summary>Keeps only unread mail, only read mail, or <see langword="null" /> for both.</summary>
    public bool? Unread { get; init; }

    /// <summary>Keeps only flagged mail, only unflagged mail, or <see langword="null" /> for both.</summary>
    public bool? Flagged { get; init; }

    /// <summary>Keeps only mail carrying attachments, only mail carrying none, or <see langword="null" /> for both.</summary>
    public bool? HasAttachments { get; init; }

    /// <summary>Which end of the list leads.</summary>
    public MailTimelineOrder Order { get; init; } = MailTimelineOrder.NewestFirst;

    /// <summary>Which way the page continues from <see cref="Cursor" />.</summary>
    public MailTimelinePageDirection Direction { get; init; } = MailTimelinePageDirection.Forward;

    /// <summary>How many rows the page may hold, or <see langword="null" /> for the deployment's own default.</summary>
    public int? PageSize { get; init; }

    /// <summary>The cursor a previous page returned, or <see langword="null" /> for the leading end of the list.</summary>
    /// <remarks>A backward page without one is refused by the deployment, because there is no row to read away from.</remarks>
    public string? Cursor { get; init; }

    /// <summary>The word the wire carries for the newest-first order.</summary>
    internal const string NewestFirstOrder = "newestFirst";

    /// <summary>The word the wire carries for the oldest-first order.</summary>
    internal const string OldestFirstOrder = "oldestFirst";

    /// <summary>The word the wire carries for a page after its cursor.</summary>
    internal const string ForwardDirection = "forward";

    /// <summary>The word the wire carries for a page before its cursor.</summary>
    internal const string BackwardDirection = "backward";

    /// <summary>Writes this query as the query string the timeline route is asked with.</summary>
    /// <returns>The query string, beginning with <c>?</c>, or an empty string where nothing is stated.</returns>
    /// <remarks>
    /// A value nobody stated is left out rather than sent empty. The route reads an empty parameter as an absent one,
    /// so both spellings work — but a request that says only what it means is the one a reader of a log or a proxy can
    /// tell apart from a request that narrowed to nothing on purpose.
    /// </remarks>
    internal string QueryString()
    {
        var stated = new List<string>(9);

        Add(stated, "account", this.Account);
        Add(stated, "folder", this.Folder);

        if (this.IncludeJunk)
        {
            Add(stated, "includeJunk", Written(true));
        }

        Add(stated, "unread", Written(this.Unread));
        Add(stated, "flagged", Written(this.Flagged));
        Add(stated, "hasAttachments", Written(this.HasAttachments));
        Add(stated, "order", Written(this.Order));
        Add(stated, "direction", Written(this.Direction));
        Add(stated, "pageSize", this.PageSize?.ToString(CultureInfo.InvariantCulture));
        Add(stated, "cursor", this.Cursor);

        return stated.Count is 0 ? string.Empty : $"?{string.Join('&', stated)}";
    }

    /// <summary>States one parameter, escaping the value for the query string it is written into.</summary>
    /// <remarks>
    /// A folder alias and a cursor are both values this client received rather than composed, and an alias is a name a
    /// mail server chose — so neither may be written raw into a URL, whatever either happens to look like today.
    /// </remarks>
    private static void Add(List<string> stated, string name, string? value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            stated.Add($"{name}={Uri.EscapeDataString(value)}");
        }
    }

    private static string? Written(bool? kept) => kept is { } wanted ? Written(wanted) : null;

    private static string Written(bool wanted) => wanted ? "true" : "false";

    private static string Written(MailTimelineOrder order) =>
        order is MailTimelineOrder.OldestFirst ? OldestFirstOrder : NewestFirstOrder;

    private static string Written(MailTimelinePageDirection direction) =>
        direction is MailTimelinePageDirection.Backward ? BackwardDirection : ForwardDirection;
}
