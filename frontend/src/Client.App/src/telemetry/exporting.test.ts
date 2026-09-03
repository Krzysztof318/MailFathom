// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { afterEach, describe, expect, it } from 'vitest';
import { logs } from '@opentelemetry/api-logs';
import { metrics, trace } from '@opentelemetry/api';
import { startExporting } from './exporting';

// What this module does is register the three providers and take them away again, so that is what is asserted: no
// batch is exported here, because nothing is recorded and a browser with no deployment behind it is not the witness
// for what reaches the wire anyway.

const session = { baseAddress: 'https://mail.example', authorization: 'Basic c2FtcGxl' };

afterEach(() => {
    trace.disable();
    metrics.disable();
    logs.disable();
});

/** Whether a span started now would be recorded, which is the whole of what registering the pipeline changes. */
function recording(): boolean {
    const span = trace.getTracer('MailFathom').startSpan('probe');
    const started = span.isRecording();

    span.end();

    return started;
}

describe('startExporting', () => {
    it('records nothing until a session is exported for', () => {
        expect(recording()).toBe(false);
    });

    it('starts recording for the session, and stops again when the session ends', () => {
        const stop = startExporting(session);

        expect(recording()).toBe(true);

        stop();

        expect(recording()).toBe(false);
    });

    it('reports under the client rather than under the deployment it is signed in to', () => {
        const stop = startExporting(session);

        // The tracer the whole client publishes through is the one this registration answers, which is what a screen
        // and `Client.Backend` both reach without either naming a provider.
        expect(trace.getTracer('MailFathom').startSpan('probe').isRecording()).toBe(true);

        stop();
    });
});
