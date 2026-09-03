// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { afterEach, describe, expect, it } from 'vitest';
import { telemetryKey } from '../device/deviceStore';
import { rememberedTelemetry, rememberTelemetry } from './rememberedTelemetry';

const anna = 'anna';
const bartek = 'bartek';

afterEach(() => {
    window.localStorage.clear();
});

describe('rememberedTelemetry', () => {
    it('answers what the deployment last said, so a restart honours it before it answers again', () => {
        rememberTelemetry(anna, false);

        expect(rememberedTelemetry(anna)).toBe(false);
    });

    it('answers the deployment own unset answer where this device has never been told anything', () => {
        expect(rememberedTelemetry(anna)).toBe(true);
    });

    it('is replaced rather than added to, so the last answer is the only one held', () => {
        rememberTelemetry(anna, false);
        rememberTelemetry(anna, true);

        expect(rememberedTelemetry(anna)).toBe(true);
    });

    // The reason this is held per person rather than per machine. One name for both would answer the second person
    // with the first person's answer for the length of one read, and where the first had permitted and the second
    // declined, that read is long enough to record and export what the second refused.
    it('answers one person without ever answering for another', () => {
        rememberTelemetry(anna, true);

        expect(rememberedTelemetry(bartek)).toBe(true);

        rememberTelemetry(bartek, false);

        expect(rememberedTelemetry(anna)).toBe(true);
        expect(rememberedTelemetry(bartek)).toBe(false);
    });

    it('answers nobody with the unset answer and keeps nothing under a name it does not have', () => {
        rememberTelemetry(null, false);

        expect(rememberedTelemetry(null)).toBe(true);
        expect(window.localStorage.length).toBe(0);
    });

    // What is stored names the person as a digest rather than as the name they typed, so a store that outlives the
    // session does not leave a list of who reads mail on this machine behind it.
    it('names the person nowhere a reader of the store could read it', () => {
        rememberTelemetry(anna, false);

        const [stored] = Object.keys(window.localStorage);

        expect(stored).toBe(telemetryKey(anna));
        expect(stored).not.toContain(anna);
    });

    // Anything but what this module wrote is read as withheld rather than guessed at. Another origin cannot reach this
    // store, so what would put something else there is a client of another version — and where a parser has to decide
    // something about somebody's privacy, the direction it is allowed to be wrong in is the one that sends nothing.
    it.each(['yes', '', 'TRUE', '1'])('reads %o as a decision to send nothing', (stored) => {
        window.localStorage.setItem(telemetryKey(anna), stored);

        expect(rememberedTelemetry(anna)).toBe(false);
    });
});
