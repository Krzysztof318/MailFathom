// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Folders;
using MailFathom.Domain.Mutations;

namespace MailFathom.Application.Mail.Mutations;

/// <summary>Pairs one mutation that has not completed with the folder binding it was recorded against.</summary>
/// <param name="Record">The durable record, exactly as it stands.</param>
/// <param name="Folder">The binding the source occurrence belongs to, including the remote path a session selects.</param>
/// <remarks>
/// The binding travels with the record because the alias and generation a record carries name a folder without saying
/// where it is, and every use of an outstanding record needs both: performing the change again selects the remote path,
/// and reporting the change to an operator names the alias. Re-resolving the alias against the server instead would ask
/// a mail server a question the row already answers, and could answer it with a folder the record was never written
/// against.
/// </remarks>
public sealed record OutstandingMailboxMutation(MailboxMutationRecord Record, MailFolderResolution Folder);
