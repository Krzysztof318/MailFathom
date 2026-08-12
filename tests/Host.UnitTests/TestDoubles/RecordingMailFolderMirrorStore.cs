// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Folders;
using MailFathom.Application.Persistence;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Folders;

namespace MailFathom.Host.UnitTests.TestDoubles;

/// <summary>Records which folders a run asked to have erased, and answers with an erasure the test arranged.</summary>
internal sealed class RecordingMailFolderMirrorStore(MailFolderMirrorErasure? erasure = null)
    : IStoredMailFolderMirrorStore
{
    private readonly List<MailFolderIdentity> erasedFolders = [];
    private readonly TaskCompletionSource firstErasureReached = new();

    /// <summary>Gets the folders each pass named, in the order the run reached them.</summary>
    internal IReadOnlyList<MailFolderIdentity> ErasedFolders
    {
        get
        {
            lock (this.erasedFolders)
            {
                return [.. this.erasedFolders];
            }
        }
    }

    /// <summary>Gets a signal a run raises once it has reached the erasure pass, which is after every folder it scheduled.</summary>
    internal Task FirstErasureReached => this.firstErasureReached.Task;

    /// <inheritdoc />
    public Task<MailFolderMirrorErasure> EraseFolderMirrorAsync(
        IPersistenceSession session,
        MailAccountId accountId,
        MailFolderAlias folderAlias,
        int maxEmails,
        CancellationToken cancellationToken)
    {
        lock (this.erasedFolders)
        {
            this.erasedFolders.Add(new MailFolderIdentity(accountId, folderAlias));
        }

        this.firstErasureReached.TrySetResult();

        return Task.FromResult(erasure ?? MailFolderMirrorErasure.Nothing);
    }
}
