// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Delivery;

namespace MailFathom.Mcp.Tools.Outgoing;

/// <summary>Reads the published state one stored stage is reported under, for every result that reports one.</summary>
/// <remarks>
/// Three tools answer with a send's state — the one that queues it, the one that reads it back, and the one that
/// withdraws it — and two of them would otherwise say <c>sending</c> where the third said something else. One mapping
/// is what keeps the word a property of the stage rather than of whichever tool a client happened to call.
/// </remarks>
internal static class SendEmailStateMapping
{
    /// <summary>Reads the published state one stored stage is reported under.</summary>
    /// <param name="stage">The stage the record has durably reached.</param>
    /// <returns>The state a client is told.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the stage is one this surface does not publish, which is a stage added without deciding what a caller should be told about it.</exception>
    /// <remarks>
    /// Written out rather than cast, so a stage added to the record has to be given a published spelling here before it
    /// can reach a client. The alternative would publish whichever name happened to sit at the same ordinal.
    /// </remarks>
    public static SendEmailState Published(OutgoingEmailStage stage) => stage switch
    {
        OutgoingEmailStage.Recorded => SendEmailState.Queued,
        OutgoingEmailStage.TransmissionBegun => SendEmailState.Sending,
        OutgoingEmailStage.Sent => SendEmailState.Sent,
        OutgoingEmailStage.Refused => SendEmailState.Refused,
        OutgoingEmailStage.Cancelled => SendEmailState.Cancelled,
        _ => throw new ArgumentOutOfRangeException(
            nameof(stage),
            stage,
            "The outgoing email stage is not one this surface publishes."),
    };
}
