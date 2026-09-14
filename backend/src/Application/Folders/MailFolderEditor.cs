// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Folders.Local;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Folders;

namespace MailFathom.Application.Folders;

/// <summary>Creates, renames, moves, and deletes an account's folders, whichever copy of its mailbox is the truth.</summary>
/// <remarks>
/// <para>
/// This is the one folder-management use case, and which of the two editors beneath it carries an act is decided here
/// from the account's phase rather than by whoever asked. A caller states the act and the folder and is told what
/// happened; nothing it receives says where the mail is kept, which is what keeps a client from branching on the
/// storage mode and then being wrong about an account that changed mode since.
/// </para>
/// <para>
/// A folder is named by an identity whose shape is not part of the contract. On an account whose mailbox MailFathom
/// holds it is the local folder's own identity; on a mirrored one it is the alias the folder is declared under. Both
/// are read as text, and text that is neither is answered as a folder the account does not have rather than as a
/// malformed request — a caller can only have got such a value from this surface.
/// </para>
/// <para>
/// An account whose mailbox is being restored to its source allows nothing. Its local folders are still the truth and
/// its source is being filled back up from them, so an act on either side would be an act against a mailbox that is
/// half in each place; the acts come back when the restore ends.
/// </para>
/// </remarks>
public sealed class MailFolderEditor
{
    private readonly LocalMailFolderEditor local;
    private readonly MirroredMailFolderEditor mirrored;
    private readonly AccessAuthorization authorization;

