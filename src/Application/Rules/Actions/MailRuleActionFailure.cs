// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Folders;
using MailFathom.Domain.Mutations;

namespace MailFathom.Application.Rules.Actions;

/// <summary>One action a rule asked for and nothing was recorded against, with the reason nothing was.</summary>
/// <param name="RuleName">The rule that asked, which is MailFathom's own configured name for it.</param>
/// <param name="Position">Where the action sits in the order its own rule declares its changes, counted from zero.</param>
/// <param name="Mutation">The change it asked for, which is the same word a log line and a counter use.</param>
/// <param name="Reason">Why the request could not be written.</param>
/// <param name="DestinationAlias">The folder the action named, and <see langword="null" /> for an action naming none.</param>
/// <remarks>
/// It names identities and a mutation name only. A rule name and a folder alias are both MailFathom's own configured
/// names, which is what lets a failure be reported without saying which message it was about. The position is what
/// attributes the refusal to one of a rule's declared changes rather than to the rule as a whole.
/// </remarks>
public sealed record MailRuleActionFailure(
    string RuleName,
    int Position,
    MailboxMutation Mutation,
    MailRuleActionFailureReason Reason,
    MailFolderAlias? DestinationAlias = null);
