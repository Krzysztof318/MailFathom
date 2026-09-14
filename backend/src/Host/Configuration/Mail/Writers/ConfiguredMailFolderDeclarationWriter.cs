// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Application.Folders;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Failures;
using MailFathom.Domain.Folders;
using MailFathom.Host.Configuration.UserSettings.Administration;

namespace MailFathom.Host.Configuration.Mail.Writers;

/// <summary>Writes what one of the signed-in user's accounts declares about its own folders, into that user's record.</summary>
/// <remarks>
/// <para>
/// The account's record is the one configuration layer a person may change, so it is where an act carried out on the
/// mail server is written down. Everything the three folder routes of an account's own settings are held to holds here
/// as well — <see cref="MailAccountFolderComposition" /> is the same composer — which is what keeps the
/// folder-management surface from being the way around the rules those routes apply.
/// </para>
/// <para>
/// A refusal here is the one case where the server and MailFathom end up disagreeing, because the server has already
/// acted by the time a declaration is written. It is reported as <see cref="MailFolderActRefusal.NotRecorded" /> rather
/// than swallowed, so the person is told the folder is not as MailFathom describes it.
/// </para>
/// </remarks>
/// <param name="accounts">The mail accounts of the signed-in user, which the declaration is written through.</param>
[SuppressMessage(
    "Performance",
    "CA1812:Avoid uninstantiated internal classes",
    Justification = "The dependency injection container materializes this adapter.")]
internal sealed class ConfiguredMailFolderDeclarationWriter(MailAccountAdministration accounts)
    : IMailFolderDeclarationWriter
{
    /// <summary>The sentence a change matching no declared folder is refused with, which the caller reads as a missing folder.</summary>
    private const string FolderMissing = "This mail account declares no such folder.";

    /// <inheritdoc />
    public async Task<IReadOnlySet<MailFolderAlias>> AliasesTheAccountDeclaresAsync(
        MailAccountId accountId,
        CancellationToken cancellationToken)
    {
        var declared = await accounts.AliasesOwnAccountDeclaresAsync(accountId.Value, cancellationToken);

        return declared
            .Select(static alias => MailFolderAlias.TryCreate(alias, out var parsed) ? parsed : (MailFolderAlias?)null)
            .OfType<MailFolderAlias>()
            .ToHashSet();
    }

    /// <inheritdoc />
    public Task<MailFolderDeclarationOutcome> DeclareAsync(
        MailAccountId accountId,
        MailFolderAlias folderAlias,
        RemoteFolderPath path,
        MailFolderSpecialUse? role,
        CancellationToken cancellationToken) =>
        this.WriteAsync(
            accountId,
            document => MailAccountFolderComposition.WithFolderDeclared(
                document,
                folderAlias.Value,
                path.Value,
                role?.ToString()),
            unmatched: null,
            cancellationToken);

    /// <inheritdoc />
    public Task<MailFolderDeclarationOutcome> RepointAsync(
        MailAccountId accountId,
        MailFolderAlias folderAlias,
        RemoteFolderPath path,
        CancellationToken cancellationToken) =>
        this.WriteAsync(
            accountId,
            document => MailAccountFolderComposition.WithFolderRepointed(document, folderAlias.Value, path.Value),
            FolderMissing,
            cancellationToken);

    /// <inheritdoc />
    public Task<MailFolderDeclarationOutcome> WithdrawAsync(
        MailAccountId accountId,
        MailFolderAlias folderAlias,
        CancellationToken cancellationToken) =>
        this.WriteAsync(
            accountId,
            document => MailAccountFolderComposition.WithFolderRemoved(document, folderAlias.Value),
            FolderMissing,
            cancellationToken);

    private async Task<MailFolderDeclarationOutcome> WriteAsync(
        MailAccountId accountId,
        Func<string, MailAccountFolderChange> compose,
        string? unmatched,
        CancellationToken cancellationToken)
    {
        var outcome = await accounts.ChangeOwnFolderDeclarationAsync(
            accountId.Value,
            compose,
            unmatched,
            cancellationToken);

        return outcome switch
        {
            null => MailFolderDeclarationOutcome.Refused(MailFolderActRefusal.AccountMissing),
            { IsSettled: true } => MailFolderDeclarationOutcome.Committed,
            _ when Names(outcome, unmatched) => MailFolderDeclarationOutcome.Refused(MailFolderActRefusal.FolderMissing),
            _ => MailFolderDeclarationOutcome.Refused(MailFolderActRefusal.NotRecorded),
        };
    }

    /// <summary>Reports whether a refusal is the one saying the account declares no such folder.</summary>
    /// <remarks>Read off the sentence the composer was given, because a folder that is gone and a candidate the binder would not take are one error code apart and mean different things to the person who asked.</remarks>
    private static bool Names(UserRecordWriteOutcome outcome, string? unmatched) =>
        unmatched is not null
        && outcome.Refusal == MailFathomErrorCode.ConfigurationCandidateInvalid
        && outcome.Messages.Contains(unmatched, StringComparer.Ordinal);
}
