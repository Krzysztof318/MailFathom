// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { unsetClientPreferences } from '@mailfathom/client-backend';
import { deviceStore, telemetryKey } from '../device/deviceStore';

// The last telemetry answer the deployment gave one person, kept on the device so that a restart honours it before the
// answer arrives again. The deployment holds the decision — `useClientPreferences.ts` is what reads and writes it there
// — and this is a cache of it rather than a second place it is decided: nothing writes here that did not first come
// from, or go to, the deployment, and a client holding that person's own answer never reads this again for the rest of
// their session.
//
// It exists because the alternative is worse than a stale value: a client that opened knowing nothing would record and
// export for the second or two before the read comes back, which is exactly the second or two somebody who turned it
// off did not agree to. The cost of being stale is the mirror of that — a decision changed on another machine is
// honoured one read late here — and that is the smaller of the two, because it errs towards sending nothing.
//
// It is held per person and never as one value for the machine, which is the whole of what makes the paragraph above
// true of a machine two people read on. One name for both would answer the second person with the first person's
// answer for the length of one read, and where the first had permitted and the second declined, that read is long
// enough to record and export what the second refused. A name per person cannot do that: nothing is stored under a
// person nobody has answered for, and what a client does then is take the deployment's own unset answer.

/**
 * What this device was last told about one person, or the deployment's unset answer where it has been told nothing.
 *
 * @param person Whose answer to read, or `null` where nobody is signed in — which reads as nothing stored, the address
 * being the only thing that could say otherwise and there being none.
 */
export function rememberedTelemetry(person: string | null): boolean {
    const stored = person === null ? null : deviceStore().read(telemetryKey(person));

    return stored === null ? unsetClientPreferences.telemetryEnabled : stored === 'true';
}

/**
 * Keeps what the deployment answered about this person, or what they just chose, for the next start of this client.
 *
 * @param person Whose answer this is, or `null` where nobody is signed in — in which case nothing is written rather
 * than something being stored under a name that would be read back for somebody else.
 */
export function rememberTelemetry(person: string | null, telemetryEnabled: boolean): void {
    if (person === null) {
        return;
    }

    deviceStore().write(telemetryKey(person), String(telemetryEnabled));
}
