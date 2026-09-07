// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Emails.Enrichment;

/// <summary>Says why a derivation produced nothing this time, for a message that is still owed one.</summary>
/// <remarks>
/// <para>
/// Every member names something that can stop being true — an operator turning enrichment on, a period turning over, a
/// provider coming back — which is why none of them is ever written down against a message. A withheld derivation
/// leaves the message exactly where it was, outstanding, and the pass that met it stops rather than spending the rest
/// of its batch on the same answer.
/// </para>
/// <para>
/// That is the difference from an empty set of marks, which is a settled answer and is stored. Recording a withholding
/// against the message would take it out of the queue permanently over a condition that lasts minutes.
/// </para>
/// </remarks>
public enum EmailEnrichmentWithholding
{
    /// <summary>The deployment has not turned enrichment on.</summary>
    NotActivated = 0,

    /// <summary>The period's spend allowance is used up.</summary>
    AllowanceExhausted = 1,

    /// <summary>The provider could not be reached, refused the call, or did not answer in time.</summary>
    ProviderUnavailable = 2,

    /// <summary>The provider answered with something this deployment could not read as a derivation.</summary>
    AnswerUnreadable = 3,
}
