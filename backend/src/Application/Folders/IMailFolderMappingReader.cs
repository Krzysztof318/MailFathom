// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Accounts;
using MailFathom.Domain.Folders;

namespace MailFathom.Application.Folders;

/// <summary>Answers which folder of an account a name refers to, whether the name is an alias or a role.</summary>
/// <remarks>
/// <para>
/// The decision is configuration's and is read through a port for the reason every other per-account folder decision is:
/// the paths that need it are a rule's destination, a mailbox read's folder filter, and every feature that wants
/// <em>this account's junk folder</em>, and none of them may reach for a settings type of its own. Answering here is
/// what stops each such feature from inventing a configuration key naming a folder a mapping already names.
/// </para>
/// <para>
/// Both questions are answered over every mapping of the account, whatever the folders take part in. What a folder is
/// for is a different question from whether MailFathom keeps a copy of it, so a mapping carrying <c>Synchronize:
/// false</c> answers here exactly as any other does, and a condition asking which role a folder plays gets the same
/// answer whether or not a run has ever reached it. Filing into such a folder is refused elsewhere — a rule's
/// destination has to name a folder the account mirrors — and that refusal belongs to the rule rather than to this
/// answer, which would otherwise be two different answers to one question.
/// </para>
/// </remarks>
public interface IMailFolderMappingReader
{
    /// <summary>Gets the folder of an account that plays a role.</summary>
    /// <param name="accountId">The account the folder belongs to.</param>
    /// <param name="role">The role the folder plays.</param>
    /// <returns>
    /// The mapping configuration labelled with that role, or <see langword="null" /> when the account maps no such
    /// folder. Nothing is guessed in that case: neither the inbox, nor the first mapping, nor a folder whose name
    /// resembles the role, because a feature filing mail into a folder nobody nominated is worse than one reporting that
    /// it has nowhere to file it.
    /// </returns>
    MailFolderMapping? FindFolderPlayingRole(MailAccountId accountId, MailFolderSpecialUse role);

    /// <summary>Gets the folder of an account configuration gave an alias.</summary>
    /// <param name="accountId">The account the folder belongs to.</param>
    /// <param name="folderAlias">MailFathom's own name for the folder.</param>
    /// <returns>The mapping, or <see langword="null" /> when configuration no longer names that alias.</returns>
    MailFolderMapping? FindFolderNamed(MailAccountId accountId, MailFolderAlias folderAlias);
}
