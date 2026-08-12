// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Folders;
using MailFathom.Domain.Mutations;

namespace MailFathom.Application.Rules.Actions;

/// <summary>One action a rule asked for and a durable mutation record was opened against.</summary>
/// <param name="RuleName">The rule that asked, which is MailFathom's own configured name for it.</param>
/// <param name="Position">Where the action sits in the order its own rule declares its changes, counted from zero.</param>
/// <param name="Mutation">The change asked for, which is the same word a log line and a counter use.</param>
/// <param name="RecordId">The record carrying the request, which is where what happened on the server is answered from.</param>
/// <param name="DestinationAlias">The folder the action named, and <see langword="null" /> for an action naming none.</param>
/// <remarks>
/// The record identifier is what makes the rule history a pointer rather than a second lifecycle. A request opened here
/// is carried by the account's convergence pass, which writes its attempts and its ending against that record and its
/// own audit trail; naming it is what leaves one answer to what happened to a message instead of two that can disagree.
/// </remarks>
public sealed record RecordedMailRuleAction(
    string RuleName,
    int Position,
    MailboxMutation Mutation,
    MailboxMutationRecordId RecordId,
    MailFolderAlias? DestinationAlias = null);
