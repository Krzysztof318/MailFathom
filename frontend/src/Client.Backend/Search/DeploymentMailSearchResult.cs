// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Client.Backend.Search;

/// <summary>One ranked message and the evidence explaining why it is in the result list.</summary>
public sealed record DeploymentMailSearchResult(
    Guid Id,
    string Account,
    string Folder,
    Guid? ThreadId,
    string? Subject,
    DateTimeOffset? ReceivedAt,
    DateTimeOffset? SentAt,
    string? SenderAddress,
    string? SenderDisplayName,
    IReadOnlyList<string> ToAddresses,
    bool Unread,
    bool Flagged,
    bool Answered,
    bool HasAttachments,
    int AttachmentCount,
    long SizeOctets,
    string? Preview,
    IReadOnlyList<string> Snippets,
    string MatchedBy)
{
    /// <summary>Gets the recipients, reading an absent wire list as empty.</summary>
    public IReadOnlyList<string> Recipients => this.ToAddresses ?? [];

    /// <summary>Gets the highlighted extracts, reading an absent wire list as empty.</summary>
    public IReadOnlyList<string> Extracts => this.Snippets ?? [];

    /// <summary>Gets which ranking found this message.</summary>
    public MailSearchMatchOrigin Origin => this.MatchedBy switch
    {
        "LexicalRanking" => MailSearchMatchOrigin.LexicalRanking,
        "SemanticRanking" => MailSearchMatchOrigin.SemanticRanking,
        "BothRankings" => MailSearchMatchOrigin.BothRankings,
        _ => MailSearchMatchOrigin.Unrecognized,
    };
}

/// <summary>Which ranking placed a message in one search.</summary>
public enum MailSearchMatchOrigin
{
    /// <summary>The deployment named an origin this client does not understand.</summary>
    Unrecognized = 0,

    /// <summary>The words of the query found the message.</summary>
    LexicalRanking = 1,

    /// <summary>The meaning of the query found the message.</summary>
    SemanticRanking = 2,

    /// <summary>Both words and meaning found the message.</summary>
    BothRankings = 3,
}
