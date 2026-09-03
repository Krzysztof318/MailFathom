// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { metrics, trace } from '@opentelemetry/api';
import { logs } from '@opentelemetry/api-logs';
import { OTLPLogExporter } from '@opentelemetry/exporter-logs-otlp-proto';
import { OTLPMetricExporter } from '@opentelemetry/exporter-metrics-otlp-proto';
import { OTLPTraceExporter } from '@opentelemetry/exporter-trace-otlp-proto';
import { defaultResource, resourceFromAttributes, type Resource } from '@opentelemetry/resources';
import { BatchLogRecordProcessor, LoggerProvider } from '@opentelemetry/sdk-logs';
import { MeterProvider, PeriodicExportingMetricReader } from '@opentelemetry/sdk-metrics';
import { BatchSpanProcessor, WebTracerProvider } from '@opentelemetry/sdk-trace-web';
import { telemetryEndpoints, type ClientSession } from '@mailfathom/client-backend';
import { heldLogRecordExporter, heldMetricExporter, heldSpanExporter } from './holding';

// Everything that turns what the client records into batches on the wire: the three providers, the three exporters,
// and the resource they all carry. It is a module of its own because `clientTelemetry.ts` reaches it through a dynamic
// import, which is what keeps the SDK out of the chunk a person waits for — it is an order of magnitude heavier than
// the API in front of it, and the client is on the screen before any of it is needed.
//
// It is fetched as the client starts rather than at a sign-in, because recording begins at the composition root: the
// three providers are registered immediately, against the holding exporters in `holding.ts`, so a start and a sign-in
// that failed are recorded rather than lost. What decides whether any of it reaches the network is a destination,
// which exists only while somebody is signed in.

/** What this stack calls itself on every record it exports, which is a client rather than the deployment behind it. */
const clientServiceName = 'mailfathom-client';

/** The one pipeline this client records into, and the two things a session does to it. */
export interface ClientPipeline {
    /**
     * Points the three signals at the deployment this session is signed in to, and flushes what was held.
     *
     * Everything a signal has been holding leaves in one export, attributed to that session by the credential the
     * export presents. It resolves once those exports have been answered, so a caller can sequence against it.
     */
    readonly exportTo: (session: ClientSession) => Promise<void>;

    /**
     * Returns to holding, which is what signing out and being pointed at another deployment both do.
     *
     * What the session recorded leaves first, under that session's own credential, so the last thing a session
     * recorded — usually why it ended — is not the one record that never leaves.
     */
    readonly hold: () => Promise<void>;

    /**
     * Stops recording altogether and lets the three providers go.
     *
     * The client itself never reaches this: a run records for as long as it is open, and closing the page is what ends
     * it. It is what a suite that registered this pipeline uses to take it away again.
     */
    readonly shutdown: () => Promise<void>;
}

/**
 * Registers the three providers for this run and answers with what a session does to them.
 *
 * They are built once rather than per session, which is the whole of the difference a destination makes: what a batch
 * is exported to and what it presents both belong to the session, but what is recorded belongs to the run. So signing
 * out takes the destination away and leaves the providers recording, and pointing the client elsewhere replaces the
 * destination without ever leaving a queue addressed to a deployment somebody has left.
 */
