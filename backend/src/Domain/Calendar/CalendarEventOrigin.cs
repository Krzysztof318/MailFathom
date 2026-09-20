// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Domain.Calendar;

/// <summary>States whether an event is on the calendar or merely offered to it.</summary>
/// <remarks>
/// <para>
/// The two values are different claims rather than two kinds of event. An asserted event is one somebody put on their
/// calendar — typed, imported from a file they chose, or accepted from a proposal; a proposed one is a date something
/// read out of mail that nobody has agreed to. A proposal is therefore not a calendar entry: it is read where
/// proposals are read and by nothing that answers what a day holds.
/// </para>
/// <para>
/// Accepting one changes this value on the row rather than writing a second record, so the proposal and the entry it
/// became are one thing under one identity — which is what keeps the message it cites, and everything later derived
/// from it, pointing at the event a person actually holds.
/// </para>
/// </remarks>
public enum CalendarEventOrigin
{
    /// <summary>Somebody put this event on their calendar.</summary>
    Asserted = 0,

    /// <summary>Something read this date out of mail, and nobody has accepted it.</summary>
    Proposed = 1,
}
