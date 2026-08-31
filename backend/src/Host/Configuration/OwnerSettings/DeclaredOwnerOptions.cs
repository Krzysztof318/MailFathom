// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using MailFathom.Host.Configuration.Mail;
using MailFathom.Host.Configuration.SensitiveContent;

namespace MailFathom.Host.Configuration.OwnerSettings;

/// <summary>One owner a deployment declares in configuration, as an element of the top-level <c>Accounts</c> collection.</summary>
/// <remarks>
/// <para>
/// It is the file's half of what <see cref="OwnerAccountOptions" /> is the row's half of. The content is the same
/// pair of properties — the owner's mail-account declarations and what they ask to have their mail scanned for — and
/// the two properties in front of them are the relational envelope a file has to state because nothing about a file
/// could derive one: the identifier every mail account, every stored message, and every job of theirs hangs on, and the
/// label an administrator tells owners apart by.
/// </para>
/// <para>
/// The identifier is the operator's rather than MailFathom's. Nothing in a file can generate one that would be the
/// same across restarts and across replicas, and inventing one per start would attach a deployment's stored mail to a
/// person who existed for one process. So it is stated, it is a version 4 UUID, and a declaration that changes it for
/// an owner the database already holds stops the start rather than orphaning their mail.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The configuration binder materializes this type when the owner collection is read.")]
internal sealed class DeclaredOwnerOptions
{
    /// <summary>The configuration section the owner collection is read from, which is a top-level one.</summary>
    /// <remarks>
    /// Deliberately not <c>MailSynchronization:Accounts</c>. That collection is the deployment's own mailbox
    /// declarations and names no owner; this one is the collection of people, each carrying their own mailboxes.
    /// </remarks>
    public const string SectionName = "Accounts";

    /// <summary>Gets or sets the identifier this owner is declared under, written as a UUID.</summary>
    /// <remarks>
    /// The attribute states what a start already refuses — a declaration carrying no identifier does not start — for
    /// the reader that cannot run one: the published configuration surface derives whether a key is required from it,
    /// and this collection is read by hand rather than through the validated options pipeline.
    /// </remarks>
    [Required]
    public string Id { get; set; } = string.Empty;

    /// <summary>Gets or sets the label this owner is told apart by, which is unique across the deployment.</summary>
    /// <remarks>Required for the reason <see cref="Id" /> is, and refused by the same rules.</remarks>
    [Required]
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Gets or sets the mail accounts this owner owns, which may be none.</summary>
    /// <remarks>
    /// Zero is an ordinary state rather than an unfinished one: an owner is declared before their first mailbox is,
    /// and one whose last mailbox is withdrawn is still an owner. The property is named as the persisted record names
    /// it, so a path reads <c>Accounts:0:MailAccounts:0</c> and says which of the two collections each segment is —
    /// and so an adoption materializes what the file supplied under the name the document already uses.
    /// </remarks>
    public List<MailSynchronizationAccountOptions> MailAccounts { get; set; } = [];

    /// <summary>Gets what this owner's mail is scanned for, within what the deployment provides.</summary>
    /// <remarks>
    /// The same block an adopted record carries, judged by the same rules, so an operator writing a posture into the
    /// file and an owner writing one into their own record are refused for the same reasons in the same words. A
    /// deployment that states nothing here scans this owner's mail exactly as it scans everybody's.
    /// </remarks>
    public OwnerSensitiveContentOptions SensitiveContent { get; } = new();
}
