// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Accounts;
using MailFathom.Domain.Folders;

namespace MailFathom.Application.Retrieval.AskMail;

/// <summary>What a caller asks when they ask a question about their mail.</summary>
/// <remarks>
/// <para>
/// This is the unvalidated contract: the question is whatever the caller wrote, and the scope is what they named rather
/// than what this deployment serves. <see cref="MailboxQuestionReader" /> turns both into the validated
/// <see cref="MailQuestionText" /> and the resolved scope a run is composed around, so no protocol adapter can reach the
/// answering port with either one unchecked.
/// </para>
/// <para>
/// There is no structured filter beside them, and that is a decision rather than an omission. A question is answered
/// from what the model looks up while answering, and the lookups it makes are its own; a sender or a date range supplied
/// here would narrow every one of them without the model knowing why its searches were returning nothing.
/// </para>
/// </remarks>
public sealed record AskMailRequest
{
    /// <summary>Gets the question to answer.</summary>
    public string? QuestionText { get; init; }

    /// <summary>Gets the accounts the answer may be drawn from, or empty for every account this deployment serves.</summary>
    public IReadOnlyList<MailAccountId> AccountIds { get; init; } = [];

    /// <summary>Gets the folder aliases the answer may be drawn from, or empty for every folder of the named accounts.</summary>
    public IReadOnlyList<MailFolderAlias> FolderAliases { get; init; } = [];
}
