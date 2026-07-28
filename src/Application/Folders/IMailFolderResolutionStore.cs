// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.Application.Persistence;
using MailMcp.Domain.Accounts;
using MailMcp.Domain.Folders;

namespace MailMcp.Application.Folders;

/// <summary>Persists which remote folder each alias is currently bound to.</summary>
/// <remarks>
/// A binding has to be durable before anything is synchronized under it, because the generation it carries is what
/// separates the occurrences and the checkpoint of one binding from those of the next.
/// </remarks>
public interface IMailFolderResolutionStore
{
    /// <summary>Gets the newest durable binding of an alias.</summary>
    /// <param name="accountId">The account owning the alias.</param>
    /// <param name="folderAlias">The operator-facing folder name.</param>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    /// <returns>The highest-generation binding, or <see langword="null" /> when the alias has never been bound.</returns>
    Task<MailFolderResolution?> GetCurrentResolutionAsync(
        MailAccountId accountId,
        MailFolderAlias folderAlias,
        CancellationToken cancellationToken);

    /// <summary>Stages a binding so the generation exists before any occurrence is stored under it.</summary>
    /// <param name="session">The open session whose transaction the staged insert joins.</param>
    /// <param name="accountId">The account owning the alias.</param>
    /// <param name="resolution">The binding to stage.</param>
    /// <param name="cancellationToken">Cancels the lookup before anything is staged.</param>
    /// <returns>A task that completes once the binding is staged in the caller's session.</returns>
    /// <remarks>
    /// Staging a binding that is already durable is a no-op rather than a conflict, so a run that resolves the same
    /// alias to the same remote folder writes nothing.
    /// </remarks>
    Task SaveResolutionAsync(
        IPersistenceSession session,
        MailAccountId accountId,
        MailFolderResolution resolution,
        CancellationToken cancellationToken);
}
