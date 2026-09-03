// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
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

    async function recordedMeasurements(): Promise<readonly MetricData[]> {
        await meters.forceFlush();

        return measurements.getMetrics().flatMap((exported) => exported.scopeMetrics.flatMap((scope) => scope.metrics));
    }

    it('spans a move to a space from the moment it was asked for', () => {
        const telemetry = clientTelemetryForThisApplication();
        const askedAt = performance.timeOrigin + performance.now();

        telemetry.navigated('mail', askedAt);

        const [span] = spans.getFinishedSpans();

        expect(span?.name).toBe('navigate mail');
        expect(span?.attributes).toEqual({ 'mailfathom.client.space': 'mail' });
    });

    it('counts a move and times it, under the space it reached', async () => {
        const telemetry = clientTelemetryForThisApplication();

        telemetry.navigated('discover', performance.timeOrigin + performance.now());

        const recorded = await recordedMeasurements();
        const counted = recorded.find((metric) => metric.descriptor.name === 'mailfathom.client.navigations');
        const timed = recorded.find((metric) => metric.descriptor.name === 'mailfathom.client.navigation.duration');

        expect(counted?.dataPoints[0]?.value).toBe(1);
        expect(counted?.dataPoints[0]?.attributes).toEqual({ 'mailfathom.client.space': 'discover' });
        expect(timed?.descriptor.unit).toBe('s');
    });

    it('records a session beginning as an informational occurrence', () => {
        const telemetry = clientTelemetryForThisApplication();

        telemetry.happened('session_started');

        const [record] = records.getFinishedLogRecords();

        expect(record?.severityNumber).toBe(SeverityNumber.INFO);
        expect(record?.attributes).toEqual({ 'mailfathom.client.event': 'session_started' });
    });

    it('records a credential the deployment stopped accepting as a warning', () => {
        const telemetry = clientTelemetryForThisApplication();

        telemetry.happened('credential_no_longer_accepted');

        const [record] = records.getFinishedLogRecords();

        expect(record?.severityNumber).toBe(SeverityNumber.WARN);
        expect(record?.attributes).toEqual({ 'mailfathom.client.event': 'credential_no_longer_accepted' });
    });

    it('carries no part of the credential or the address into anything it records', () => {
        const telemetry = clientTelemetryForThisApplication();

        telemetry.navigated('mail', performance.timeOrigin + performance.now());
        telemetry.happened('session_started');

        const written = JSON.stringify([
            spans.getFinishedSpans().map((span) => [span.name, span.attributes]),
            records.getFinishedLogRecords().map((record) => [record.body, record.attributes]),
        ]);

        expect(written).not.toContain('c2FtcGxl');
        expect(written).not.toContain('mail.example');
    });

    it('leaves the registries alone for a client that has not signed in', () => {
        const telemetry = clientTelemetryForThisApplication();

        telemetry.exportFor(null)();
        telemetry.navigated('mail', performance.timeOrigin + performance.now());

        // The span still reaches the exporter this test registered, which is what says nothing replaced it.
        expect(spans.getFinishedSpans()).toHaveLength(1);
    });
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
