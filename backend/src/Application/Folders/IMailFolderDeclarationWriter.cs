// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Accounts;
using MailFathom.Domain.Folders;

namespace MailFathom.Application.Folders;

/// <summary>Writes what an account declares about its own folders: which ones it reads, and where each of them is.</summary>
/// <remarks>
/// <para>
/// A mirrored account's folders are declarations rather than rows, so an act on one is a change to what the account
/// says about itself. <see cref="IMailFolderMappingReader" /> answers what the whole of configuration currently
/// declares, composed from every source; this writes the one layer a person may change, which is the account's own
/// record. What an operator wrote in their own file is theirs and is refused here, which is
/// <see cref="MailFolderActRefusal.NotDeclaredByTheAccount" />.
/// </para>
/// <para>
/// A declaration is written only once the mail server has carried out the act it describes. That ordering is the whole
/// of the <em>never half an act</em> rule: a refused server leaves nothing written, so no alias is ever left resolving
/// to a folder the server never made, which is the state a mistyped path is supposed to be the only cause of.
/// </para>
/// <para>
/// The alias is stable across every act. A rename and a move change where the folder is, never what MailFathom calls
/// it, because the alias is what the stored mail, the checkpoints, and the bindings are keyed by — moving it would
/// separate a folder from its own mail to make a tree read more tidily.
/// </para>
/// </remarks>
public interface IMailFolderDeclarationWriter
{
    /// <summary>Reads the aliases the account's own record declares, which are the folders this surface may act on.</summary>
    /// <param name="accountId">The account.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>The aliases, empty where the record declares none or names no such account.</returns>
    /// <remarks>Asked by the reader that publishes which acts a folder allows, so a folder an operator fixed is reported as allowing none rather than refusing the act after the person has asked for it.</remarks>
    Task<IReadOnlySet<MailFolderAlias>> AliasesTheAccountDeclaresAsync(
        MailAccountId accountId,
        CancellationToken cancellationToken);

    /// <summary>Declares one more folder of the account, at a path the mail server now advertises.</summary>
    /// <param name="accountId">The account.</param>
    /// <param name="folderAlias">The alias to declare it under, which nothing afterwards changes.</param>
    /// <param name="path">The path the server advertises the folder at.</param>
    /// <param name="role">The role the folder plays, or <see langword="null" /> for an ordinary folder.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>What the write did.</returns>
    Task<MailFolderDeclarationOutcome> DeclareAsync(
        MailAccountId accountId,
        MailFolderAlias folderAlias,
        RemoteFolderPath path,
        MailFolderSpecialUse? role,
        CancellationToken cancellationToken);

    /// <summary>Points a folder the account already declares at the path it has been renamed or moved to.</summary>
    /// <param name="accountId">The account.</param>
    /// <param name="folderAlias">The alias the folder is declared under, which this does not change.</param>
    /// <param name="path">The path the server now advertises the folder at.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>What the write did.</returns>
    Task<MailFolderDeclarationOutcome> RepointAsync(
        MailAccountId accountId,
        MailFolderAlias folderAlias,
        RemoteFolderPath path,
        CancellationToken cancellationToken);

    /// <summary>Withdraws a folder from what the account declares, which takes it out of everything MailFathom reads.</summary>
    /// <param name="accountId">The account.</param>
    /// <param name="folderAlias">The alias the folder is declared under.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>What the write did.</returns>
    /// <remarks>
    /// A withdrawn folder leaves the tree, leaves what the account synchronizes, and leaves every mailbox query,
    /// because a folder is readable by being declared rather than by not being mentioned. Whether the mail stored from
    /// it is also removed is the account's deletion setting and is decided by the caller, not here.
    /// </remarks>
    Task<MailFolderDeclarationOutcome> WithdrawAsync(
        MailAccountId accountId,
        MailFolderAlias folderAlias,
        CancellationToken cancellationToken);
}
