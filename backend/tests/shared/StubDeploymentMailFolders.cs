// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Folders;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Folders;

namespace MailFathom.TestSupport;

/// <summary>Answers every deployment-wide folder set from the per-folder doubles a test already arranged.</summary>
/// <remarks>
/// Composed from <see cref="StubMailFolderParticipation" /> and <see cref="StubJunkMailFolderCatalog" /> rather than
/// arranged on its own, so the set a query narrows by and the per-folder answer a use case reads cannot disagree — which
/// is the property the account records give the deployed reader, both being read off the same folder rows. What spam
/// classification covers is stated by the spam settings double instead, so the two sets about it answer with nothing.
/// </remarks>
internal sealed class StubDeploymentMailFolders(
    StubMailFolderParticipation participation,
    StubJunkMailFolderCatalog junk)
    : IDeploymentMailFolders
{
    /// <summary>Gets the folders of a deployment that maps none.</summary>
    public static StubDeploymentMailFolders None => new(StubMailFolderParticipation.Nothing, StubJunkMailFolderCatalog.None);

    /// <summary>Builds the folder sets the given participation reads, with no junk folder.</summary>
    /// <param name="participation">What each mapped folder takes part in.</param>
    /// <returns>The folder sets.</returns>
    public static StubDeploymentMailFolders Of(StubMailFolderParticipation participation) =>
        new(participation, StubJunkMailFolderCatalog.None);

    /// <inheritdoc />
    public Task<IReadOnlyList<MailFolderIdentity>> ReadAsync(
        MailFolderSelection selection,
        CancellationToken cancellationToken) =>
        Task.FromResult(selection switch
        {
            MailFolderSelection.Mapped => participation.FoldersMapped,
            MailFolderSelection.Synchronized => participation.FoldersSynchronized,
            MailFolderSelection.VisibleToTools => participation.FoldersVisibleToTools,
            MailFolderSelection.GeneratingEmbeddings => participation.FoldersGeneratingEmbeddings,
            MailFolderSelection.Junk => junk.JunkFolders,
            MailFolderSelection.OfAccountsClassifyingSpam or MailFolderSelection.ClassifiedForSpam => [],
            _ => throw new ArgumentOutOfRangeException(nameof(selection), selection, "No folder set has that name."),
        });

    /// <inheritdoc />
    public async Task<IReadOnlyList<MailFolderIdentity>> ReadAsync(
        MailFolderSelection selection,
        IReadOnlyCollection<MailAccountId> accounts,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(accounts);

        return [.. (await this.ReadAsync(selection, cancellationToken)).Where(folder => accounts.Contains(folder.AccountId))];
    }
}
