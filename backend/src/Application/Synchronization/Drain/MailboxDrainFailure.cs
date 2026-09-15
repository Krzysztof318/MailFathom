// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Synchronization.Drain;

/// <summary>Why one drain batch's commands did not reach the source.</summary>
/// <remarks>
/// A held account whose source never empties is the one failure here an operator has to diagnose from counts alone, so
/// the count says which kind rather than only how many: a source that will never serve the commands, one that refused
/// the credential, and one that was merely busy are three different things to do about it, and a single figure makes
/// them one. None of these names a message.
/// </remarks>
public enum MailboxDrainFailure
{
    /// <summary>The source did not serve the commands within its resilience budget, or the transport failed.</summary>
    /// <remarks>The ordinary one, and the only one the next run is expected to get past on its own.</remarks>
    SourceUnavailable = 0,

    /// <summary>The source advertises no message-scoped expunge, so no batch can ever be served.</summary>
    /// <remarks>
    /// Reachable only where the capability changed under an account already held, since it is established before the
    /// account moves. Repeating the batch cannot succeed until the source or its server changes.
    /// </remarks>
    SourceCannotExpungeOneMessage = 1,

    /// <summary>The source no longer holds the folder the batch names.</summary>
    /// <remarks>Synchronization is what resolves it, by rebinding the alias or reporting the folder as gone.</remarks>
    FolderMissing = 2,

    /// <summary>The batch failed for a reason this pass could not classify.</summary>
    SomethingElse = 3,

    /// <summary>The source refused the credential the account is reached under.</summary>
    /// <remarks>
    /// Told apart from a source that was merely busy because waiting does not clear it: an expired token, a revoked
    /// application password, or a credential the server stopped accepting keeps every later run failing the same way
    /// until somebody supplies a new one.
    /// </remarks>
    SourceRefusedTheCredential = 4,
}