    /// <summary>Initializes a new instance of the <see cref="MailFolderEditor" /> class.</summary>
    /// <param name="local">Acts on the folders of an account whose mailbox MailFathom holds.</param>
    /// <param name="mirrored">Acts on the folders of an account whose mail server is the truth.</param>
    /// <param name="authorization">Answers which user the caller acts for.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    public MailFolderEditor(
        LocalMailFolderEditor local,
        MirroredMailFolderEditor mirrored,
        AccessAuthorization authorization)
    {
        ArgumentNullException.ThrowIfNull(local);
        ArgumentNullException.ThrowIfNull(mirrored);
        ArgumentNullException.ThrowIfNull(authorization);

        this.local = local;
        this.mirrored = mirrored;
        this.authorization = authorization;
    }

    /// <summary>Reads the account's folders and which acts each of them and the account itself allow.</summary>
    /// <param name="account">The caller's account.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>The folders and their acts, or <see langword="null" /> where the caller holds no such account.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller does not hold <c>mailfathom.mail.read</c>.</exception>
    public async Task<MailFolderManagement?> ReadAsync(MailAccountId account, CancellationToken cancellationToken)
    {
        var holding = await this.local.ReadAsync(account, cancellationToken);

        if (holding is null)
        {
            return null;
        }

        return holding.Phase is MailAccountCustodyPhase.Mirrored
            ? await this.mirrored.ReadAsync(account, cancellationToken)
            : DescribeHeld(holding);
    }

    /// <summary>Creates a folder, either by naming it or by naming the role it is to play.</summary>
    /// <param name="account">The caller's account.</param>
    /// <param name="parentId">The folder to create it beneath, or <see langword="null" /> for the top of the hierarchy.</param>
    /// <param name="name">The folder's name, which a creation naming a role does not need.</param>
    /// <param name="role">The role the folder is to play, or <see langword="null" /> for an ordinary folder.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>The created folder, or the refusal.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller does not hold <c>mailfathom.mail.folders.write</c> and <c>mailfathom.mail.read</c>, both of which an act needs.</exception>
    public Task<MailFolderActOutcome> CreateAsync(
        MailAccountId account,
        string? parentId,
        string? name,
        MailFolderSpecialUse? role,
        CancellationToken cancellationToken) =>
        this.ActAsync(
            account,
            holding => this.CreateLocallyAsync(account, holding, parentId, name, role, cancellationToken),
            identity => TryReadParentAlias(parentId, out var parent)
                ? this.mirrored.CreateAsync(identity, parent, name, role, cancellationToken)
                : Task.FromResult(MailFolderActOutcome.Refused(MailFolderActRefusal.ParentMissing)),
            cancellationToken);

    /// <summary>Renames a folder, leaving it where it is.</summary>
    /// <param name="account">The caller's account.</param>
    /// <param name="folderId">The folder.</param>
    /// <param name="name">The new name.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>The renamed folder, or the refusal.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller does not hold <c>mailfathom.mail.folders.write</c> and <c>mailfathom.mail.read</c>, both of which an act needs.</exception>
    public Task<MailFolderActOutcome> RenameAsync(
        MailAccountId account,
        string? folderId,
        string? name,
        CancellationToken cancellationToken) =>
        this.ActOnFolderAsync(
            account,
            folderId,
            folder => this.local.RenameAsync(account, folder, name, cancellationToken),
            (identity, alias) => this.mirrored.RenameAsync(identity, alias, name, cancellationToken),
            cancellationToken);

    /// <summary>Moves a folder, with everything beneath it, to another place in the hierarchy.</summary>
    /// <param name="account">The caller's account.</param>
    /// <param name="folderId">The folder.</param>
    /// <param name="parentId">The folder to move it beneath, or <see langword="null" /> for the top of the hierarchy.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>The moved folder, or the refusal.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller does not hold <c>mailfathom.mail.folders.write</c> and <c>mailfathom.mail.read</c>, both of which an act needs.</exception>
    public Task<MailFolderActOutcome> MoveAsync(
        MailAccountId account,
        string? folderId,
        string? parentId,
        CancellationToken cancellationToken) =>
        this.ActOnFolderAsync(
            account,
            folderId,
            folder => TryReadParent(parentId, out var parent)
                ? this.local.MoveAsync(account, folder, parent, cancellationToken)
                : Task.FromResult(LocalMailFolderEditOutcome.Refused(MailFolderActRefusal.ParentMissing)),
            (identity, alias) => TryReadParentAlias(parentId, out var parent)
                ? this.mirrored.MoveAsync(identity, alias, parent, cancellationToken)
                : Task.FromResult(MailFolderActOutcome.Refused(MailFolderActRefusal.ParentMissing)),
            cancellationToken);

    /// <summary>Deletes a folder, which means whatever the account's own rules make it mean.</summary>
    /// <param name="account">The caller's account.</param>
    /// <param name="folderId">The folder.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>The folder as the act left it, or the refusal.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller does not hold <c>mailfathom.mail.folders.write</c> and <c>mailfathom.mail.read</c>, both of which an act needs.</exception>
    /// <remarks>
    /// On an account whose mailbox MailFathom holds, a deletion moves the folder into the trash and a deletion of one
    /// already there erases it with its mail. On a mirrored account it deletes the folder on the mail server, or marks
    /// it deleted here, according to the account's own deletion setting. The change reported says which of them it was.
    /// </remarks>
    public Task<MailFolderActOutcome> DeleteAsync(
        MailAccountId account,
        string? folderId,
        CancellationToken cancellationToken) =>
        this.ActOnFolderAsync(
            account,
            folderId,
            folder => this.local.DeleteAsync(account, folder, cancellationToken),
            (identity, alias) => this.mirrored.DeleteAsync(identity, alias, cancellationToken),
            cancellationToken);

    private async Task<MailFolderActOutcome> ActAsync(
        MailAccountId account,
        Func<LocalMailFolderHolding, Task<MailFolderActOutcome>> held,
        Func<MailAccountId, Task<MailFolderActOutcome>> mirroredAct,
        CancellationToken cancellationToken)
    {
        this.authorization.RequirePermission(MailFathomPermission.MailFoldersWrite);

        var holding = await this.local.ReadAsync(account, cancellationToken);

        return holding switch
        {
            null => MailFolderActOutcome.Refused(MailFolderActRefusal.AccountMissing),
            { Phase: MailAccountCustodyPhase.Mirrored } => await mirroredAct(account),
            { Phase: MailAccountCustodyPhase.Held } => await held(holding),
            _ => MailFolderActOutcome.Refused(MailFolderActRefusal.AccountNotHeld),
        };
    }

    private Task<MailFolderActOutcome> ActOnFolderAsync(
        MailAccountId account,
        string? folderId,
        Func<LocalMailFolderId, Task<LocalMailFolderEditOutcome>> held,
        Func<MailAccountId, MailFolderAlias, Task<MailFolderActOutcome>> mirroredAct,
        CancellationToken cancellationToken) =>
        this.ActAsync(
            account,
            async holding => LocalFolderOf(folderId) is { } folder
                ? Describe(await held(folder), holding)
                : MailFolderActOutcome.Refused(MailFolderActRefusal.FolderMissing),
            async identity => AliasOf(folderId) is { } alias
                ? await mirroredAct(identity, alias)
                : MailFolderActOutcome.Refused(MailFolderActRefusal.FolderMissing),
            cancellationToken);

    private async Task<MailFolderActOutcome> CreateLocallyAsync(
        MailAccountId account,
        LocalMailFolderHolding holding,
        string? parentId,
        string? name,
        MailFolderSpecialUse? role,
        CancellationToken cancellationToken)
    {
        // Every role a held account has a folder for is supplied the moment its hierarchy is read, so a creation naming
        // one is asking for a folder that is already there rather than for something this could make.
        if (role is not null)
        {
            return MailFolderActOutcome.Refused(MailFolderActRefusal.RoleAlreadyPlayed);
        }

        if (!TryReadParent(parentId, out var parent))
        {
            return MailFolderActOutcome.Refused(MailFolderActRefusal.ParentMissing);
        }

        return Describe(await this.local.CreateAsync(account, parent, name, cancellationToken), holding);
    }

    private static MailFolderManagement DescribeHeld(LocalMailFolderHolding holding)
    {
        var folders = holding.Folders;
        var mayCreate = holding.Phase is MailAccountCustodyPhase.Held
            && folders.Count < LocalMailFolderTree.MaximumFolders;

        return new MailFolderManagement(
            mayCreate ? [MailFolderAct.Create] : [],
            // A held account is given every role it needs the moment its hierarchy is read, and an archive there is an
            // ordinary folder somebody makes, so no role is ever waiting to be created.
            [],
            [
                .. folders
                    .Select(folder => Describe(folder, holding.Phase))
                    .OrderBy(static folder => folder.Name, StringComparer.OrdinalIgnoreCase),
            ]);
    }

    private static ManagedMailFolder Describe(LocalMailFolder folder, MailAccountCustodyPhase phase) => new(
        folder.Id.Value.ToString(),
        folder.ParentId?.Value.ToString(),
        folder.Name.Value,
        folder.Role,
        folder.IsProtected || phase is not MailAccountCustodyPhase.Held
            ? []
            : [MailFolderAct.Rename, MailFolderAct.Move, MailFolderAct.Delete]);

    private static MailFolderActOutcome Describe(LocalMailFolderEditOutcome outcome, LocalMailFolderHolding holding) =>
        outcome is { Folder: { } folder, Kind: { } kind }
            ? new MailFolderActOutcome(Describe(folder, holding.Phase), kind, Refusal: null, outcome.MailErasureDeferred)
            : MailFolderActOutcome.Refused(
                outcome.Refusal ?? throw new InvalidOperationException("An edit outcome carried neither a folder nor a refusal."));

    // Absence of a parent is what names the top of the hierarchy, so text that names no folder must not be read as it:
    // the folder would be created or moved somewhere the request never asked for. The two readings differ only in what
    // an identity looks like on each side — an identifier a held account issued, an alias a mirrored one declares.
    private static bool TryReadParent(string? parentId, out LocalMailFolderId? parent)
    {
        parent = LocalFolderOf(parentId);

        return parent is not null || string.IsNullOrWhiteSpace(parentId);
    }

    private static bool TryReadParentAlias(string? parentId, out MailFolderAlias? parent)
    {
        parent = AliasOf(parentId);

        return parent is not null || string.IsNullOrWhiteSpace(parentId);
    }

    private static LocalMailFolderId? LocalFolderOf(string? folderId) =>
        Guid.TryParse(folderId, out var parsed) && parsed != Guid.Empty ? LocalMailFolderId.Create(parsed) : null;

    private static MailFolderAlias? AliasOf(string? folderId) =>
        MailFolderAlias.TryCreate(folderId, out var alias) ? alias : null;
}
