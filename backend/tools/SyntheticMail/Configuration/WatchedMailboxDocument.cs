// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.SyntheticMail.Configuration;

/// <summary>The <c>mailbox</c> block of the local file exactly as it is written, before anything about it has been checked.</summary>
/// <remarks>
/// Every member is optional for the reason <see cref="SendingAccountDocument" />'s are: a half-written block is what
/// this type represents, and <see cref="SendingAccountFile" /> is where a missing value becomes a message naming the
/// key to set.
/// </remarks>
internal sealed record WatchedMailboxDocument
{
    /// <summary>The IMAP host the mailbox is read and written through.</summary>
    public string? Host { get; init; }

    /// <summary>The IMAP port.</summary>
    public int? Port { get; init; }

    /// <summary>How the connection is secured, named after a <see cref="MailTransportSecurity" /> value.</summary>
    public string? Security { get; init; }

    /// <summary>The address of the mailbox MailFathom synchronizes, which is the recipient every exchange is delivered to.</summary>
    public string? Address { get; init; }

    /// <summary>The user name to authenticate with, when the server does not accept the address as one.</summary>
    public string? UserName { get; init; }

    /// <summary>The password for that mailbox, which belongs to a throwaway account and to no other.</summary>
    public string? Password { get; init; }

    /// <summary>The Sent folder to append the mailbox's own half of an exchange to, for a server advertising no special-use folder.</summary>
    public string? SentFolder { get; init; }

    /// <inheritdoc />
    /// <remarks>Redacted for the reason <see cref="SendingAccountDocument.ToString" /> is, and against the same failure: this block also holds a real password between parsing and validation.</remarks>
    public override string ToString() =>
        $"{nameof(WatchedMailboxDocument)} {{ {nameof(this.Host)} = {this.Host}, {nameof(this.Port)} = {this.Port}, {nameof(this.Security)} = {this.Security}, {nameof(this.Address)} = {this.Address}, {nameof(this.UserName)} = {this.UserName}, {nameof(this.Password)} = ***, {nameof(this.SentFolder)} = {this.SentFolder} }}";
}
