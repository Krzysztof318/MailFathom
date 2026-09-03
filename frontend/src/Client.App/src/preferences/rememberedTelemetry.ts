// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { unsetClientPreferences } from '@mailfathom/client-backend';
import { deviceKeys, deviceStore } from '../device/deviceStore';

// The last telemetry answer this deployment gave, kept on the device so that a restart honours it before the answer
// arrives again. The deployment holds the decision — `useClientPreferences.ts` is what reads and writes it there — and
// this is a cache of it rather than a second place it is decided: nothing writes here that did not first come from, or
// go to, the deployment, and a client that has been given an answer never reads this again for the rest of the run.
//
// It exists because the alternative is worse than a stale value: a client that opened knowing nothing would record and
// export for the second or two before the read comes back, which is exactly the second or two somebody who turned it
// off did not agree to. The cost of being stale is the mirror of that — a decision changed on another machine is
// honoured one read late here — and that is the smaller of the two, because it errs towards sending nothing.

/** What this device was last told, or the deployment's own unset answer where it has never been told anything. */
export function rememberedTelemetry(): boolean {
    const stored = deviceStore().read(deviceKeys.telemetry);

    return stored === null ? unsetClientPreferences.telemetryEnabled : stored === 'true';
}

/** Keeps what the deployment answered, or what somebody just chose, for the next start of this client. */
export function rememberTelemetry(telemetryEnabled: boolean): void {
    deviceStore().write(deviceKeys.telemetry, String(telemetryEnabled));
}
