// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Calendar.Extraction;

/// <summary>Says why an extraction produced nothing this time, over text that is still owed one.</summary>
/// <remarks>
/// <para>
/// Every member names something that can stop being true — an operator turning the extraction on, a period turning
/// over, a provider coming back — so none of them is ever written down against a message. A withheld extraction leaves
/// the message where it was and stops the pass that met it, rather than spending the rest of its batch on the same
/// answer.
/// </para>
/// <para>
/// A provider that answered with something unreadable is deliberately absent. The call was made and paid for, and
/// asking again buys the same answer, so it settles as an extraction that proposed nothing rather than leaving the
/// message to be offered to the endpoint forever.
/// </para>
/// </remarks>
public enum CalendarEventExtractionWithholding
{
    /// <summary>The deployment does not read text into calendar events.</summary>
    NotActivated = 0,

    /// <summary>The period's spend allowance is used up.</summary>
    AllowanceExhausted = 1,

    /// <summary>The provider could not be reached, refused the call, or did not answer in time.</summary>
    ProviderUnavailable = 2,
}
