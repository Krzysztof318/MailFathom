// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { metrics, SpanStatusCode, trace } from '@opentelemetry/api';
import {
    AggregationTemporality,
    InMemoryMetricExporter,
    MeterProvider,
    PeriodicExportingMetricReader,
    type MetricData,
} from '@opentelemetry/sdk-metrics';
import {
    BasicTracerProvider,
    InMemorySpanExporter,
    SimpleSpanProcessor,
    type ReadableSpan,
} from '@opentelemetry/sdk-trace-base';
import { failed, read, type ClientFailureReason } from './failure';
import { readMailAttachment } from './mailAttachment';
import { changeOwnDisplayName } from './ownDisplayName';
import { readOwnPortrait, removeOwnPortrait, replaceOwnPortrait } from './ownPortrait';
import type { ClientSession } from './session';
import { reported, spanned, telemetryEndpoints, telemetryName } from './telemetry';
import type { ClientRequest } from './transport';

// The SDK is here and nowhere in this package's source: what a test needs is somewhere to read a span and a
// measurement back from, and the registries the source publishes to are global. Every one of them is released in the
// teardown below, because the registration outlives the file otherwise and the next one would report into this one's
// exporters.

let spans: InMemorySpanExporter;
let measurements: InMemoryMetricExporter;
let traces: BasicTracerProvider;
let meters: MeterProvider;

// Everything is built per test rather than once for the file, and that is not tidiness: shutting a provider down
// stops the exporter behind it for good, so a second test sharing one would read an empty exporter and report that
// nothing was recorded.
beforeEach(() => {
    spans = new InMemorySpanExporter();
    measurements = new InMemoryMetricExporter(AggregationTemporality.CUMULATIVE);
    traces = new BasicTracerProvider({ spanProcessors: [new SimpleSpanProcessor(spans)] });
    meters = new MeterProvider({
        readers: [new PeriodicExportingMetricReader({ exporter: measurements, exportIntervalMillis: 2_147_483_647 })],
    });

    trace.setGlobalTracerProvider(traces);
    metrics.setGlobalMeterProvider(meters);
});

afterEach(async () => {
    trace.disable();
    metrics.disable();
    await traces.shutdown();
    await meters.shutdown();
});

async function recordedMeasurements(): Promise<readonly MetricData[]> {
    await meters.forceFlush();

    return measurements.getMetrics().flatMap((exported) => exported.scopeMetrics.flatMap((scope) => scope.metrics));
}

function onlySpan(): ReadableSpan {
    const [only] = spans.getFinishedSpans();

    if (only === undefined) {
        throw new Error('The operation recorded no span at all.');
    }

    return only;
}

describe('telemetryEndpoints', () => {
    it('appends the OTLP path for each signal to the receiver the client surface serves', () => {
        expect(telemetryEndpoints({ baseAddress: 'https://mail.example' })).toEqual({
            traces: 'https://mail.example/api/client/telemetry/v1/traces',
            metrics: 'https://mail.example/api/client/telemetry/v1/metrics',
            logs: 'https://mail.example/api/client/telemetry/v1/logs',
        });
    });
});

