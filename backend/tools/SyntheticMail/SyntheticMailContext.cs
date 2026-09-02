// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.SyntheticMail.Configuration;
using MailFathom.SyntheticMail.Delivery;
using MailFathom.SyntheticMail.Generation.AiContent;

namespace MailFathom.SyntheticMail;

/// <summary>Everything the command needs from outside itself.</summary>
/// <param name="Console">The terminal the command reports to.</param>
/// <param name="ReadAccount">Reads the sending account from the named local file.</param>
/// <param name="ReadWatchedMailbox">Reads the mailbox MailFathom synchronizes from the same local file.</param>
/// <param name="ReadAiProvider">Reads the AI provider from the named local file.</param>
/// <param name="OpenTransport">Opens a submission session for one account; the caller disposes it.</param>
/// <param name="OpenWatchedMailbox">Opens an IMAP session against one mailbox; the caller disposes it.</param>
/// <param name="OpenAiContentSource">Opens a content source over one provider configuration.</param>
/// <param name="Clock">What resolves today's date, what the pacing waits on, and what bounds a delivery wait.</param>
/// <remarks>
/// One seam rather than six, so a test drives the command end to end without a mail server, without a credential
/// file, and without the wall clock. Everything this tool reaches outside its own process is behind it.
/// </remarks>
internal sealed record SyntheticMailContext(
    ISyntheticMailConsole Console,
    Func<string, SendingAccount> ReadAccount,
    Func<string, WatchedMailboxAccount> ReadWatchedMailbox,
    Func<string, AiProviderConfiguration> ReadAiProvider,
    Func<SendingAccount, ISyntheticMailTransport> OpenTransport,
    Func<WatchedMailboxAccount, IWatchedMailbox> OpenWatchedMailbox,
    Func<AiProviderConfiguration, IAiEmailContentSource> OpenAiContentSource,
    TimeProvider Clock)
{
    /// <summary>Builds the context the command runs under for a developer at a terminal.</summary>
    /// <returns>The context.</returns>
    internal static SyntheticMailContext ForTerminal() => new(
        new SystemSyntheticMailConsole(),
        SendingAccountFile.Read,
        SendingAccountFile.ReadWatchedMailbox,
        SyntheticAiProviderFile.Read,
        account => new SmtpSyntheticMailTransport(account),
        watched => new ImapWatchedMailbox(watched),
        provider => new OpenAiEmailContentSource(provider),
        TimeProvider.System);
}
