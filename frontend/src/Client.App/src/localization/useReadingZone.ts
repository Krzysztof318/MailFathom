// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { createContext, useContext } from 'react';

// The zone every date on the screen is placed in, which is the one the signed-in person's record states rather than
// the one the runtime reports. It is a context rather than a prop because seven screens word an instant and none of
// them is near the frame that reads the record, and it carries the value itself rather than a state setter because
// what it publishes is what `profile/useOwnProfile.ts` already holds.
//
// It sits beside the localization context, and apart from it, because the two answer different questions and come
// from different places: which language a person reads is theirs to switch on this device, and which days their mail
// falls on is what the deployment records about them and anchors its own answers on.
//
// `null` is the runtime's own zone. That is what stands before the record has answered, what a deployment holding no
// record for the reader leaves in force, and what every test that pins no zone gets — so nothing has to name a zone
// to draw a date.

export const ReadingZoneContext = createContext<string | null>(null);

/** The zone the signed-in person's days are read in, or `null` for the zone this runtime reports. */
export function useReadingZone(): string | null {
    return useContext(ReadingZoneContext);
}
