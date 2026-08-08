// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
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

    /// <summary>How the connection is secured, named after a <see cref="SmtpTransportSecurity" /> value.</summary>
    public string? Security { get; init; }

    /// <summary>The address the run authenticates and submits as.</summary>
    public string? Address { get; init; }

    /// <summary>The password for that address, which belongs to a throwaway account and to no other.</summary>
    public string? Password { get; init; }

    /// <summary>The user name to authenticate with, when the server does not accept the address as one.</summary>
    public string? UserName { get; init; }

    /// <summary>Whose address generated mail is from, named after a <see cref="Generation.SyntheticAuthorIdentity" /> value.</summary>
    public string? Author { get; init; }
}
