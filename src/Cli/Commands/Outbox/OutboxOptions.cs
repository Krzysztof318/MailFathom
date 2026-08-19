// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.CommandLine;

namespace MailFathom.Cli.Commands.Outbox;

/// <summary>The options the outbox commands name a send and an account with.</summary>
/// <remarks>
/// Declared once because the three commands that act on one send name it the same way, and a description that drifted
/// between them would be the first hint an operator has that they take different identifiers. It lives here rather than
/// in <see cref="CliOptions" /> because nothing outside this group names a queued message.
/// </remarks>
internal static class OutboxOptions
{
    /// <summary>The word an operator adds to say they mean to offer a permanently refused message again.</summary>
    /// <remarks>
    /// Named where both the command that declares it and the sentence that tells an operator to type it can reach it,
    /// so the refusal never names a flag the command does not have.
    /// </remarks>
    internal const string RefusalRestatedFlag = "--despite-refusal";

    /// <summary>Builds the option naming which send a command is about.</summary>
    /// <returns>The option.</returns>
    /// <remarks>
    /// Required and without a default, for the reason every destination-naming option here is: each of these commands
    /// acts on one specific message somebody is waiting for, and there is no send it would be reasonable to guess.
    /// </remarks>
    internal static Option<Guid> Message() => new("--message")
    {
        Description = "The queued message to act on, by the identifier the outbox reading reports for it.",
        Required = true,
    };

    /// <summary>Builds the option narrowing a reading to one account.</summary>
    /// <returns>The option.</returns>
    /// <remarks>Optional, because "what is stuck" is a question about the instance first: an operator narrows once they know which mailbox it is.</remarks>
    internal static Option<string?> Account() => new("--account")
    {
        Description = "Report only the sends of this account, as the deployment's configuration names it.",
    };
}
