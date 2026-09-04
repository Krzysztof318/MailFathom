// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { afterEach, beforeEach, describe, expect, it, vi, type MockInstance } from 'vitest';
import { metrics, trace } from '@opentelemetry/api';
import { logs, SeverityNumber } from '@opentelemetry/api-logs';
import { InMemoryLogRecordExporter, LoggerProvider, SimpleLogRecordProcessor } from '@opentelemetry/sdk-logs';
import {
    AggregationTemporality,
    InMemoryMetricExporter,
    MeterProvider,
    PeriodicExportingMetricReader,
    type MetricData,
} from '@opentelemetry/sdk-metrics';
import { BasicTracerProvider, InMemorySpanExporter, SimpleSpanProcessor } from '@opentelemetry/sdk-trace-base';
import { clientTelemetryForThisApplication, noTelemetry } from './clientTelemetry';

// The registries this module publishes to are global, so every one of them is released after each test: a
// registration that outlived the file would take the next file's records.
//
// The pipeline is the one collaborator replaced here, and it is replaced rather than run because composing this
// telemetry now starts it: the module under test is the queue in front of the three registries, and a real pipeline
// would take those registries over from the exporters each test reads its records back out of. What that pipeline
// does with a session is `exporting.test.ts`, and what it holds meanwhile is `holding.test.ts`.

const pipeline = vi.hoisted(() => {
    const steps: string[] = [];

    return {
        steps,

        exportTo(session: { readonly authorization: string }) {
            steps.push(`export ${session.authorization}`);

            return Promise.resolve();
        },

        hold() {
            steps.push('hold');

            return Promise.resolve();
        },

        discard() {
            steps.push('discard');

            return Promise.resolve();
        },

        shutdown: () => Promise.resolve(),
    };
});

vi.mock('./exporting', () => ({ startRecording: () => pipeline }));

const session = { baseAddress: 'https://mail.example', authorization: 'Basic c2FtcGxl' };

beforeEach(() => {
    pipeline.steps.length = 0;
});

describe('noTelemetry', () => {
    it('records nothing and hands back a teardown that is safe to call', () => {
        const stop = noTelemetry.exportFor(session, true);

        expect(() => {
            noTelemetry.navigated('mail', performance.timeOrigin);
            noTelemetry.happened('session_started');
            noTelemetry.renderFailed('application', new Error('Nothing composed a pipeline yet.'));
            stop();
        }).not.toThrow();
    });
});

