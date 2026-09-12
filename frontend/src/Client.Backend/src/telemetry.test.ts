// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { metrics, SpanStatusCode, trace } from '@opentelemetry/api';
import { logs, SeverityNumber } from '@opentelemetry/api-logs';
import { InMemoryLogRecordExporter, LoggerProvider, SimpleLogRecordProcessor } from '@opentelemetry/sdk-logs';
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
import { readClientPreferences, unsetClientPreferences, writeClientPreferences } from './clientPreferences';
import { readDeploymentSession } from './deploymentSession';
import { failed, read, type ClientFailureReason } from './failure';
import { readMailAccounts } from './mailAccounts';
import { readMailAttachment } from './mailAttachment';
import { readMailBody } from './mailBody';
import { readMailFolders } from './mailFolders';
import { readMailMessage } from './mailMessage';
import { markMailRead } from './mailMutations';
import { readMailSearch } from './mailSearch';
import { readMailThread } from './mailThread';
import { readMailTimeline } from './mailTimeline';
import { changeOwnDisplayName, readOwnDisplayName } from './ownDisplayName';
import { readOwnPortrait, removeOwnPortrait, replaceOwnPortrait } from './ownPortrait';
import type { ClientSession } from './session';
import { reachDeployment, signIn } from './signIn';
import {
    defaultTelemetryLevel,
    isDeploymentTelemetryLevel,
    recordDownTo,
    reported,
    severityOf,
    spanned,
    telemetryEndpoints,
    telemetryName,
    worthRecording,
    writeClientEvent,
} from './telemetry';
import type { ClientRequest, MailFathomTransport } from './transport';

// The SDK is here and nowhere in this package's source: what a test needs is somewhere to read a span and a
// measurement back from, and the registries the source publishes to are global. Every one of them is released in the
// teardown below, because the registration outlives the file otherwise and the next one would report into this one's
// exporters.

let spans: InMemorySpanExporter;
let measurements: InMemoryMetricExporter;
let records: InMemoryLogRecordExporter;
let traces: BasicTracerProvider;
let meters: MeterProvider;
let loggers: LoggerProvider;

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

    records = new InMemoryLogRecordExporter();
    loggers = new LoggerProvider({ processors: [new SimpleLogRecordProcessor({ exporter: records })] });

    trace.setGlobalTracerProvider(traces);
    metrics.setGlobalMeterProvider(meters);
    logs.setGlobalLoggerProvider(loggers);

    // The floor is module state, so a test that lowered it would otherwise decide what the next one records. Every
    // test states what it wants, and the file begins where a client that has heard from no deployment begins.
    recordDownTo(defaultTelemetryLevel);
});

