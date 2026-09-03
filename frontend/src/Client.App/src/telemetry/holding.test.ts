// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { metrics, type Attributes } from '@opentelemetry/api';
import { ExportResultCode, type ExportResult } from '@opentelemetry/core';
import { LoggerProvider, SimpleLogRecordProcessor, type ReadableLogRecord } from '@opentelemetry/sdk-logs';
import {
    AggregationTemporality,
    InMemoryMetricExporter,
    MeterProvider,
    PeriodicExportingMetricReader,
    type PushMetricExporter,
} from '@opentelemetry/sdk-metrics';
import { BasicTracerProvider, SimpleSpanProcessor, type ReadableSpan } from '@opentelemetry/sdk-trace-base';
import { heldLogRecordExporter, heldMetricExporter, heldSpanExporter } from './holding';

// Every buffer here is driven through a real provider rather than handed records built by hand, because what a span
// and a log record cost is measured off what the SDK actually produced. A destination is faked, since what is being
// proven is what reaches one and when — never what an OTLP exporter does with it.

// Far enough away that the reader never collects on its own: every collection in this file is one a test asked for.
const neverOnItsOwn = 2_147_483_647;

interface FakeDestination<TRecord> {
    readonly exported: TRecord[][];
    readonly refuse: () => void;
    export: (records: TRecord[], done: (result: ExportResult) => void) => void;
    forceFlush: () => Promise<void>;
    shutdown: () => Promise<void>;
}

function fakeDestination<TRecord>(): FakeDestination<TRecord> {
    let answer: ExportResult = { code: ExportResultCode.SUCCESS };

    return {
        exported: [],

        refuse() {
            answer = { code: ExportResultCode.FAILED };
        },

        export(records, done) {
            this.exported.push(records);
            done(answer);
        },

        forceFlush: () => Promise.resolve(),
        shutdown: () => Promise.resolve(),
    };
}

interface FakeMeasurementDestination extends PushMetricExporter {
    readonly exported: number[];
    readonly refuse: () => void;
}

function fakeMeasurementDestination(): FakeMeasurementDestination {
    let answer: ExportResult = { code: ExportResultCode.SUCCESS };

    return {
        exported: [],

        refuse() {
            answer = { code: ExportResultCode.FAILED };
        },

        export(measurements, done) {
            for (const scope of measurements.scopeMetrics) {
                for (const metric of scope.metrics) {
                    for (const point of metric.dataPoints) {
                        if (typeof point.value === 'number') {
                            this.exported.push(point.value);
                        }
                    }
                }
            }

            done(answer);
        },

        forceFlush: () => Promise.resolve(),
        shutdown: () => Promise.resolve(),
    };
}

