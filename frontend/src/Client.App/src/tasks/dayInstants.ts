// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

// The three places this screen has to turn a reader's day into the instants the wire takes. Every calendar route states
// two instants and the day layout states two more, so which moments are somebody's day is the client's to say.
//
// It says it in the zone the runtime reports, which the reader's record may now disagree with: the record is what the
// deployment anchors a relative period on and what every wording here is placed in, and a reader whose record states
// another zone has this screen's day boundaries drawn from their machine rather than from it. That is a placement
// rather than a wording and it reaches the calendar's own span the same way, so it is one correction against both
// rather than a second reading invented here.
//
// They are together rather than beside whichever caller reached for one first, because all three are the same reading
// of the same thing and a second copy of it is how two of them come to disagree about when a day ends.

/**
 * The instants one local day opens and closes at.
 *
 * Built by moving the day rather than by adding twenty-four hours, so a day carrying a daylight-saving change still
 * closes at the next midnight rather than an hour either side of it.
 *
 * @param now The instant the reader is reading at.
 * @returns The two instants, as the wire spells them.
 */
export function dayAround(now: number): { readonly from: string; readonly until: string } {
    const at = new Date(now);

    return {
        from: new Date(at.getFullYear(), at.getMonth(), at.getDate()).toISOString(),
        until: new Date(at.getFullYear(), at.getMonth(), at.getDate() + 1).toISOString(),
    };
}

/**
 * The instant a calendar day opens at, in the reader's own zone.
 *
 * A task's due date is a day somebody picked rather than a moment, so it is read as local midnight and never as UTC
 * midnight — read the other way, an event written for it would land on the day before for every reader west of
 * Greenwich. It is the same reading `wordCalendarDay` gives the same value, which is why neither of them names a zone.
 *
 * @param day A calendar day as `yyyy-mm-dd`.
 * @returns The instant it opens at, or `null` where the value is not a day.
 */
export function startOfDay(day: string): string | null {
    const at = new Date(`${day}T00:00:00`);

    return Number.isNaN(at.getTime()) ? null : at.toISOString();
}

/**
 * The instant something beginning then and running that long ends at.
 *
 * Arithmetic over what the deployment itself stated rather than a judgement of its own: an arrangement names where a
 * task starts and how many minutes it takes, and an event states two instants.
 *
 * @param startAt When it begins, as the deployment stated it.
 * @param minutes How long it runs.
 * @returns The instant it ends at, or `null` where the start is not an instant.
 */
export function endAfter(startAt: string, minutes: number): string | null {
    const began = Date.parse(startAt);

    return Number.isNaN(began) ? null : new Date(began + minutes * 60_000).toISOString();
}