afterEach(async () => {
    trace.disable();
    metrics.disable();
    logs.disable();
    recordDownTo(defaultTelemetryLevel);
    await traces.shutdown();
    await meters.shutdown();
    await loggers.shutdown();
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

// Everything this package can be asked to do, driven with mail in every argument that takes one, so that the promise
// #1232 makes about the client is a test rather than a sentence in a document. Every operation records through
// `spanned` or `reported` and this package records nowhere else, so covering the operations covers the whole of the
// surface — and an operation added later that named a composed path rather than a route template fails the shape
// assertion below rather than shipping one span name per message.
//
// The values below are what a real screen would pass and are exactly the things that must never leave: a message
// identifier, a thread identifier, a folder somebody named, an address, the name a person goes by, a search somebody
// typed, the credential the session presents, and the deployment it presents it to. None of them comes from a real
// mailbox, which `frontend/AGENTS.md` requires of anything standing in for mail here.
//
// The block above asserts what each operation names its own span; this one asserts what none of them may carry, over
// all of them at once. Reading an operation into both is the point rather than a duplication: the drive is what makes
// the promise hold for whatever is recorded next rather than for what happened to be recorded when it was written.
describe('what the whole of this package records', () => {
    const storedEmailId = '018f2c31-2f2c-7c1e-9f0e-3a1b6f9c0d21';
    const threadId = '018f2c31-2f2c-7c1e-9f0e-000000000002';
    const folder = 'INBOX/Clients/Acme';
    const address = 'anna.kowalska@example.test';
    const searchText = 'salary review';
    const credential = 'Basic QWxhZGRpbjpvcGVuIHNlc2FtZQ==';
    const ownName = 'Anna Kowalska';
    const session: ClientSession = { baseAddress: 'https://mail.example.invalid', authorization: credential };

    // What this package could have read is not what the test is about, so every answer is refused: the operation then
    // reports a failure, which is the branch carrying the failure dimension as well and therefore the wider of the two.
    const refusing: MailFathomTransport = () => Promise.resolve({ status: 500, body: '', headers: {} });

    // The operations that compose a request instead of sending one are handed the composer the application hands
    // them, and it answers with the request itself — which is what puts the composed path in front of the assertions
    // below rather than only the template the span was named after.
    const delivered = (request: ClientRequest): Promise<ClientRequest> => Promise.resolve(request);
    const nothingFailed = (): ClientFailureReason | null => null;

    const window = {
        account: 'work',
        folder,
        includeJunk: false,
        unread: null,
        flagged: null,
        hasAttachments: null,
        pageSize: 50,
        cursor: null,
    };

    async function driveTheWholeSurface(): Promise<void> {
        await reachDeployment(session, refusing);
        await signIn(session, refusing);
        await readDeploymentSession(session, refusing);
        await readClientPreferences(session, refusing);
        await writeClientPreferences(session, refusing, unsetClientPreferences);
        await readOwnDisplayName(session, refusing);
        await changeOwnDisplayName(session, refusing, ownName);
        await readOwnPortrait(session, delivered, nothingFailed);
        await replaceOwnPortrait(session, 'image/png', delivered, nothingFailed);
        await removeOwnPortrait(session, delivered, nothingFailed);
        await readMailAccounts(session, refusing);
        await readMailFolders(session, refusing);
        await readMailTimeline(session, refusing, {
            ...window,
            order: 'newestFirst',
            direction: 'forward',
            receivedOnOrAfter: null,
            receivedBefore: null,
            carriesMark: null,
            markDueOnOrAfter: null,
            markDueBefore: null,
        });
        await readMailSearch(session, refusing, {
            ...window,
            text: searchText,
            sender: address,
            recipient: address,
            receivedOnOrAfter: null,
            receivedBefore: null,
        });
        await readMailThread(session, refusing, threadId, 'a cursor');
        await readMailMessage(session, refusing, storedEmailId);
        await readMailBody(session, refusing, storedEmailId, { remoteImages: true, fullHtml: true });
        await readMailAttachment(session, storedEmailId, 1, 2_048, delivered, nothingFailed);
        await markMailRead(session, refusing, [storedEmailId, threadId]);
    }

    it.each([
        ['a message identifier', storedEmailId],
        ['a thread identifier', threadId],
        ['a folder somebody named', folder],
        ['an address', address],
        ['the name the signed-in person goes by', ownName],
        ['a search somebody typed', searchText],
        ['the credential', credential],
        ['the deployment it is signed in to', 'mail.example.invalid'],
    ])('carries no %s into a span, a measurement, or a log record', async (_, forbidden) => {
        // At the lowest floor there is, so the assertion is made over everything this package can write rather than
        // over the part a default deployment happens to keep. A record refused before it was composed would pass this
        // by never existing, which is the way a redaction test quietly stops asserting anything.
        recordDownTo('trace');

        await driveTheWholeSurface();

        const recorded = JSON.stringify([
            spans.getFinishedSpans().map((span) => [span.name, span.attributes, span.status.message]),
            await recordedMeasurements(),
            records.getFinishedLogRecords().map((one) => [one.body, one.attributes]),
        ]);

        expect(recorded).not.toContain(forbidden);
    });

    // The stronger half of the same claim, and the half that survives a value nobody thought to forbid: every request
    // this package names itself is a method and a route template, whose segments are literals or `{placeholder}` holes
    // and nothing else. A composed path would put one value per message into a span name and into a metric dimension,
    // which is a cardinality defect as well as a disclosure.
    //
    // A literal segment is lower-case words joined by hyphens and carries no digit, which is what makes the pattern an
    // assertion rather than a shape: an identifier, an address, a folder name, and a percent-encoded search term each
    // fail it on a character a route this package publishes never has.
    const literalSegment = String.raw`[a-z][a-zA-Z]*(-[a-z][a-zA-Z]*)*`;
    const routeTemplate = new RegExp(String.raw`^(GET|POST|PUT|DELETE) (\/(${literalSegment}|\{[a-zA-Z]+\}))+$`);

    it('names every request it makes by a route template rather than by a path it composed', async () => {
        await driveTheWholeSurface();

        const named = spans.getFinishedSpans().map((span) => span.name);

        expect(named.length).toBeGreaterThan(0);
        expect(named.filter((name) => !routeTemplate.test(name))).toEqual([]);
    });

    it('names a span after exactly what it put in the dimension beside it', async () => {
        await driveTheWholeSurface();

        const disagreeing = spans
            .getFinishedSpans()
            .filter((span) => span.attributes['mailfathom.client.request'] !== span.name);

        expect(disagreeing).toEqual([]);
    });

    // The same claim read from the other end, and the one that survives an attribute nobody thought to forbid: every
    // value a record here carries is a closed-set name, a route template, or a plain count, so anything that is none of
    // the three is a value that reached a record from somewhere it should not have.
    it('writes no attribute that is not a closed-set name, a route template, or a count', async () => {
        recordDownTo('trace');

        await driveTheWholeSurface();

        const written = records.getFinishedLogRecords();
        const loose = written
            .flatMap((one) => Object.entries(one.attributes))
            .filter(([, value]) => typeof value !== 'number')
            .filter(
                ([, value]) =>
                    typeof value !== 'string' || (!/^[a-z][a-z_]*$/.test(value) && !routeTemplate.test(value)),
            );

        expect(written.length).toBeGreaterThan(0);
        expect(loose).toEqual([]);
    });
});

describe('the level a deployment asks for', () => {
    it('reads every level this client publishes and nothing else', () => {
        for (const level of ['off', 'trace', 'debug', 'info', 'warn', 'error', 'fatal']) {
            expect(isDeploymentTelemetryLevel(level)).toBe(true);
        }

        for (const notALevel of ['verbose', 'Info', '', true, null, undefined, 3]) {
            expect(isDeploymentTelemetryLevel(notALevel)).toBe(false);
        }
    });

    it('admits a record at the floor and refuses one below it', () => {
        recordDownTo('warn');

        expect(worthRecording(SeverityNumber.WARN)).toBe(true);
        expect(worthRecording(SeverityNumber.ERROR)).toBe(true);
        expect(worthRecording(SeverityNumber.INFO)).toBe(false);
        expect(worthRecording(SeverityNumber.DEBUG)).toBe(false);
    });

    // The client begins where a collector begins, so a deployment that has not answered — every cold start, and every
    // sign-in that failed before one could — holds what an ordinary deployment would have asked for anyway.
    it('stands at the level a collector keeps by default until a deployment has answered', () => {
        expect(defaultTelemetryLevel).toBe('info');
        expect(worthRecording(SeverityNumber.INFO)).toBe(true);
        expect(worthRecording(SeverityNumber.DEBUG)).toBe(false);
    });

    it('refuses every record where the deployment forwards none', () => {
        recordDownTo('off');
        writeClientEvent({ event: 'render_failed', severity: SeverityNumber.FATAL });

        expect(worthRecording(SeverityNumber.FATAL)).toBe(false);
        expect(records.getFinishedLogRecords()).toEqual([]);
    });

    it('writes the occurrence, its sentence, and the attributes it was given', () => {
        recordDownTo('trace');
        writeClientEvent({ event: 'signals_refused', attributes: { 'mailfathom.client.attempt': 3 }, at: 1_700_000 });

        const [only] = records.getFinishedLogRecords();

        expect(only?.severityNumber).toBe(severityOf.signals_refused);
        expect(only?.body).toBe('A connection to the deployment signal hub could not be opened.');
        expect(only?.attributes).toEqual({
            'mailfathom.client.event': 'signals_refused',
            'mailfathom.client.attempt': 3,
        });
    });
});

describe('a request the client made', () => {
    it('is recorded at the quietest level there is, so an ordinary deployment is told about none of them', async () => {
        recordDownTo('debug');
        await spanned('GET /folders', () => Promise.resolve(read('a directory')));

        expect(records.getFinishedLogRecords()).toEqual([]);
    });

    it('is recorded once it reports as read, naming the route template and the outcome', async () => {
        recordDownTo('trace');
        await spanned('GET /folders', () => Promise.resolve(read('a directory')));

        const [only] = records.getFinishedLogRecords();

        expect(only?.severityNumber).toBe(SeverityNumber.TRACE);
        expect(only?.attributes['mailfathom.client.event']).toBe('request_completed');
        expect(only?.attributes['mailfathom.client.request']).toBe('GET /folders');
        expect(only?.attributes['mailfathom.client.outcome']).toBe('read');
    });

    // One level above the stream around it, because a failure is rare enough to be worth having without it: an
    // operator lowering the floor to the client's own account of what it did gets the requests that went wrong and not
    // the ones that did not.
    it('is recorded a level higher where it failed, naming which failure it was', async () => {
        recordDownTo('debug');
        await spanned('GET /folders', () => Promise.resolve(failed('unavailable', 503)));

        const [only] = records.getFinishedLogRecords();

        expect(only?.severityNumber).toBe(SeverityNumber.DEBUG);
        expect(only?.attributes['mailfathom.client.event']).toBe('request_failed');
        expect(only?.attributes['mailfathom.client.failure']).toBe('unavailable');
    });
});