describe('clientTelemetryForThisApplication', () => {
    let spans: InMemorySpanExporter;
    let measurements: InMemoryMetricExporter;
    let records: InMemoryLogRecordExporter;
    let traces: BasicTracerProvider;
    let meters: MeterProvider;
    let loggers: LoggerProvider;

    // Built per test, because shutting a provider down stops the exporter behind it for good.
    beforeEach(() => {
        spans = new InMemorySpanExporter();
        measurements = new InMemoryMetricExporter(AggregationTemporality.CUMULATIVE);
        records = new InMemoryLogRecordExporter();
        traces = new BasicTracerProvider({ spanProcessors: [new SimpleSpanProcessor(spans)] });
        meters = new MeterProvider({
            readers: [
                new PeriodicExportingMetricReader({ exporter: measurements, exportIntervalMillis: 2_147_483_647 }),
            ],
        });
        loggers = new LoggerProvider({ processors: [new SimpleLogRecordProcessor({ exporter: records })] });

        trace.setGlobalTracerProvider(traces);
        metrics.setGlobalMeterProvider(meters);
        logs.setGlobalLoggerProvider(loggers);
    });

    afterEach(async () => {
        trace.disable();
        metrics.disable();
        logs.disable();
        await traces.shutdown();
        await meters.shutdown();
        await loggers.shutdown();
    });

    // Everything this module records is queued behind whatever the pipeline is doing, so a test reads what was written
    // rather than what had been written by the time the call returned.
    async function written<TRecord>(reading: () => readonly TRecord[]): Promise<readonly TRecord[]> {
        await vi.waitFor(() => {
            expect(reading().length).toBeGreaterThan(0);
        });

        return reading();
    }

    async function recordedMeasurements(): Promise<readonly MetricData[]> {
        await meters.forceFlush();

        return measurements.getMetrics().flatMap((exported) => exported.scopeMetrics.flatMap((scope) => scope.metrics));
    }

    it('spans a move to a space from the moment it was asked for', async () => {
        const telemetry = clientTelemetryForThisApplication();
        const askedAt = performance.timeOrigin + performance.now();

        telemetry.navigated('mail', askedAt);

        const [span] = await written(() => spans.getFinishedSpans());

        expect(span?.name).toBe('navigate mail');
        expect(span?.attributes).toEqual({ 'mailfathom.client.space': 'mail' });
    });

    // An address is what a person put in the fragment, so what reaches this is whatever survived reading it rather
    // than a value the compiler chose. Each of these is something a later route could legitimately carry, and none of
    // them may become a span name or a dimension value.
    it.each([
        ['a message identifier', 'mail/018f2c31-2f2c-7c1e-9f0e-3a1b6f9c0d21'],
        ['a folder somebody named', 'mail/INBOX/Clients/Acme'],
        ['what somebody searched for', 'search?q=salary%20review'],
    ])('reports a move to %s as an unnamed space', async (_, address) => {
        const telemetry = clientTelemetryForThisApplication();

        telemetry.navigated(address, performance.timeOrigin + performance.now());

        const [span] = await written(() => spans.getFinishedSpans());

        expect(span?.name).toBe('navigate other');
        expect(span?.attributes).toEqual({ 'mailfathom.client.space': 'other' });
    });

    it('counts a move and times it, under the space it reached', async () => {
        const telemetry = clientTelemetryForThisApplication();

        telemetry.navigated('discover', performance.timeOrigin + performance.now());

        await written(() => spans.getFinishedSpans());

        const recorded = await recordedMeasurements();
        const counted = recorded.find((metric) => metric.descriptor.name === 'mailfathom.client.navigations');
        const timed = recorded.find((metric) => metric.descriptor.name === 'mailfathom.client.navigation.duration');

        expect(counted?.dataPoints[0]?.value).toBe(1);
        expect(counted?.dataPoints[0]?.attributes).toEqual({ 'mailfathom.client.space': 'discover' });
        expect(timed?.descriptor.unit).toBe('s');
    });

    it('records a session beginning as an informational occurrence', async () => {
        const telemetry = clientTelemetryForThisApplication();

        telemetry.happened('session_started');

        const [record] = await written(() => records.getFinishedLogRecords());

        expect(record?.severityNumber).toBe(SeverityNumber.INFO);
        expect(record?.attributes).toEqual({ 'mailfathom.client.event': 'session_started' });
    });

    it('records a credential the deployment stopped accepting as a warning', async () => {
        const telemetry = clientTelemetryForThisApplication();

        telemetry.happened('credential_no_longer_accepted');

        const [record] = await written(() => records.getFinishedLogRecords());

        expect(record?.severityNumber).toBe(SeverityNumber.WARN);
        expect(record?.attributes).toEqual({ 'mailfathom.client.event': 'credential_no_longer_accepted' });
    });

    it('records a failure a boundary contained as an error, naming the region and what was thrown', async () => {
        const telemetry = clientTelemetryForThisApplication();

        telemetry.renderFailed('reading_pane', new TypeError('Cannot read properties of undefined.'));

        const [record] = await written(() => records.getFinishedLogRecords());

        expect(record?.severityNumber).toBe(SeverityNumber.ERROR);
        expect(record?.attributes).toEqual({
            'mailfathom.client.event': 'render_failed',
            'mailfathom.client.region': 'reading_pane',
            'mailfathom.client.error': 'TypeError',
        });
    });

    // An exception's message is unbounded by construction and a message assembled from mail is what this is about, so
    // the record says which region and which class and nothing else — not the message, and not the stack behind it.
    it('carries neither the message of what was thrown nor its stack into what it records', async () => {
        const telemetry = clientTelemetryForThisApplication();

        telemetry.renderFailed('reading_pane', new Error('Salary review from anna@mail.example could not be drawn.'));

        const [record] = await written(() => records.getFinishedLogRecords());
        const recorded = JSON.stringify([record?.body, record?.attributes]);

        expect(recorded).not.toContain('Salary review');
        expect(recorded).not.toContain('mail.example');
        expect(recorded).not.toContain('clientTelemetry.test');
    });

    it('reports a thrown value that is not an error at all as the kind of value it was', async () => {
        const telemetry = clientTelemetryForThisApplication();

        telemetry.renderFailed('application', 'something a library threw instead of an error');

        const [record] = await written(() => records.getFinishedLogRecords());

        expect(record?.attributes['mailfathom.client.error']).toBe('string');
    });

    it('refuses a class name that is not an ordinary one, rather than reporting whatever it was called', async () => {
        class Unbounded extends Error {}
        Object.defineProperty(Unbounded, 'name', { value: 'x'.repeat(4_096) });

        const telemetry = clientTelemetryForThisApplication();

        telemetry.renderFailed('application', new Unbounded());

        const [record] = await written(() => records.getFinishedLogRecords());

        expect(record?.attributes['mailfathom.client.error']).toBe('unknown');
    });

    it('carries no part of the credential or the address into anything it records', async () => {
        const telemetry = clientTelemetryForThisApplication();

        telemetry.navigated('mail', performance.timeOrigin + performance.now());
        telemetry.happened('session_started');

        await written(() => records.getFinishedLogRecords());

        const recorded = JSON.stringify([
            spans.getFinishedSpans().map((span) => [span.name, span.attributes]),
            records.getFinishedLogRecords().map((record) => [record.body, record.attributes]),
        ]);

        expect(recorded).not.toContain('c2FtcGxl');
        expect(recorded).not.toContain('mail.example');
    });

    describe('somebody who has not agreed to be reported on', () => {
        it('has nothing written about them, whether or not there is a session to export it', async () => {
            const telemetry = clientTelemetryForThisApplication();

            telemetry.exportFor(session, false);
            telemetry.navigated('mail', performance.timeOrigin + performance.now());
            telemetry.happened('session_started');
            telemetry.renderFailed('reading_pane', new TypeError('A message this pane cannot draw.'));

            // Nothing arrives to wait for, so what is waited on is the pipeline having been asked to throw away what
            // it held — everything above was queued before that and would be in front of it had it been recorded.
            await vi.waitFor(() => {
                expect(pipeline.steps).toContain('discard');
            });

            expect(spans.getFinishedSpans()).toHaveLength(0);
            expect(records.getFinishedLogRecords()).toHaveLength(0);
        });

        it('is recorded again from the moment they say so', async () => {
            const telemetry = clientTelemetryForThisApplication();

            telemetry.exportFor(session, false);
            telemetry.navigated('mail', performance.timeOrigin + performance.now());
            telemetry.exportFor(session, true);
            telemetry.navigated('discover', performance.timeOrigin + performance.now());

            const [span] = await written(() => spans.getFinishedSpans());

            // One span rather than two: the move made while they had said no is not recovered by them saying yes.
            expect(spans.getFinishedSpans()).toHaveLength(1);
            expect(span?.name).toBe('navigate discover');
        });
    });

    it('leaves the registries alone for a client that has not signed in', async () => {
        const telemetry = clientTelemetryForThisApplication();

        telemetry.exportFor(null, true)();
        telemetry.navigated('mail', performance.timeOrigin + performance.now());

        // The span still reaches the exporter this test registered, which is what says nothing replaced it.
        expect(await written(() => spans.getFinishedSpans())).toHaveLength(1);
    });

    // How long the document took to arrive is a measurement about a deployment answering, so it is reported only where
    // the deployment is what served the document. A desktop shell serving the same bundle over `http://tauri.localhost`
    // and a development server pointed at a deployment elsewhere both fail that, and both would otherwise put a disk
    // read on a histogram of network arrivals. The browser is stubbed because jsdom navigates to nothing and so times
    // nothing, which would let either assertion below pass for the wrong reason.
    describe('the arrival of the document', () => {
        let timing: MockInstance<typeof performance.getEntriesByType>;

        beforeEach(() => {
            timing = vi
                .spyOn(performance, 'getEntriesByType')
                .mockReturnValue([{ duration: 1_500 } as PerformanceEntry]);
        });

        afterEach(() => {
            vi.restoreAllMocks();
        });

        it('reports it where the deployment is what served this client', async () => {
            const telemetry = clientTelemetryForThisApplication();

            telemetry.exportFor({ ...session, baseAddress: window.location.origin }, true);

            await vi.waitFor(async () => {
                expect(await arrivalDuration()).toBe(1.5);
            });
        });

        it('waits for the document to finish loading before reading what it cost', async () => {
            vi.spyOn(document, 'readyState', 'get').mockReturnValue('interactive');

            const telemetry = clientTelemetryForThisApplication();

            // What a session restored from a stored credential looks like: it is signed in before the load event, and
            // the entry then describes a document still arriving. This run gets one sign-in and no second attempt, so
            // reading it here is losing the measurement rather than deferring it.
            telemetry.exportFor({ ...session, baseAddress: window.location.origin }, true);

            // Queued behind the start, so its record appearing is how this knows the start finished — reading the
            // histogram before that would find it empty whether the measurement was deferred or merely late.
            telemetry.happened('session_started');

            await written(() => records.getFinishedLogRecords());

            expect(await arrivalDuration()).toBeUndefined();

            window.dispatchEvent(new Event('load'));

            await vi.waitFor(async () => {
                expect(await arrivalDuration()).toBe(1.5);
            });
        });

        it('still reports it for the session it is about, after one it was not', async () => {
            const telemetry = clientTelemetryForThisApplication();

            // A run pointed somewhere else mid-run: the first session is signed in to a deployment that did not serve
            // this document, and the second is signed in to the one that did. The document was fetched once, before
            // either, so the measurement belongs to the second session rather than being spent on the first.
            //
            // What is read is the entry being consulted at all, rather than where the value landed: starting a second
            // pipeline takes the registries this test put there away, so the histogram is no longer this test's to
            // read by then. Consulting the entry is the whole of what the first session must not have used up.
            telemetry.exportFor(session, true);
            telemetry.exportFor({ ...session, baseAddress: window.location.origin }, true);

            await vi.waitFor(() => {
                expect(timing).toHaveBeenCalled();
            });
        });

        it('reports nothing where the client was served by something other than its deployment', async () => {
            const telemetry = clientTelemetryForThisApplication();

            // Signed in to a deployment elsewhere, which is every desktop shell and every development server. The
            // teardown is queued behind the start, so waiting for it is how this knows the start finished rather than
            // that it has not begun.
            telemetry.exportFor(session, true)();

            await vi.waitFor(() => {
                expect(pipeline.steps).toContain('hold');
            });

            expect(timing).not.toHaveBeenCalled();
        });
    });

    async function arrivalDuration(): Promise<number | undefined> {
        const recorded = await recordedMeasurements();
        const arrival = recorded.find((metric) => metric.descriptor.name === 'mailfathom.client.arrival.duration');
        const [point] = arrival?.dataPoints ?? [];

        return typeof point?.value === 'object' && 'sum' in point.value ? point.value.sum : undefined;
    }
});

