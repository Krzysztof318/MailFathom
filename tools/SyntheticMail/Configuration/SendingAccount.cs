// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.SyntheticMail.Generation;
using MimeKit;

namespace MailFathom.SyntheticMail.Configuration;

/// <summary>The throwaway account a run submits as, once every value has been checked.</summary>
/// <param name="Host">The submission host.</param>
/// <param name="Port">The submission port.</param>
/// <param name="Security">How the connection carrying the credential is secured.</param>
/// <param name="Address">The address the run submits as, and the envelope sender of every message.</param>
/// <param name="UserName">The user name authentication presents, which is usually the address itself.</param>
/// <param name="Password">The password, which never reaches an argument, a log line, or the process list.</param>
/// <param name="AuthorIdentity">Whose address generated mail is from.</param>
/// <remarks>
/// Only this type crosses into the delivery layer, so nothing there has to remember which values were validated. The
/// password is carried as an ordinary string because it is read from a local file and handed straight to one
/// authentication call; nothing here logs, serializes, or persists it, and <see cref="ToString" /> is what keeps
/// printing out of that list rather than the habits of the call sites.
/// </remarks>
internal sealed record SendingAccount(
    string Host,
    int Port,
    SmtpTransportSecurity Security,
    MailboxAddress Address,
    string UserName,
    string Password,
    SyntheticAuthorIdentity AuthorIdentity)
{
    /// <inheritdoc />
    /// <remarks>
    /// Written by hand because the synthesized one prints every member, <see cref="Password" /> included — so an
    /// interpolation of the whole record, a future log line, or a debugger inspection would put a real credential
    /// somewhere nobody meant to. Every call site interpolates individual fields today, and that is exactly the kind
    /// of guarantee that holds until one of them does not, so the redaction belongs to the type.
    /// <c>ResolvedSecret.ToString()</c> in <c>Infrastructure</c> redacts for the same reason; this is that decision at
    /// the scale a development tool needs, which is a printing rule rather than an erasable buffer.
    /// </remarks>
    public override string ToString() =>
        $"{nameof(SendingAccount)} {{ {nameof(this.Host)} = {this.Host}, {nameof(this.Port)} = {this.Port}, {nameof(this.Security)} = {this.Security}, {nameof(this.Address)} = {this.Address}, {nameof(this.UserName)} = {this.UserName}, {nameof(this.Password)} = ***, {nameof(this.AuthorIdentity)} = {this.AuthorIdentity} }}";
}
