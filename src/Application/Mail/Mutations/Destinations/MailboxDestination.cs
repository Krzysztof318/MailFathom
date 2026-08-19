// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Folders;

namespace MailFathom.Application.Mail.Mutations.Destinations;

/// <summary>The folder a mutation files into, as the account's server currently holds it.</summary>
/// <param name="Binding">The alias binding the folder is currently resolved under.</param>
/// <param name="IsMirrored">Whether the account mirrors the folder's mail.</param>
/// <remarks>
/// <para>
/// The binding rather than the path alone, because the three things that need a destination need different parts of it
/// and none of them can be derived from another at the point of use. The request carries the path; the rule history
/// names the alias, which a rule written against a role never spelled out; and a caller that has to open a write
/// session on the folder needs the whole binding, since a session's occurrence scope is the generation the binding
/// carries.
/// </para>
/// <para>
/// Whether the folder is mirrored stays a separate answer, because it is a property of the mapping rather than of the
/// binding. It is what decides the local disposition a relocation is authored with: a message moved somewhere
/// MailFathom keeps no copy of has left the mirrored mailbox for good.
/// </para>
/// </remarks>
public sealed record MailboxDestination(MailFolderResolution Binding, bool IsMirrored)
{
    /// <summary>Gets MailFathom's own name for the folder, which is what a history line and a refusal name it by.</summary>
    public MailFolderAlias Alias => this.Binding.Alias;

    /// <summary>Gets where the folder is on the server, which is what an IMAP command is issued against.</summary>
    public RemoteFolderPath Path => this.Binding.RemotePath;
}
