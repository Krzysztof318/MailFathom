// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { metrics } from '@opentelemetry/api';
import { ExportResultCode, type ExportResult } from '@opentelemetry/core';
import type { LogRecordExporter, ReadableLogRecord } from '@opentelemetry/sdk-logs';
import { AggregationTemporality, type PushMetricExporter } from '@opentelemetry/sdk-metrics';
import type { ReadableSpan, SpanExporter } from '@opentelemetry/sdk-trace-web';
import { telemetryName } from '@mailfathom/client-backend';

// What stands between the three providers and the wire while nobody is signed in. Each of these is an exporter as far
// as the SDK is concerned, and it is the only kind of exporter registered: the OTLP exporters are built when a session
// exists and handed to one of these, so a client that never signs in never constructs one and never addresses anything.
//
// The pipeline records from the composition root because the failures somebody cannot describe are exactly the ones
// that happen before a session — starting up, resolving which deployment this client belongs to, and a sign-in that did
// not succeed. None of that is visible to the deployment, because nothing reached it.
//
// Everything held lives in memory and nowhere else. A client closed without a session takes its buffer with it: nothing
// is written to storage, so a restart begins empty, because a record of somebody's use of their own machine kept on
// that machine is one they never agreed to.

/** Which signal a buffer holds, named as the client endpoint's own relay names the three. */
type Signal = 'traces' | 'logs';

/** Why a record was dropped, written as a past participle like every other outcome MailFathom publishes. */
type Loss = 'overflowed' | 'export_failed';

/**
 * How much one signal holds before the oldest records go.
 *
 * Both bounds are on the buffer rather than on how long a record has been in it, because what makes holding safe is
 * that it cannot grow without limit — and a person who leaves the sign-in screen open for an afternoon is exactly the
 * case an elapsed-time bound would throw away and a size bound keeps. The byte bound is the one that matters on a page
 * recording many small spans, and the record bound the one that matters on a few large ones.
 */
const heldRecordBound = 512;
const heldByteBound = 128 * 1024;

/**
 * What a record costs beyond the text this client wrote into it: identifiers, timestamps, a status, and the scope.
 *
 * The exact figure is what the exporter's protobuf encoding produces, and it is not known until the exporter serializes
 * the batch — which is work the bound exists to avoid, and work that would then be done twice. So the bound is held
 * against an estimate whose varying part is measured and whose fixed part is allowed for here.
 */
const recordOverheadBytes = 128;

/** An exporter of one kind of record, which is the half of `SpanExporter` and `LogRecordExporter` that they share. */
interface RecordDestination<TRecord> {
    export(records: TRecord[], done: (result: ExportResult) => void): void;
    forceFlush?(): Promise<void>;
    shutdown(): Promise<void>;
}

/** What names a destination for what a signal has been holding, and what takes it away again. */
export interface Held<TDestination> {
    /**
     * Points this signal at a destination and empties the buffer into it in one export.
     *
     * @returns When that export has been answered, so a caller can sequence what it does next against it. A failed
     * export is not held a second time: the exporter behind it has already applied the pipeline's own retry bounds,
     * and what it could not deliver past them is counted rather than kept for a retry nothing would ever run.
     */
    readonly exportTo: (destination: TDestination) => Promise<void>;

    /** Returns to holding. Nothing reaches the network again until a destination is named. */
    readonly hold: () => void;
}

/** Holds spans until somebody signs in, and exports to the deployment that session is signed in to afterwards. */
export function heldSpanExporter(): SpanExporter & Held<SpanExporter> {
    return heldRecords<ReadableSpan>('traces', spanBytes);
}

/** Holds log records until somebody signs in, on the same terms the spans above are held on. */
export function heldLogRecordExporter(): LogRecordExporter & Held<LogRecordExporter> {
    return heldRecords<ReadableLogRecord>('logs', logRecordBytes);
}

/**
 * Defers exporting measurements until somebody signs in, and buffers none of them.
 *
 * This is the one signal with nothing to hold, and that is a property of how it is aggregated rather than an omission.
 * The temporality below is cumulative, so every instrument's own state already carries everything recorded since the
 * client opened, and the first export after a sign-in therefore carries the start it followed whether or not anything
 * was kept in the meantime. Buffering the reader's periodic collections beside that would hold several snapshots of
 * one running total, spend the byte bound above on them, and export them as history that is already in the last one.
 *
 * It is also what keeps the drop counter honest. What a full buffer or a failed export lost is reported as a
 * measurement, and a measurement that was itself buffered would be a count of losses waiting behind the thing it is
 * counting. Nothing here can be lost that way: a failed export loses nothing at all, because the next one carries the
 * same totals again.
 */
