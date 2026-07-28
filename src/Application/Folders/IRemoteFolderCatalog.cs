// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.Application.Synchronization;
using MailMcp.Domain.Accounts;
using MailMcp.Domain.Folders;
using MailMcp.Domain.Transport;

namespace MailMcp.Application.Folders;

/// <summary>Lists the folders a mail server advertises for one account.</summary>
/// <remarks>
/// The catalog is read-only by contract as well as by implementation: it exposes no operation that creates, renames,
/// subscribes to, or deletes a folder, and listing folders selects none, so no remote message flag can change while
/// discovery runs.
/// </remarks>
public interface IRemoteFolderCatalog
{
    /// <summary>Lists every folder the account's server advertises, together with the roles it reports for them.</summary>
    /// <param name="accountId">The local account whose server is listed.</param>
    /// <param name="transportSecurityPolicy">The connection and authentication policy the implementation must obey.</param>
    /// <param name="cancellationToken">Cancels connecting, authenticating, and listing.</param>
    /// <returns>The advertised folders, in the order the server reported them.</returns>
    /// <exception cref="MailboxUnavailableException">Thrown when the mail server did not serve the listing within its configured resilience budget.</exception>
    Task<IReadOnlyList<RemoteFolder>> ListFoldersAsync(
        MailAccountId accountId,
        MailTransportSecurityPolicy transportSecurityPolicy,
        CancellationToken cancellationToken);
}

/// <summary>Describes one folder as the mail server advertises it.</summary>
/// <param name="Path">The advertised path and the hierarchy delimiter reported with it.</param>
/// <param name="SpecialUses">The roles the server reports for the folder, which is empty when it reports none.</param>
public sealed record RemoteFolder(RemoteFolderPath Path, IReadOnlyList<MailFolderSpecialUse> SpecialUses);
