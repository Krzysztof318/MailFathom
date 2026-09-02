// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MimeKit;

namespace MailFathom.SyntheticMail.Configuration;

/// <summary>The mailbox MailFathom synchronizes, once every value has been checked.</summary>
/// <param name="Host">The IMAP host.</param>
/// <param name="Port">The IMAP port.</param>
/// <param name="Security">How the connection carrying the credential is secured.</param>
/// <param name="Address">The address of the mailbox, which is the recipient every exchange is delivered to.</param>
/// <param name="UserName">The user name authentication presents, which is usually the address itself.</param>
/// <param name="Password">The password, which never reaches an argument, a log line, or the process list.</param>
/// <param name="SentFolder">The Sent folder to append to, or <see langword="null" /> to use whichever folder the server advertises as its own.</param>
/// <remarks>
/// <para>
/// An exchange needs this mailbox for two things a sending account cannot do. The first is reading back the identifier
/// the server assigned to what was delivered, because the <c>Message-Id</c> a run proposes is the submission server's
/// to replace and a reply built on the proposed one references a message the mailbox does not hold. The second is
/// putting the mailbox's own half of the exchange where a mailbox keeps it, which is an <c>APPEND</c> to its Sent
/// folder rather than a submission.
/// </para>
/// <para>
/// It carries no submission settings, and deliberately: the thread has to assemble in the mailbox MailFathom
/// synchronizes, and every message of it is either delivered there by the sending account or appended there by this
/// run. Submitting the mailbox's half onwards to the sending account would need a second credential and a second
/// refusal path to produce a copy nothing here reads.
/// </para>
/// <para>
/// The password is carried as an ordinary string and printed as <c>***</c> for the reasons
/// <see cref="SendingAccount" /> gives for its own.
/// </para>
/// </remarks>
internal sealed record WatchedMailboxAccount(
    string Host,
    int Port,
    MailTransportSecurity Security,
    MailboxAddress Address,
    string UserName,
    string Password,
    string? SentFolder)
{
    /// <inheritdoc />
    /// <remarks>Written by hand so the synthesized printer cannot put <see cref="Password" /> into a log line, a debugger view, or an interpolation of the whole record.</remarks>
    public override string ToString() =>
        $"{nameof(WatchedMailboxAccount)} {{ {nameof(this.Host)} = {this.Host}, {nameof(this.Port)} = {this.Port}, {nameof(this.Security)} = {this.Security}, {nameof(this.Address)} = {this.Address}, {nameof(this.UserName)} = {this.UserName}, {nameof(this.Password)} = ***, {nameof(this.SentFolder)} = {this.SentFolder} }}";
}