export function startRecording(): ClientPipeline {
    const resource = clientResource();
    const spans = heldSpanExporter();
    const measurements = heldMetricExporter();
    const records = heldLogRecordExporter();

    const traces = new WebTracerProvider({ resource, spanProcessors: [new BatchSpanProcessor(spans)] });
    const meters = new MeterProvider({
        resource,
        readers: [new PeriodicExportingMetricReader({ exporter: measurements })],
    });
    const loggers = new LoggerProvider({ resource, processors: [new BatchLogRecordProcessor({ exporter: records })] });

    traces.register();
    metrics.setGlobalMeterProvider(meters);
    logs.setGlobalLoggerProvider(loggers);

    let destinations: readonly { readonly shutdown: () => Promise<void> }[] = [];

    async function releaseDestinations(): Promise<void> {
        spans.hold();
        measurements.hold();
        records.hold();

        const leaving = destinations;

        destinations = [];

        await Promise.all(leaving.map((destination) => destination.shutdown()));
    }

    return {
        async exportTo(session) {
            // Anything still queued in the two processors joins what is already held, so the flush below is one export
            // rather than one for the buffer and another for whatever had not reached it yet.
            await Promise.all([traces.forceFlush(), loggers.forceFlush()]);

            const endpoints = telemetryEndpoints(session);

            // The finished header value, exactly as every other request to this surface carries it and composed no more
            // here than anywhere else. Nothing else is added: the receiver decides whose telemetry this is from the
            // credential.
            const headers = { Authorization: session.authorization };

            const exportSpansTo = new OTLPTraceExporter({ url: endpoints.traces, headers });
            const exportMeasurementsTo = new OTLPMetricExporter({ url: endpoints.metrics, headers });
            const exportRecordsTo = new OTLPLogExporter({ url: endpoints.logs, headers });

            destinations = [exportSpansTo, exportMeasurementsTo, exportRecordsTo];

            // The measurements are pointed first and flushed with the rest, because what carries them is the reader's
            // own collection rather than a buffer: the instruments hold cumulative totals, and forcing that collection
            // now is what puts everything recorded since the client opened into the same first export.
            await measurements.exportTo(exportMeasurementsTo);
            await Promise.all([spans.exportTo(exportSpansTo), records.exportTo(exportRecordsTo), meters.forceFlush()]);
        },

        async hold() {
            await Promise.all([traces.forceFlush(), meters.forceFlush(), loggers.forceFlush()]);

            // ponytail: the measurements a session recorded stay in their instruments after it ends, the temporality
            // being cumulative and a provider belonging to the run rather than to a session — so signing out and
            // signing in as somebody else attributes the first person's totals to the second, on a deployment several
            // owners share. Nothing personal travels either way, both being counts over a closed set of route
            // templates and space names, and the spans and the log records are unaffected. Resetting them means a
            // meter provider per session, which the global registry refuses to re-register; #1227 is where that would
            // be taken up if a deployment reads a person's own totals rather than the deployment's.
            await releaseDestinations();
        },

        async shutdown() {
            trace.disable();
            metrics.disable();
            logs.disable();

            await releaseDestinations();
            await Promise.all([traces.shutdown(), meters.shutdown(), loggers.shutdown()]);
        },
    };
}

/**
 * What every record this client exports says about who produced it.
 *
 * It identifies a client and never a person: the three attributes are what this stack is, what version of it is
 * running, and which head it is running in. The receiver on the client surface writes the owner attributes itself,
 * from the credential the export presented, and replaces whatever a page put in their place — so nothing here is the
 * place a person would be named even if something tried.
 */
function clientResource(): Resource {
    return defaultResource().merge(
        resourceFromAttributes({
            // `service.name` and `service.version` are OpenTelemetry's own registry entries and are written out rather
            // than imported: a pin for two strings would cost a row in the register, a line in the census, and a
            // notice inside every bundle this repository publishes.
            'service.name': clientServiceName,
            'service.version': __MAILFATHOM_VERSION__,
            'mailfathom.client.head': runningHead(),
        }),
    );
}

// Reported rather than branched on, and the difference is the whole of why this is allowed to exist. Nothing in this
// client behaves differently for the answer — no component, no hook, and no screen asks it — and what it produces is a
// dimension an operator groups a dashboard by. It reads the same probe `shellOperations/linkOpener.ts` reads, for a
// different question: that module asks whether a shell offered a command, and this one asks what to call the head on a
// record that has already been decided.
function runningHead(): 'desktop' | 'web' {
    return Object.hasOwn(globalThis, '__TAURI_INTERNALS__') ? 'desktop' : 'web';
}