export function heldMetricExporter(): PushMetricExporter & Held<PushMetricExporter> {
    let destination: PushMetricExporter | null = null;

    return {
        export(measurements, done) {
            if (destination === null) {
                done({ code: ExportResultCode.SUCCESS });

                return;
            }

            destination.export(measurements, done);
        },

        selectAggregationTemporality: () => AggregationTemporality.CUMULATIVE,

        forceFlush: () => destination?.forceFlush() ?? Promise.resolve(),

        shutdown() {
            destination = null;

            return Promise.resolve();
        },

        exportTo(named) {
            destination = named;

            return Promise.resolve();
        },

        hold() {
            destination = null;
        },
    };
}

interface HeldRecordExporter<TRecord> extends RecordDestination<TRecord>, Held<RecordDestination<TRecord>> {
    forceFlush(): Promise<void>;
}

function heldRecords<TRecord>(signal: Signal, sizeOf: (record: TRecord) => number): HeldRecordExporter<TRecord> {
    let held: { readonly record: TRecord; readonly bytes: number }[] = [];
    let heldBytes = 0;
    let destination: RecordDestination<TRecord> | null = null;

    function keep(records: readonly TRecord[]): void {
        for (const record of records) {
            const bytes = sizeOf(record);

            held.push({ record, bytes });
            heldBytes += bytes;
        }

        let overflowed = 0;

        while (held.length > heldRecordBound || heldBytes > heldByteBound) {
            const oldest = held.shift();

            if (oldest === undefined) {
                break;
            }

            heldBytes -= oldest.bytes;
            overflowed += 1;
        }

        if (overflowed > 0) {
            countLost(signal, 'overflowed', overflowed);
        }
    }

    function send(named: RecordDestination<TRecord>, records: TRecord[], done: (result: ExportResult) => void): void {
        named.export(records, (result) => {
            if (result.code !== ExportResultCode.SUCCESS) {
                countLost(signal, 'export_failed', records.length);
            }

            done(result);
        });
    }

    return {
        export(records, done) {
            if (destination === null) {
                keep(records);

                // Answered as a success because it is one: the records are held rather than lost, and the processor
                // that handed them over is free to let go of them. A failure would say the opposite about a safe batch,
                // and the processor would drop them for good rather than hand them back.
                done({ code: ExportResultCode.SUCCESS });

                return;
            }

            send(destination, records, done);
        },

        forceFlush: () => destination?.forceFlush?.() ?? Promise.resolve(),

        shutdown() {
            destination = null;
            held = [];
            heldBytes = 0;

            return Promise.resolve();
        },

        exportTo(named) {
            destination = named;

            const waiting = held.map((entry) => entry.record);

            held = [];
            heldBytes = 0;

            if (waiting.length === 0) {
                return Promise.resolve();
            }

            return new Promise((answered) => {
                send(named, waiting, () => {
                    answered();
                });
            });
        },

        hold() {
            destination = null;
        },
    };
}

/**
 * Reports records this client could not deliver, as a count rather than as a log line.
 *
 * A log line per drop is the shape this deliberately refuses: the condition it describes is a burst by definition, so
 * it would arrive as the loudest thing in a deployment's log at exactly the moment the client is least able to send
 * anything. A counter says the same thing in one time series, and the two conditions are separable on it.
 */
function countLost(signal: Signal, condition: Loss, records: number): void {
    metrics
        .getMeter(telemetryName)
        .createCounter('mailfathom.client.telemetry.dropped', { unit: '{record}' })
        .add(records, {
            'mailfathom.client.signal': signal,
            'mailfathom.client.telemetry.condition': condition,
        });
}

function spanBytes(span: ReadableSpan): number {
    // The client opens no span events and no links, so what a span carries beyond its name and its attributes is the
    // fixed part the overhead above allows for.
    return recordOverheadBytes + span.name.length + attributeBytes(span.attributes);
}

function logRecordBytes(record: ReadableLogRecord): number {
    return recordOverheadBytes + textBytes(record.body) + attributeBytes(record.attributes);
}

function attributeBytes(attributes: Readonly<Record<string, unknown>>): number {
    let bytes = 0;

    for (const [name, value] of Object.entries(attributes)) {
        bytes += name.length + textBytes(value);
    }

    return bytes;
}

// A number, a boolean, and an absent value each encode to a handful of bytes whatever they hold, so only text is
// measured and everything else is allowed for at the widest of them.
function textBytes(value: unknown): number {
    return typeof value === 'string' ? value.length : 8;
}
