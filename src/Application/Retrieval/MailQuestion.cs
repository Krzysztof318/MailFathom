// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Mailboxes;

namespace MailFathom.Application.Retrieval;

/// <summary>What a caller asks about their mail, and the mail the answer may be drawn from.</summary>
/// <param name="Text">The validated question, in the words the caller wrote it in.</param>
/// <param name="Scope">The accounts and folders the answer may be drawn from.</param>
/// <remarks>
/// <para>
/// The scope belongs to the question rather than to the deployment because it is the caller's authorization expressed as
/// data: a question asked over one account must not be answerable from another, whatever the model is later told. It is
/// resolved before the run starts and applied to every retrieval the run makes.
/// </para>
/// <para>
/// Both halves arrive validated and neither can be built otherwise, so an entrypoint added later reaches the answering
/// port with a bounded question and a resolved scope or reaches it not at all.
/// </para>
/// </remarks>
public sealed record MailQuestion(MailQuestionText Text, MailboxScope Scope);