describe('the buffers a client holds before it signs in', () => {
    let losses: InMemoryMetricExporter;
    let meters: MeterProvider;

    // The counter a drop is reported on is written to the global registry, which is the client's own pipeline in a
    // running client and this in-memory one here.
    beforeEach(() => {
        losses = new InMemoryMetricExporter(AggregationTemporality.CUMULATIVE);
        meters = new MeterProvider({
            readers: [new PeriodicExportingMetricReader({ exporter: losses, exportIntervalMillis: neverOnItsOwn })],
        });

        metrics.setGlobalMeterProvider(meters);
    });

    afterEach(async () => {
        metrics.disable();
        await meters.shutdown();
    });

    async function dropsCounted(): Promise<readonly { readonly value: number; readonly attributes: Attributes }[]> {
        await meters.forceFlush();

        const collected = losses.getMetrics().at(-1);
        const counted: { readonly value: number; readonly attributes: Attributes }[] = [];

        for (const scope of collected?.scopeMetrics ?? []) {
            for (const metric of scope.metrics) {
                if (metric.descriptor.name !== 'mailfathom.client.telemetry.dropped') {
                    continue;
                }

                for (const point of metric.dataPoints) {
                    if (typeof point.value === 'number') {
                        counted.push({ value: point.value, attributes: point.attributes });
                    }
                }
            }
        }

        return counted;
    }

    describe('heldSpanExporter', () => {
        let held: ReturnType<typeof heldSpanExporter>;
        let traces: BasicTracerProvider;

        beforeEach(() => {
            held = heldSpanExporter();
            traces = new BasicTracerProvider({ spanProcessors: [new SimpleSpanProcessor(held)] });
        });

        afterEach(async () => {
            await traces.shutdown();
        });

        async function record(name: string, attributes: Attributes = {}): Promise<void> {
            traces.getTracer('MailFathom').startSpan(name, { attributes }).end();

            await traces.forceFlush();
        }

        it('empties everything it held into one export rather than one for each batch it took', async () => {
            const destination = fakeDestination<ReadableSpan>();

            await record('navigate mail');
            await record('navigate discover');

            await held.exportTo(destination);

            expect(destination.exported).toHaveLength(1);
            expect(destination.exported[0]?.map((span) => span.name)).toEqual(['navigate mail', 'navigate discover']);
        });

        it('sends what it records after a destination is named rather than holding it', async () => {
            const destination = fakeDestination<ReadableSpan>();

            await held.exportTo(destination);
            await record('navigate mail');

            expect(destination.exported.flat().map((span) => span.name)).toEqual(['navigate mail']);
        });

        it('holds again once the destination is taken away', async () => {
            const destination = fakeDestination<ReadableSpan>();

            await held.exportTo(destination);
            held.hold();

            await record('navigate mail');

            expect(destination.exported).toHaveLength(0);
        });

        it('drops the oldest records once it is holding more than it may, and counts them', async () => {
            const destination = fakeDestination<ReadableSpan>();

            for (let recorded = 0; recorded < 600; recorded += 1) {
                await record(`navigate space ${String(recorded)}`);
            }

            await held.exportTo(destination);

            // The bound is 512 records, so the first 88 are the ones that went and the newest are what a person's
            // sign-in actually carries.
            expect(destination.exported[0]).toHaveLength(512);
            expect(destination.exported[0]?.[0]?.name).toBe('navigate space 88');
            expect(await dropsCounted()).toEqual([
                {
                    value: 88,
                    attributes: {
                        'mailfathom.client.signal': 'traces',
                        'mailfathom.client.telemetry.condition': 'overflowed',
                    },
                },
            ]);
        });

        it('drops the oldest once what it holds is too large, whatever the count is', async () => {
            const destination = fakeDestination<ReadableSpan>();
            const wide = 'x'.repeat(100 * 1024);

            await record('navigate mail', { 'mailfathom.client.space': wide });
            await record('navigate discover', { 'mailfathom.client.space': wide });

            await held.exportTo(destination);

            expect(destination.exported[0]?.map((span) => span.name)).toEqual(['navigate discover']);
            expect((await dropsCounted())[0]?.value).toBe(1);
        });

        it('counts what a refused export could not deliver rather than holding it a second time', async () => {
            const destination = fakeDestination<ReadableSpan>();

            destination.refuse();

            await record('navigate mail');
            await held.exportTo(destination);

            expect(await dropsCounted()).toEqual([
                {
                    value: 1,
                    attributes: {
                        'mailfathom.client.signal': 'traces',
                        'mailfathom.client.telemetry.condition': 'export_failed',
                    },
                },
            ]);

            const next = fakeDestination<ReadableSpan>();

            held.hold();
            await held.exportTo(next);

            expect(next.exported).toHaveLength(0);
        });

        it('writes nothing to the device, so a client closed without a session leaves nothing behind', async () => {
            const kept = vi.spyOn(Storage.prototype, 'setItem');

            await record('navigate mail');

            expect(kept).not.toHaveBeenCalled();

            kept.mockRestore();
        });
    });

    describe('heldLogRecordExporter', () => {
        it('holds what happened until a destination is named, and then sends it', async () => {
            const destination = fakeDestination<ReadableLogRecord>();
            const held = heldLogRecordExporter();
            const loggers = new LoggerProvider({ processors: [new SimpleLogRecordProcessor({ exporter: held })] });

            loggers.getLogger('MailFathom').emit({ body: 'A client session began.' });
            await loggers.forceFlush();

            expect(destination.exported).toHaveLength(0);

            await held.exportTo(destination);

            expect(destination.exported.flat().map((record) => record.body)).toEqual(['A client session began.']);

            await loggers.shutdown();
        });
    });

    describe('heldMetricExporter', () => {
        let held: ReturnType<typeof heldMetricExporter>;
        let recorded: MeterProvider;

        beforeEach(() => {
            held = heldMetricExporter();
            recorded = new MeterProvider({
                readers: [new PeriodicExportingMetricReader({ exporter: held, exportIntervalMillis: neverOnItsOwn })],
            });
        });

        afterEach(async () => {
            await recorded.shutdown();
        });

        it('carries what was measured before the destination existed, the totals being cumulative', async () => {
            const destination = fakeMeasurementDestination();

            recorded.getMeter('MailFathom').createCounter('mailfathom.client.navigations').add(3);

            // Collected while there is nowhere to send it, which is every collection a client makes before somebody
            // signs in. Nothing is kept, and nothing is lost by not keeping it.
            await recorded.forceFlush();

            expect(destination.exported).toEqual([]);

            await held.exportTo(destination);
            await recorded.forceFlush();

            expect(destination.exported).toEqual([3]);
        });

        it('counts no loss for an export the deployment refused, the next one carrying the same totals', async () => {
            const destination = fakeMeasurementDestination();

            destination.refuse();

            recorded.getMeter('MailFathom').createCounter('mailfathom.client.navigations').add(1);

            await held.exportTo(destination);
            await recorded.forceFlush();

            expect(await dropsCounted()).toEqual([]);
        });
    });
});
