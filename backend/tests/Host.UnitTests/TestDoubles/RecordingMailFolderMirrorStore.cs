// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Folders;
using MailFathom.Application.Persistence;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Folders;

namespace MailFathom.Host.UnitTests.TestDoubles;

/// <summary>Records which folders a run asked to have erased, and answers with an erasure the test arranged.</summary>
/// <remarks>
/// Nothing in an account run reaches this store any more, because a folder whose synchronization was switched off keeps
/// what it stored. What the recording is for is proving exactly that: an empty list here is the assertion, and it is
/// worth making against a store a run could have reached rather than against one nothing is registered for.
/// </remarks>
internal sealed class RecordingMailFolderMirrorStore(MailFolderMirrorErasure? erasure = null)
    : IStoredMailFolderMirrorStore
{
    private readonly List<MailFolderIdentity> erasedFolders = [];

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

        return Task.FromResult(erasure ?? MailFolderMirrorErasure.Nothing);
    }
}
