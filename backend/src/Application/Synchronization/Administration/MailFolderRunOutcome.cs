// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Synchronization.Administration;

/// <summary>How one folder's turn through a synchronization run ended.</summary>
/// <remarks>
/// <para>
/// It is the classification a failing folder is read by, and it is deliberately a closed set of names rather than
/// anything the failure itself carried. An administrative surface must publish neither an exception message nor a
/// remote folder path, and what an operator acts on is which of these happened: two of them are corrected by editing a
/// folder mapping, two are waited out, one is a credential to replace, and one is a defect to report.
/// </para>
/// <para>
/// The members mirror the branches the supervisor already separates when it logs a folder, so a value here and the line
/// in a log describe the same event rather than two classifications that happen to agree.
/// </para>
/// </remarks>
public enum MailFolderRunOutcome
{
    /// <summary>The run reached the folder the alias is bound to and committed what it fetched.</summary>
    Synchronized = 0,

    /// <summary>The mail server advertised no folder matching the alias, so the folder was not synchronized.</summary>
    AliasUnresolved = 1,

    /// <summary>Several advertised folders matched the alias, so the folder was not synchronized until a remote path says which one it means.</summary>
    AliasAmbiguous = 2,

    /// <summary>Another writer moved the folder's progress while the run was deciding from it, so the run gave the folder up to the next one.</summary>
    DeferredAfterConcurrencyConflict = 3,

    /// <summary>The mail server did not serve the folder within the account's resilience budget.</summary>
    DeferredAfterMailServerUnavailable = 4,

    /// <summary>The folder's run ended in a way nothing here classifies, which is a defect rather than a condition to wait out.</summary>
    UnexpectedFailure = 5,

    /// <summary>The host began shutting down while the folder was running, so the run ended without finishing.</summary>
    InterruptedByShutdown = 6,

    /// <summary>The mail server refused the account's credential, which no later run clears without somebody replacing it.</summary>
    CredentialRefused = 7,
}
