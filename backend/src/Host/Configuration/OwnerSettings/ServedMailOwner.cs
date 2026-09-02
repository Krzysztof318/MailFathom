// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;
using MailFathom.Host.Configuration.Mail;
using MailFathom.Host.Configuration.SensitiveContent;
using MailFathom.Host.Configuration.Spam;

namespace MailFathom.Host.Configuration.OwnerSettings;

/// <summary>One owner this deployment serves, and where what it serves them from was read.</summary>
/// <param name="Owner">The identity every mail account and every stored message of theirs hangs on.</param>
/// <param name="DisplayName">The label an operator tells this owner apart by.</param>
/// <param name="Source">Where this owner's mail accounts are read from.</param>
/// <param name="MailAccounts">The owner's mail accounts, where this record is what holds them.</param>
/// <param name="SpamClassification">
/// How this owner's own mail is classified, where their document is what decides it, and <see langword="null" /> where
/// a configuration source still does. Absence is what says *read the deployment's section for this owner* rather than
/// an owner who classifies nothing, which is why it is nullable and not an empty block.
/// </param>
/// <param name="SensitiveContent">What this owner asks to have their own mail scanned for, or nothing where they asked for nothing.</param>
/// <remarks>
/// The accounts are empty for an owner whose source is <see cref="MailOwnerAccountSource.DeploymentSection" />, and
/// deliberately so: those declarations are in the reloadable mail snapshot, which is where a reload of the file has to
/// be able to reach them. Copying them here would freeze a deployment's existing shape at the start that read it, so
/// what this record carries is what the snapshot cannot — an owner's own declared section, and the document of an
/// owner who has taken their record over.
/// <para>
/// The scanning block is absent for the sole owner a deployment serves from its own section, who has no record of their
/// own to state one in and therefore reads the deployment's posture. Absence and a block that asks for nothing compose
/// to the same answer, so nothing downstream tells them apart.
/// </para>
/// </remarks>
internal sealed record ServedMailOwner(
    MailOwnerId Owner,
    string DisplayName,
    MailOwnerAccountSource Source,
    IReadOnlyList<MailSynchronizationAccountOptions> MailAccounts,
    OwnerSpamClassificationOptions? SpamClassification = null,
    OwnerSensitiveContentOptions? SensitiveContent = null)
{
    /// <summary>Gets whether a configuration source still reaches this owner's mail accounts.</summary>
    /// <remarks>
    /// False from the moment an adoption writes their document, and permanently: their accounts have stopped being
    /// configuration keys rather than merely losing precedence, so neither the file nor an environment variable nor a
    /// command-line argument reaches them afterwards.
    /// </remarks>
    public bool ReadFromConfiguration => this.Source != MailOwnerAccountSource.OwnerDocument;
}