// Outside the block above, because what it is about is what a session does to the pipeline rather than what is written
// into the registries a test put there.
describe('exportFor', () => {
    afterEach(() => {
        trace.disable();
        metrics.disable();
        logs.disable();
    });

    it('names the session as the destination, and takes it away again when the session ends', async () => {
        const telemetry = clientTelemetryForThisApplication();
        const stop = telemetry.exportFor(session, true);

        // The pipeline is fetched rather than bundled, so it arrives a moment after the client composed it. This waits
        // on what it was asked to do rather than on a duration.
        await vi.waitFor(() => {
            expect(pipeline.steps).toEqual(['export Basic c2FtcGxl']);
        });

        stop();

        await vi.waitFor(() => {
            expect(pipeline.steps).toEqual(['export Basic c2FtcGxl', 'hold']);
        });
    });

    it('asks for no destination at all for a client that has not signed in', async () => {
        const telemetry = clientTelemetryForThisApplication();

        telemetry.exportFor(null, true)();

        // Signing in afterwards is what makes the absence provable: the queue is ordered, so a destination or a hold
        // asked for by the call above would sit in front of this one.
        telemetry.exportFor(session, true);

        await vi.waitFor(() => {
            expect(pipeline.steps).toEqual(['export Basic c2FtcGxl']);
        });
    });

    // The sequence `App.tsx` writes, and the one a test against an already-registered provider cannot stand in for:
    // nothing answers the log registry when `happened` is called, because the pipeline has only just been asked for.
    // A record written where it was asked would reach the no-op logger and be gone — there is no delivery of a record
    // that registry already took. The provider is registered here in the same turn, which is what a pipeline arriving
    // a moment later looks like from the queue's side.
    it('records what happened against the pipeline that arrives, not the registry that answered first', async () => {
        const arriving = new InMemoryLogRecordExporter();
        const loggers = new LoggerProvider({ processors: [new SimpleLogRecordProcessor({ exporter: arriving })] });
        const telemetry = clientTelemetryForThisApplication();

        const stop = telemetry.exportFor(session, true);

        telemetry.happened('session_started');
        logs.setGlobalLoggerProvider(loggers);

        await vi.waitFor(() => {
            expect(arriving.getFinishedLogRecords()).toHaveLength(1);
        });

        expect(arriving.getFinishedLogRecords()[0]?.attributes).toEqual({
            'mailfathom.client.event': 'session_started',
        });

        stop();
        await loggers.shutdown();
    });

    it('throws away what was held rather than exporting it, when the answer arrives as off', async () => {
        const telemetry = clientTelemetryForThisApplication();

        // What a fresh client does: it records against the deployment's unset answer while the preference is being
        // read, and the answer comes back off. Nothing may be addressed, so the discard stands alone in the queue —
        // an export before it would be the one batch the person had asked never to be sent.
        telemetry.navigated('mail', performance.timeOrigin + performance.now());
        telemetry.exportFor(session, false);

        await vi.waitFor(() => {
            expect(pipeline.steps).toEqual(['discard']);
        });
    });

    it('exports for a session again once the answer comes back the other way', async () => {
        const telemetry = clientTelemetryForThisApplication();

        telemetry.exportFor(session, false);
        telemetry.exportFor(session, true);

        await vi.waitFor(() => {
            expect(pipeline.steps).toEqual(['discard', 'export Basic c2FtcGxl']);
        });
    });

    // The order React produces when only the permission changed: the previous effect's cleanup, then the next effect's
    // body. Holding flushes, so a hold that went ahead here would send the batch somebody had just declined — and the
    // discard behind it would then be throwing away an empty buffer while the records were already on the wire.
    it('throws away rather than flushing when a session that was permitted is refused', async () => {
        const telemetry = clientTelemetryForThisApplication();

        const stop = telemetry.exportFor(session, true);

        stop();
        telemetry.exportFor(session, false);

        await vi.waitFor(() => {
            expect(pipeline.steps).toEqual(['export Basic c2FtcGxl', 'discard']);
        });
        expect(pipeline.steps).not.toContain('hold');
    });

    it('keeps the session that is signed in when one ends and the next begins in the same turn', async () => {
        const telemetry = clientTelemetryForThisApplication();

        // What signing out and straight back in looks like from here, and what React does on every mount in strict
        // mode: the teardown and the next start are asked for before the first one has finished arriving. Left to
        // race, the hold lands last and leaves the session that is now signed in holding for the rest of the run.
        const stop = telemetry.exportFor(session, true);

        stop();
        telemetry.exportFor({ ...session, authorization: 'Basic c29tZWJvZHkgZWxzZQ==' }, true);

        await vi.waitFor(() => {
            expect(pipeline.steps).toEqual(['export Basic c2FtcGxl', 'hold', 'export Basic c29tZWJvZHkgZWxzZQ==']);
        });
    });
});
