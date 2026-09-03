// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { logs } from '@opentelemetry/api-logs';
import { metrics, trace } from '@opentelemetry/api';
import { ExportResultCode } from '@opentelemetry/core';
import { startRecording, type ClientPipeline } from './exporting';

// What this module does is register the three providers for the whole of a run and decide, from whether a session
// exists, whether any of it leaves the client. So that is what is asserted here: that recording begins before anybody
// has signed in, that nothing reaches the network while it is holding, and what a session is exported to when one
// arrives. What a buffer does with what it holds is `holding.test.ts`, against a destination it can fake.
//
// The three OTLP exporters are the one thing replaced. They are where this pipeline meets the network, they are
// constructed here rather than handed in — a destination belongs to a session, and there is no session to hand one
// from before somebody signs in — and jsdom is not a deployment. Replacing them is what lets this file read the
// address and the credential the client would have presented.

const destinations = vi.hoisted(() => {
    const built: { readonly url: string; readonly authorization: string }[] = [];
    const shutDown: string[] = [];

    class FakeDestination {
        readonly url: string;

        constructor(options: { url: string; headers: Record<string, string> }) {
            this.url = options.url;
            built.push({ url: options.url, authorization: options.headers['Authorization'] ?? '' });
        }

        export(_records: unknown, done: (result: { code: ExportResultCode }) => void): void {
            done({ code: ExportResultCode.SUCCESS });
        }

        forceFlush(): Promise<void> {
            return Promise.resolve();
        }

        shutdown(): Promise<void> {
            shutDown.push(this.url);

            return Promise.resolve();
        }
    }

    return { built, shutDown, FakeDestination };
});

vi.mock('@opentelemetry/exporter-trace-otlp-proto', () => ({ OTLPTraceExporter: destinations.FakeDestination }));
vi.mock('@opentelemetry/exporter-metrics-otlp-proto', () => ({ OTLPMetricExporter: destinations.FakeDestination }));
vi.mock('@opentelemetry/exporter-logs-otlp-proto', () => ({ OTLPLogExporter: destinations.FakeDestination }));

const session = { baseAddress: 'https://mail.example', authorization: 'Basic c2FtcGxl' };

let running: ClientPipeline | null = null;

beforeEach(() => {
    destinations.built.length = 0;
    destinations.shutDown.length = 0;
});

afterEach(async () => {
    await running?.shutdown();
    running = null;

    trace.disable();
    metrics.disable();
    logs.disable();

    vi.restoreAllMocks();
});

/** Whether a span started now would be recorded, which is the whole of what registering the pipeline changes. */
function recording(): boolean {
    const span = trace.getTracer('MailFathom').startSpan('probe');
    const started = span.isRecording();

    span.end();

    return started;
}

describe('startRecording', () => {
    it('records nothing until the pipeline is composed', () => {
        expect(recording()).toBe(false);
    });

    it('records from the moment it is composed, which is before anybody has signed in', () => {
        running = startRecording();

        expect(recording()).toBe(true);
    });

    it('reports under the client rather than under the deployment it is signed in to', () => {
        running = startRecording();

        // The tracer the whole client publishes through is the one this registration answers, which is what a screen
        // and `Client.Backend` both reach without either naming a provider.
        expect(trace.getTracer('MailFathom').startSpan('probe').isRecording()).toBe(true);
    });

    it('addresses nothing and reaches no network while it is holding, however much it has recorded', async () => {
        const sent = vi.spyOn(globalThis, 'fetch').mockResolvedValue(new Response());
        const opened = vi.spyOn(XMLHttpRequest.prototype, 'open');

        running = startRecording();

        record();

        // Everything the three providers hold is pushed at its exporter, which is the point a destination would be
        // addressed if there were one.
        await running.hold();

        expect(destinations.built).toEqual([]);
        expect(sent).not.toHaveBeenCalled();
        expect(opened).not.toHaveBeenCalled();
    });

    it('exports to the deployment the session is signed in to, presenting that session', async () => {
        running = startRecording();

        record();

        await running.exportTo(session);

        expect(destinations.built).toEqual([
            { url: 'https://mail.example/api/client/telemetry/v1/traces', authorization: 'Basic c2FtcGxl' },
            { url: 'https://mail.example/api/client/telemetry/v1/metrics', authorization: 'Basic c2FtcGxl' },
            { url: 'https://mail.example/api/client/telemetry/v1/logs', authorization: 'Basic c2FtcGxl' },
        ]);
    });

    it('lets the destination go when the session ends, so nothing stays addressed to it', async () => {
        running = startRecording();

        await running.exportTo(session);
        await running.hold();

        expect(destinations.shutDown).toHaveLength(3);

        record();

        // Signing in again names a destination of its own rather than reviving the one that was let go.
        await running.exportTo({ ...session, authorization: 'Basic c29tZWJvZHkgZWxzZQ==' });

        expect(destinations.built).toHaveLength(6);
    });

    it('stops recording when the run that composed it lets it go', async () => {
        running = startRecording();

        await running.shutdown();
        running = null;

        expect(recording()).toBe(false);
    });
});

/** One record on each of the three signals, which is what a client has to show for the time before it signed in. */
function record(): void {
    trace.getTracer('MailFathom').startSpan('navigate mail').end();
    metrics.getMeter('MailFathom').createCounter('mailfathom.client.navigations').add(1);
    logs.getLogger('MailFathom').emit({ body: 'A client session began.' });
}
