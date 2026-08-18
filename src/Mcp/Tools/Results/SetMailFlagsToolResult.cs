// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.ComponentModel;
using MailFathom.Application.Mail.Mutations.Authoring;

namespace MailFathom.Mcp.Tools.Results;

/// <summary>Publishes what a request to write flags and keywords was written down as.</summary>
/// <remarks>
/// <para>
/// It answers with records rather than with a mailbox, because at the moment it is produced no IMAP command has gone
/// out. Saying that plainly is what stops a caller reading the result as the star already being on the message: the
/// account's own run carries each record to the server, and a change that never arrives is found by its record.
/// </para>
/// <para>
/// Nothing derived from the message appears. The email's own identifier, the account, the folder alias, and MailFathom's
/// record identities are all its own names for things, and the keywords the caller sent are not repeated back — a label
/// is text the owner chose and can name a person or a case, and the caller already holds what it wrote.
/// </para>
/// </remarks>
[Description("What the change was written down as: one durable record per value asked for, each carried to the mail server by the account's next run.")]
internal sealed record SetMailFlagsToolResult
{
    /// <summary>Gets the email the change was written down against.</summary>
    [Description("The storedEmailId the change was recorded against, which is the one the call named.")]
    public required string StoredEmailId { get; init; }

    /// <summary>Gets the account whose run will carry the change.</summary>
    [Description("The account the email belongs to. Its next synchronization run is what issues the change to the mail server.")]
    public required string AccountId { get; init; }

    /// <summary>Gets the operator's own name for the folder the email is in.</summary>
    [Description("The folder alias the email is in, as MailFathom's configuration names it.")]
    public required string FolderAlias { get; init; }

    /// <summary>Gets one entry per value the call asked for.</summary>
    [Description("One entry per value asked for, in the order seen, flagged, keywords. A call that asked for one value carries one entry.")]
    public required IReadOnlyList<RecordedMailboxChange> RecordedChanges { get; init; }

    /// <summary>Publishes what the use case recorded.</summary>
    /// <param name="result">What was written down.</param>
    /// <returns>The wire representation of <paramref name="result" />.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="result" /> is <see langword="null" />.</exception>
    public static SetMailFlagsToolResult From(AuthoredMailFlagChangeResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new SetMailFlagsToolResult
        {
            StoredEmailId = result.StoredEmailId.ToString(),
            AccountId = result.AccountId.Value,
            FolderAlias = result.FolderAlias.Value,
            RecordedChanges =
            [
                .. result.Recorded.Select(static recorded => new RecordedMailboxChange
                {
                    Change = recorded.Mutation.Name,
                    ChangeRecordId = recorded.RecordId.ToString(),
                    State = recorded.Lifecycle.Name,
                }),
            ],
        };
    }
}

/// <summary>Publishes one durable record a change produced.</summary>
/// <remarks>
/// The change and the state are published under the names MailFathom's own log lines and counters use, so a caller
/// quoting one to an operator is quoting the word they will find. Neither is a caller-facing enumeration this surface
/// invented, which is why both are text.
/// </remarks>
[Description("One durable record: which change it carries, what identifies it, and where it stands.")]
internal sealed record RecordedMailboxChange
{
    /// <summary>Gets the change the record carries.</summary>
    [Description("The change written down: set-seen, set-flagged, add-keywords, remove-keywords, or set-keywords.")]
    public required string Change { get; init; }

    /// <summary>Gets what everything afterwards refers to that change by.</summary>
    [Description("The identifier of the durable record. Quote it when asking an operator why a change has not arrived.")]
    public required string ChangeRecordId { get; init; }

    /// <summary>Gets where the record stands.</summary>
    [Description("Where the record stands: pending means nothing has been issued to the mail server yet, which is what a fresh call returns; converging, completed, and dead-lettered are the later states, and a call repeated under the same requestId answers with whichever the earlier one has reached. dead-lettered means the change will not be attempted again and needs an operator.")]
    public required string State { get; init; }
}