describe('spanned', () => {
    it('answers exactly what the operation answered', async () => {
        const answer = await spanned('GET /folders', () => Promise.resolve(read('a directory')));

        expect(answer).toEqual({ outcome: 'read', value: 'a directory' });
    });

    it('names the span after the route template it was given', async () => {
        await spanned('GET /messages/{storedEmailId}', () => Promise.resolve(read(1)));

        expect(onlySpan().name).toBe('GET /messages/{storedEmailId}');
    });

    it('publishes under the one name this product owns', async () => {
        await spanned('GET /folders', () => Promise.resolve(read(1)));

        expect(onlySpan().instrumentationScope.name).toBe(telemetryName);
    });

    it('records a read as an outcome with no failure beside it', async () => {
        await spanned('GET /folders', () => Promise.resolve(read(1)));

        const span = onlySpan();

        expect(span.attributes).toEqual({
            'mailfathom.client.request': 'GET /folders',
            'mailfathom.client.outcome': 'read',
        });
        expect(span.status.code).not.toBe(SpanStatusCode.ERROR);
    });

    it.each(['unauthenticated', 'unauthorized', 'unavailable', 'unreadable'] as const)(
        'records a failure as the reason the operation mapped it to, for %s',
        async (reason) => {
            await spanned('GET /folders', () => Promise.resolve(failed(reason, 401)));

            const span = onlySpan();

            expect(span.attributes).toEqual({
                'mailfathom.client.request': 'GET /folders',
                'mailfathom.client.outcome': 'failed',
                'mailfathom.client.failure': reason,
            });
            expect(span.status.code).toBe(SpanStatusCode.ERROR);
        },
    );

    it('carries nothing of the answer itself into the span', async () => {
        await spanned('GET /messages/{storedEmailId}', () =>
            Promise.resolve(read({ subject: 'Quarterly figures', from: 'somebody@example.test' })),
        );

        const span = onlySpan();

        expect(JSON.stringify(span.attributes)).not.toContain('Quarterly');
        expect(span.status.message).toBeUndefined();
    });

    it('ends the span even where the operation threw rather than answering', async () => {
        const thrown = spanned('GET /folders', () => Promise.reject(new Error('a parser defect')));

        await expect(thrown).rejects.toThrow('a parser defect');
        expect(onlySpan().name).toBe('GET /folders');
    });

    it('counts the request and times it, under the same dimensions the span carries', async () => {
        await spanned('GET /folders', () => Promise.resolve(failed('unavailable', null)));

        const recorded = await recordedMeasurements();
        const counted = recorded.find((metric) => metric.descriptor.name === 'mailfathom.client.requests');
        const timed = recorded.find((metric) => metric.descriptor.name === 'mailfathom.client.request.duration');

        expect(counted?.dataPoints[0]?.value).toBe(1);
        expect(counted?.dataPoints[0]?.attributes).toEqual({
            'mailfathom.client.request': 'GET /folders',
            'mailfathom.client.outcome': 'failed',
            'mailfathom.client.failure': 'unavailable',
        });
        expect(timed?.descriptor.unit).toBe('s');
        expect(timed?.dataPoints).toHaveLength(1);
    });

    it('counts a read and times it too, under an outcome carrying no failure', async () => {
        await spanned('GET /folders', () => Promise.resolve(read(1)));

        const recorded = await recordedMeasurements();
        const counted = recorded.find((metric) => metric.descriptor.name === 'mailfathom.client.requests');
        const timed = recorded.find((metric) => metric.descriptor.name === 'mailfathom.client.request.duration');

        expect(counted?.dataPoints[0]?.value).toBe(1);
        expect(counted?.dataPoints[0]?.attributes).toEqual({
            'mailfathom.client.request': 'GET /folders',
            'mailfathom.client.outcome': 'read',
        });
        expect(timed?.dataPoints[0]?.attributes).toEqual({
            'mailfathom.client.request': 'GET /folders',
            'mailfathom.client.outcome': 'read',
        });
    });
});

describe('reported', () => {
    const nothingFailed = (): ClientFailureReason | null => null;

    it('answers exactly what the operation answered, whatever shape that answer is', async () => {
        const answer = await reported(
            'POST /display-name',
            () => Promise.resolve({ outcome: 'stored' }),
            nothingFailed,
        );

        expect(answer).toEqual({ outcome: 'stored' });
    });

    it('records the failure the operation read off an answer of its own shape', async () => {
        await reported(
            'POST /display-name',
            () => Promise.resolve('refused'),
            () => 'unauthorized',
        );

        const span = onlySpan();

        expect(span.attributes).toEqual({
            'mailfathom.client.request': 'POST /display-name',
            'mailfathom.client.outcome': 'failed',
            'mailfathom.client.failure': 'unauthorized',
        });
        expect(span.status.code).toBe(SpanStatusCode.ERROR);
    });

    it('records an answer the client acts on as a read, whatever the route answered it with', async () => {
        await reported('POST /display-name', () => Promise.resolve('notAcceptable'), nothingFailed);

        const span = onlySpan();

        expect(span.attributes).toEqual({
            'mailfathom.client.request': 'POST /display-name',
            'mailfathom.client.outcome': 'read',
        });
        expect(span.status.code).not.toBe(SpanStatusCode.ERROR);
    });

    it('counts and times it under the same dimensions, an answer of its own shape being no exception', async () => {
        await reported('DELETE /portrait', () => Promise.resolve('gone'), nothingFailed);

        const recorded = await recordedMeasurements();
        const counted = recorded.find((metric) => metric.descriptor.name === 'mailfathom.client.requests');
        const timed = recorded.find((metric) => metric.descriptor.name === 'mailfathom.client.request.duration');

        expect(counted?.dataPoints[0]?.attributes).toEqual({
            'mailfathom.client.request': 'DELETE /portrait',
            'mailfathom.client.outcome': 'read',
        });
        expect(timed?.dataPoints).toHaveLength(1);
    });

    it('ends the span even where the operation threw rather than answering', async () => {
        const thrown = reported('DELETE /portrait', () => Promise.reject(new Error('a defect')), nothingFailed);

        await expect(thrown).rejects.toThrow('a defect');
        expect(onlySpan().name).toBe('DELETE /portrait');
    });

    // What the span carries onto the wire is not observable from here: the context manager that holds an active
    // context is registered by the application, so `context.active()` in this project answers the root whatever
    // `reported` set. `exporting.test.ts` in `Client.App` is where a request composed inside one of these operations
    // is shown carrying the `traceparent` written from it, for a request that package puts on the wire itself.
});

