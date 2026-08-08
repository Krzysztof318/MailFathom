// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.SyntheticMail.Configuration;
using MailFathom.SyntheticMail.Delivery;

namespace MailFathom.SyntheticMail;

/// <summary>Everything the command needs from outside itself.</summary>
/// <param name="Console">The terminal the command reports to.</param>
/// <param name="ReadAccount">Reads the sending account from the named local file.</param>
/// <param name="OpenTransport">Opens a submission session for one account; the caller disposes it.</param>
/// <param name="Clock">What resolves today's date and what the pacing waits on.</param>
/// <remarks>
/// One seam rather than four, so a test drives the command end to end without a mail server, without a credential
/// file, and without the wall clock. Everything this tool reaches outside its own process is behind it.
/// </remarks>
internal sealed record SyntheticMailContext(
    ISyntheticMailConsole Console,
    Func<string, SendingAccount> ReadAccount,
    Func<SendingAccount, ISyntheticMailTransport> OpenTransport,
    TimeProvider Clock)
{
    /// <summary>Builds the context the command runs under for a developer at a terminal.</summary>
    /// <returns>The context.</returns>
    internal static SyntheticMailContext ForTerminal() => new(
        new SystemSyntheticMailConsole(),
        SendingAccountFile.Read,
        account => new SmtpSyntheticMailTransport(account),
        TimeProvider.System);
}
