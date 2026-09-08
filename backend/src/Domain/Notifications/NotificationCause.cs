// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Domain.Notifications;

/// <summary>What a notification was raised for, as a closed set rather than as the sentence it is said in.</summary>
/// <remarks>
/// A notification's words are a rendering decision and the service takes none: it holds which condition occurred and
/// the numbers that condition is stated with, and whoever draws the row says it in the language that reader has. The
/// set is therefore the record's rather than one producer's, exactly as <see cref="NotificationKind" /> is, and a
/// condition that gains a producer joins it here.
/// </remarks>
public enum NotificationCause
{
    /// <summary>Mail arrived for the person while nobody was looking at the screen.</summary>
    MailArrived = 0,

    /// <summary>A synchronization run ended with folders it did not finish.</summary>
    SynchronizationIncomplete = 1,

    /// <summary>The mail server refused the credential MailFathom holds for an account.</summary>
    CredentialRefused = 2,
}
