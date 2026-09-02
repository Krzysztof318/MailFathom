// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.SyntheticMail.Configuration;

/// <summary>The local file exactly as it is written, before anything about it has been checked.</summary>
/// <remarks>
/// Every member is optional, because a half-written file is the case this type exists to represent: what turns it into
/// a <see cref="SendingAccount" /> is <see cref="SendingAccountFile" />, which is where a missing value becomes a
/// message naming the key to set rather than a null reference somewhere later.
/// </remarks>
internal sealed record SendingAccountDocument
{
    /// <summary>The submission host.</summary>
    public string? Host { get; init; }

    /// <summary>The submission port.</summary>
    public int? Port { get; init; }

    /// <summary>How the connection is secured, named after a <see cref="MailTransportSecurity" /> value.</summary>
    public string? Security { get; init; }

    /// <summary>The address the run authenticates and submits as.</summary>
    public string? Address { get; init; }

    /// <summary>The password for that address, which belongs to a throwaway account and to no other.</summary>
    public string? Password { get; init; }

    /// <summary>The user name to authenticate with, when the server does not accept the address as one.</summary>
    public string? UserName { get; init; }

    /// <summary>Whose address generated mail is from, named after a <see cref="Generation.SyntheticAuthorIdentity" /> value.</summary>
    public string? Author { get; init; }

    /// <summary>The mailbox MailFathom synchronizes, which only a run generating exchanges needs.</summary>
    public WatchedMailboxDocument? Mailbox { get; init; }

    /// <inheritdoc />
    /// <remarks>
    /// Redacted for the reason <see cref="SendingAccount.ToString" /> is, one step earlier in the same pipeline: this
    /// is what holds the credential between parsing and validation, so it is what a message about a file that failed
    /// validation would be written from. The synthesized printer prints every member, which would put a real password
    /// into the one kind of output this type exists to produce.
    /// </remarks>
    public override string ToString() =>
        $"{nameof(SendingAccountDocument)} {{ {nameof(this.Host)} = {this.Host}, {nameof(this.Port)} = {this.Port}, {nameof(this.Security)} = {this.Security}, {nameof(this.Address)} = {this.Address}, {nameof(this.Password)} = ***, {nameof(this.UserName)} = {this.UserName}, {nameof(this.Author)} = {this.Author}, {nameof(this.Mailbox)} = {this.Mailbox} }}";
}