// The claim this package makes about itself, and what #1231 made it cost: a request nobody opened a span around sends
// no trace context either, so the deployment opens a root trace for work a screen is waiting on. Each operation is
// reached from here rather than from its own file because what is asserted is the record, and the record is read out
// of the one harness above.
describe('every request this package composes', () => {
    const session: ClientSession = { baseAddress: 'https://mail.example.invalid', authorization: 'Basic dGVzdA==' };
    const messageId = '00000000-0000-4000-8000-000000000000';
    const nothingFailed = (): ClientFailureReason | null => null;
    const delivered = (request: ClientRequest): Promise<ClientRequest> => Promise.resolve(request);

    it('reports a name somebody corrected, though it answers an outcome of its own', async () => {
        await changeOwnDisplayName(
            session,
            () =>
                Promise.resolve({
                    status: 200,
                    body: JSON.stringify({ displayName: 'Ada Lovelace', changeable: true }),
                    headers: {},
                }),
            'Ada Lovelace',
        );

        expect(onlySpan().name).toBe('POST /display-name');
        expect(onlySpan().attributes['mailfathom.client.outcome']).toBe('read');
    });

    it('reports a name this deployment would not record as an answer rather than as a failure', async () => {
        await changeOwnDisplayName(session, () => Promise.resolve({ status: 400, body: '', headers: {} }), '');

        expect(onlySpan().attributes).toEqual({
            'mailfathom.client.request': 'POST /display-name',
            'mailfathom.client.outcome': 'read',
        });
    });

    it('reports a name a refused credential lost, under the reason the operation mapped it to', async () => {
        await changeOwnDisplayName(session, () => Promise.resolve({ status: 401, body: '', headers: {} }), 'Ada');

        expect(onlySpan().attributes).toEqual({
            'mailfathom.client.request': 'POST /display-name',
            'mailfathom.client.outcome': 'failed',
            'mailfathom.client.failure': 'unauthenticated',
        });
    });

    it('reports a download the application put on the wire, named by a template rather than by the file', async () => {
        await readMailAttachment(session, messageId, 1, 2_048, delivered, nothingFailed);

        const span = onlySpan();

        expect(span.name).toBe('GET /messages/{storedEmailId}/attachments/{position}');
        expect(JSON.stringify(span.attributes)).not.toContain(messageId);
    });

    it('reports what the application made of that download, where it made a failure of it', async () => {
        await readMailAttachment(session, messageId, 1, 2_048, delivered, () => 'unavailable');

        expect(onlySpan().attributes['mailfathom.client.failure']).toBe('unavailable');
    });

    it('reports the portrait read the application put on the wire', async () => {
        await readOwnPortrait(session, delivered, nothingFailed);

        expect(onlySpan().name).toBe('GET /portrait');
    });

    it('reports the portrait replacement the application put on the wire', async () => {
        await replaceOwnPortrait(session, 'image/png', delivered, nothingFailed);

        expect(onlySpan().name).toBe('POST /portrait');
    });

    it('reports the portrait removal the application put on the wire', async () => {
        await removeOwnPortrait(session, delivered, nothingFailed);

        expect(onlySpan().name).toBe('DELETE /portrait');
    });
});
