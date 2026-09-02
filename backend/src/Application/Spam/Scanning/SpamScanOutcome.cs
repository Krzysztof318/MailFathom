// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Spam.Scanning;

/// <summary>How a scan of one message ended.</summary>
/// <remarks>
/// A scan is a secondary signal over content that is already local, so none of these is an error a caller reports
/// upwards: a classification reached without the scanner is a weaker classification rather than a failed one. That is
/// why this is a result rather than an exception — the immediate caller acts on it and continues.
/// </remarks>
public enum SpamScanOutcome
{
    /// <summary>The scanner answered with a score, the threshold it judged against, and what fired.</summary>
    Scored = 0,

    /// <summary>The scanner could not be reached, did not answer within its bound, or answered unintelligibly.</summary>
    Unavailable = 1,

    /// <summary>The message was larger than the scanner accepts, so nothing was sent to it.</summary>
    /// <remarks>
    /// Distinct from being unavailable because it is a property of the message rather than of the deployment: retrying
    /// it will produce the same answer, and nothing an operator does to the sidecar changes that.
    /// </remarks>
    ContentTooLarge = 2,
}
