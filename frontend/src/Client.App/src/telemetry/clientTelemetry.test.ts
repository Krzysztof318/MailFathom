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

const session = { baseAddress: 'https://mail.example', authorization: 'Basic c2FtcGxl' };

describe('noTelemetry', () => {
    it('records nothing and hands back a teardown that is safe to call', () => {
        const stop = noTelemetry.exportFor(session);

        expect(() => {
            noTelemetry.navigated('mail', performance.timeOrigin);
            noTelemetry.happened('session_started');
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

    it('leaves the registries alone for a client that has not signed in', async () => {
        const telemetry = clientTelemetryForThisApplication();

        telemetry.exportFor(null)();
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

            telemetry.exportFor({ ...session, baseAddress: window.location.origin });

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
            telemetry.exportFor({ ...session, baseAddress: window.location.origin });

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

        it('reports nothing where the client was served by something other than its deployment', async () => {
            const telemetry = clientTelemetryForThisApplication();

            // Signed in to a deployment elsewhere, which is every desktop shell and every development server. The
            // teardown is queued behind the start, so waiting for it is how this knows the start finished rather than
            // that it has not begun.
            telemetry.exportFor(session)();

            await vi.waitFor(() => {
                expect(isRecording()).toBe(false);
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

// Outside the block above, because what it is about is the registries being taken over rather than what is written
// into the ones a test put there — and a provider registered here would be the thing under test's own.
describe('exportFor', () => {
    afterEach(() => {
        trace.disable();
        metrics.disable();
        logs.disable();
    });

    it('reaches the pipeline for a session that exists, and lets it go when the session ends', async () => {
        const telemetry = clientTelemetryForThisApplication();

        expect(isRecording()).toBe(false);
        const stop = telemetry.exportFor(session);

        // The pipeline is fetched rather than bundled, so it arrives a moment after it was asked for. This waits on
        // the registry answering rather than on a duration, which is what the teardown below does too.
        await vi.waitFor(() => {
            expect(isRecording()).toBe(true);
        });

        stop();

        await vi.waitFor(() => {
            expect(isRecording()).toBe(false);
        });
    });

    // The sequence `App.tsx` writes, and the one a test against an already-registered provider cannot stand in for:
    // nothing answers the log registry when `happened` is called, because `exportFor` has only asked for the pipeline.
    // A record written where it was asked would reach the no-op logger and be gone — there is no delivery of a record
    // that registry already took. The provider is registered here in the same turn, which is what a pipeline arriving
    // a moment later looks like from the queue's side.
    it('records what happened against the pipeline that arrives, not the registry that answered first', async () => {
        const arriving = new InMemoryLogRecordExporter();
        const pipeline = new LoggerProvider({ processors: [new SimpleLogRecordProcessor({ exporter: arriving })] });
        const telemetry = clientTelemetryForThisApplication();

        const stop = telemetry.exportFor(session);

        telemetry.happened('session_started');
        logs.setGlobalLoggerProvider(pipeline);

        await vi.waitFor(() => {
            expect(arriving.getFinishedLogRecords()).toHaveLength(1);
        });

        expect(arriving.getFinishedLogRecords()[0]?.attributes).toEqual({
            'mailfathom.client.event': 'session_started',
        });

        stop();
        await pipeline.shutdown();
    });

    it('keeps the session that is signed in when one ends and the next begins in the same turn', async () => {
        const telemetry = clientTelemetryForThisApplication();

        // What signing out and straight back in looks like from here, and what React does on every mount in strict
        // mode: the teardown and the next start are asked for before the first one has finished arriving. Left to
        // race, the teardown lands last and takes the registries away from the session that is now signed in.
        const stop = telemetry.exportFor(session);

        stop();
        telemetry.exportFor({ ...session, authorization: 'Basic c29tZWJvZHkgZWxzZQ==' });

        await vi.waitFor(() => {
            expect(isRecording()).toBe(true);
        });
    });
});

/** Whether a span started now would be recorded, which is the whole of what registering the pipeline changes. */
function isRecording(): boolean {
    const span = trace.getTracer('MailFathom').startSpan('probe');
    const recording = span.isRecording();

    span.end();

    return recording;
}
