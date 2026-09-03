// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { afterEach, describe, expect, it } from 'vitest';
import { rememberedTelemetry, rememberTelemetry } from './rememberedTelemetry';

afterEach(() => {
    window.localStorage.clear();
});

describe('rememberedTelemetry', () => {
    it('answers what the deployment last said, so a restart honours it before it answers again', () => {
        rememberTelemetry(false);

        expect(rememberedTelemetry()).toBe(false);
    });

    it('answers the deployment own unset answer where this device has never been told anything', () => {
        expect(rememberedTelemetry()).toBe(true);
    });

    it('is replaced rather than added to, so the last answer is the only one held', () => {
        rememberTelemetry(false);
        rememberTelemetry(true);

        expect(rememberedTelemetry()).toBe(true);
    });

    // Anything but what this module wrote is read as withheld rather than guessed at. Another origin cannot reach this
    // store, so what would put something else there is a client of another version — and where a parser has to decide
    // something about somebody's privacy, the direction it is allowed to be wrong in is the one that sends nothing.
    it.each(['yes', '', 'TRUE', '1'])('reads %o as a decision to send nothing', (stored) => {
        window.localStorage.setItem('mailfathom.telemetry', stored);

        expect(rememberedTelemetry()).toBe(false);
    });
});
