// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import type { ClientEvent } from '@mailfathom/client-backend';
import type { ClientTelemetry } from './clientTelemetry';

/** One occurrence as a screen reported it, which is the whole of what a test about telemetry has to read. */
export interface RecordedEvent {
    readonly event: ClientEvent;
    readonly attributes: Readonly<Record<string, string | number>>;
}

/**
 * A pipeline that writes nothing anywhere and remembers what it was handed.
 *
 * It is here rather than beside any one screen because four of them report occurrences and each would otherwise carry
 * a stub of its own — and a stub per screen is how two of them come to disagree about what a recorded event even is.
 * Nothing about exporting is modelled: a screen states what happened and never learns whether it left the process,
 * which is the boundary this stands in for.
 */
export function recordingTelemetry(): { readonly telemetry: ClientTelemetry; readonly recorded: RecordedEvent[] } {
    const recorded: RecordedEvent[] = [];

    return {
        recorded,
        telemetry: {
            exportFor: () => () => undefined,
            navigated: () => undefined,
            happened: (event, attributes = {}) => {
                recorded.push({ event, attributes });
            },
            renderFailed: () => undefined,
        },
    };
}

/** What was reported under one name, which is what an assertion about a single occurrence reads. */
export function recordsOf(recorded: readonly RecordedEvent[], event: ClientEvent): RecordedEvent[] {
    return recorded.filter((record) => record.event === event);
}
