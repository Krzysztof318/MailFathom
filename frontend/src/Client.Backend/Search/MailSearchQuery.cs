// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;

namespace MailFathom.Client.Backend.Search;

/// <summary>One ranked search and every constraint the client asks it under.</summary>
public sealed record MailSearchQuery
{
    /// <summary>The text to match and rank by.</summary>
    public string Query { get; init; } = string.Empty;

    /// <summary>The account to search, or <see langword="null" /> for every account.</summary>
    public string? Account { get; init; }

    /// <summary>The folder alias or role to search, or <see langword="null" /> for every folder.</summary>
    public string? Folder { get; init; }

    /// <summary>Whether junk mail takes part.</summary>
    public bool IncludeJunk { get; init; }

    /// <summary>The sender address required, or <see langword="null" /> for any sender.</summary>
    public string? Sender { get; init; }

    /// <summary>The recipient address required, or <see langword="null" /> for any recipient.</summary>
    public string? Recipient { get; init; }

    /// <summary>Whether only unread or only read mail is kept, or <see langword="null" /> for both.</summary>
    public bool? Unread { get; init; }

    /// <summary>Whether only flagged or only unflagged mail is kept, or <see langword="null" /> for both.</summary>
    public bool? Flagged { get; init; }

    /// <summary>Whether mail must have attachments, must have none, or may have either.</summary>
    public bool? HasAttachments { get; init; }

    /// <summary>The inclusive beginning of the received range.</summary>
    public DateTimeOffset? ReceivedOnOrAfter { get; init; }

    /// <summary>The exclusive end of the received range.</summary>
    public DateTimeOffset? ReceivedBefore { get; init; }

    /// <summary>How many results the page may hold.</summary>
    public int? PageSize { get; init; }

    /// <summary>The cursor continuing the same ranked search.</summary>
    public string? Cursor { get; init; }

    /// <summary>Writes this search as the query string the client route accepts.</summary>
    internal string QueryString()
    {
        var stated = new List<string>(13);

        Add(stated, "query", this.Query);
        Add(stated, "account", this.Account);
        Add(stated, "folder", this.Folder);

        if (this.IncludeJunk)
        {
            Add(stated, "includeJunk", "true");
        }

        Add(stated, "sender", this.Sender);
        Add(stated, "recipient", this.Recipient);
        Add(stated, "unread", Written(this.Unread));
        Add(stated, "flagged", Written(this.Flagged));
        Add(stated, "hasAttachments", Written(this.HasAttachments));
        Add(stated, "receivedOnOrAfter", Written(this.ReceivedOnOrAfter));
        Add(stated, "receivedBefore", Written(this.ReceivedBefore));
        Add(stated, "pageSize", this.PageSize?.ToString(CultureInfo.InvariantCulture));
        Add(stated, "cursor", this.Cursor);

        return stated.Count is 0 ? string.Empty : $"?{string.Join('&', stated)}";
    }

    private static void Add(List<string> stated, string name, string? value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            stated.Add($"{name}={Uri.EscapeDataString(value)}");
        }
    }

    private static string? Written(bool? value) => value is { } stated ? (stated ? "true" : "false") : null;

    private static string? Written(DateTimeOffset? value) => value?.ToString("O", CultureInfo.InvariantCulture);
}
