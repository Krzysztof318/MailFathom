// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;

namespace MailFathom.Host.Configuration.OwnerSettings.Administration;

/// <summary>One owner a deployment holds, as an administrator reads a roster.</summary>
/// <param name="Owner">The identifier every mail account and every stored message of theirs hangs on.</param>
/// <param name="DisplayName">The label an operator tells this owner apart by, which nothing resolves them by.</param>
/// <param name="RecordIsTheirOwn">Whether their mail accounts come from their own record rather than from a configuration source.</param>
/// <param name="Served">Whether this process is serving them, which a held owner no source declares is not.</param>
/// <remarks>
/// <para>
/// The label is here because a column of generated identifiers is not a roster anybody can read. Nothing resolves an
/// owner by it — every later act names the identifier — but choosing which owner to act on is what an administrator
/// does first, and the identifier says nothing about who the person is.
/// </para>
/// <para>
/// The last two are separate facts and a reader needs both. An owner held and not served keeps every message of theirs
/// and synchronizes none of it, which is what a file that stopped declaring them leaves behind; an owner served from a
/// configuration source has an empty record, which is what makes a write to it something to refuse rather than apply.
/// </para>
/// </remarks>
internal sealed record OwnerRosterEntry(
    MailOwnerId Owner,
    string DisplayName,
    bool RecordIsTheirOwn,
    bool Served);
