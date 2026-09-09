// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Emails.ThreadStates;

/// <summary>Says why a derivation produced nothing this time, for a conversation that is still owed one.</summary>
/// <remarks>
/// <para>
/// Every member names something that can stop being true — an operator turning the derivation on, a period turning
/// over, a provider coming back — which is why none of them is ever written down against a conversation. A withheld
/// derivation leaves the conversation exactly where it was, outstanding, and the pass that met it stops rather than
/// spending the rest of its batch on the same answer.
/// </para>
/// <para>
/// A conversation longer than one derivation may take in is not one of these. Nothing about it stops being true on the
/// next run, so it is a settled record carrying <see cref="ThreadStateCoverage.ThreadTooLarge" /> rather than a
/// withholding that would put the same conversation at the front of every pass forever.
/// </para>
/// </remarks>
public enum ThreadStateWithholding
{
    /// <summary>The deployment has not turned the derivation on.</summary>
    NotActivated = 0,

    /// <summary>The period's spend allowance is used up.</summary>
    AllowanceExhausted = 1,

    /// <summary>The provider could not be reached, refused the call, or did not answer in time.</summary>
    ProviderUnavailable = 2,
}
