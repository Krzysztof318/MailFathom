// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;
using MailFathom.Host.Configuration.Mail;

namespace MailFathom.Host.Configuration.OwnerSettings;

/// <summary>One owner this deployment serves, and where what it serves them from was read.</summary>
/// <param name="Owner">The identity every mail account and every stored message of theirs hangs on.</param>
/// <param name="DisplayName">The label an operator tells this owner apart by.</param>
/// <param name="Source">Where this owner's mail accounts are read from.</param>
/// <param name="MailAccounts">The owner's mail accounts, where this record is what holds them.</param>
/// <remarks>
/// The accounts are empty for an owner whose source is <see cref="MailOwnerAccountSource.DeploymentSection" />, and
/// deliberately so: those declarations are in the reloadable mail snapshot, which is where a reload of the file has to
/// be able to reach them. Copying them here would freeze a deployment's existing shape at the start that read it, so
/// what this record carries is what the snapshot cannot — an owner's own declared section, and the document of an
/// owner who has taken their record over.
/// </remarks>
internal sealed record ServedMailOwner(
    MailOwnerId Owner,
    string DisplayName,
    MailOwnerAccountSource Source,
    IReadOnlyList<MailSynchronizationAccountOptions> MailAccounts)
{
    /// <summary>Gets whether a configuration source still reaches this owner's mail accounts.</summary>
    /// <remarks>
    /// False from the moment an adoption writes their document, and permanently: their accounts have stopped being
    /// configuration keys rather than merely losing precedence, so neither the file nor an environment variable nor a
    /// command-line argument reaches them afterwards.
    /// </remarks>
    public bool ReadFromConfiguration => this.Source != MailOwnerAccountSource.OwnerDocument;
}
