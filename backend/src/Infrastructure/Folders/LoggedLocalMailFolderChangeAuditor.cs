// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Folders.Local;
using Microsoft.Extensions.Logging;

namespace MailFathom.Infrastructure.Folders;

internal sealed partial class LoggedLocalMailFolderChangeAuditor(ILogger<LoggedLocalMailFolderChangeAuditor> logger)
    : ILocalMailFolderChangeAuditor
{
    public Task RecordAsync(LocalMailFolderChange change, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(change);

        this.LogLocalFolderChanged(
            change.Account.User.Value,
            change.Account.Id.Value,
            change.Folder.Value,
            change.Kind,
            change.ErasedFolderCount,
            change.OccurredAt);

        return Task.CompletedTask;
    }

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Local folder {FolderId} of account {UserId}/{AccountId} was changed ({ChangeKind}), erasing {ErasedFolderCount} folders, at {OccurredAt}.")]
    private partial void LogLocalFolderChanged(
        Guid userId,
        string accountId,
        Guid folderId,
        LocalMailFolderChangeKind changeKind,
        int erasedFolderCount,
        DateTimeOffset occurredAt);
}
