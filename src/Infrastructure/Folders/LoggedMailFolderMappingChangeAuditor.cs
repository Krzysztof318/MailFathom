// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.Application.Folders;
using Microsoft.Extensions.Logging;

namespace MailMcp.Infrastructure.Folders;

/// <summary>Writes mapping changes to the structured log as the deployment's audit record.</summary>
/// <remarks>
/// This is the one place in MailMcp that writes a remote folder path outside the database. It is deliberate: an
/// operator who repointed an alias needs both paths to recognize the change, and a folder that resynchronizes from
/// the beginning is otherwise unexplained. Everything else — every ordinary synchronization log line — names the
/// alias only. A durable audit store replaces this implementation without any caller changing.
/// </remarks>
internal sealed partial class LoggedMailFolderMappingChangeAuditor(ILogger<LoggedMailFolderMappingChangeAuditor> logger)
    : IMailFolderMappingChangeAuditor
{
    /// <inheritdoc />
    public Task RecordMappingChangeAsync(MailFolderMappingChange change, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(change);

        if (change.PreviousRemotePath is { } previousRemotePath)
        {
            this.LogFolderAliasRepointed(
                change.AccountId.Value,
                change.Alias.Value,
                previousRemotePath.Value,
                change.NewRemotePath.Value,
                change.Generation.Value,
                change.OccurredAt);
        }
        else
        {
            this.LogFolderAliasBound(
                change.AccountId.Value,
                change.Alias.Value,
                change.NewRemotePath.Value,
                change.Generation.Value,
                change.OccurredAt);
        }

        return Task.CompletedTask;
    }

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Folder alias {AccountId}/{FolderAlias} was bound to remote folder {NewRemotePath} as resolution generation {ResolutionGeneration} at {OccurredAt}.")]
    private partial void LogFolderAliasBound(
        string accountId,
        string folderAlias,
        string newRemotePath,
        int resolutionGeneration,
        DateTimeOffset occurredAt);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Folder alias {AccountId}/{FolderAlias} was repointed from remote folder {PreviousRemotePath} to {NewRemotePath} at {OccurredAt}; resolution generation {ResolutionGeneration} starts without a checkpoint and synchronizes the new folder from its first UID.")]
    private partial void LogFolderAliasRepointed(
        string accountId,
        string folderAlias,
        string previousRemotePath,
        string newRemotePath,
        int resolutionGeneration,
        DateTimeOffset occurredAt);
}
