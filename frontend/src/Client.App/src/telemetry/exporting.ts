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

// Everything that turns what the client records into batches on the wire: the three providers, the three exporters,
// and the resource they all carry. It is a module of its own because `clientTelemetry.ts` reaches it through a dynamic
// import, which is what keeps this out of the chunk a person waits for.
//
// The whole of it is the OpenTelemetry SDK, which is an order of magnitude heavier than the API in front of it, and
// none of it can do anything until somebody has signed in — there is no destination to export to and no credential to
// present before that. So it is fetched at the moment it becomes useful rather than at the moment the client opens,
// and a person who never signs in never downloads it. That is the whole of the split; nothing else here is deferred.

/** What this stack calls itself on every record it exports, which is a client rather than the deployment behind it. */
const clientServiceName = 'mailfathom-client';

/**
 * Registers the three providers for one session and answers with what unregisters them.
 *
 * They are built per session rather than once, because what a batch is exported to and what it presents both belong to
 * the session: pointing the client elsewhere, or signing out, must not leave a queue addressed to the deployment
 * somebody has left. Shutting each provider down flushes what it still holds, which is what keeps the last thing a
 * session recorded — usually why it ended — from being the one record that never leaves.
 */
export function startExporting(session: ClientSession): () => void {
    const resource = clientResource();
    const endpoints = telemetryEndpoints(session);

    // The finished header value, exactly as every other request to this surface carries it and composed no more here
    // than anywhere else. Nothing else is added: the receiver decides whose telemetry this is from the credential.
    const headers = { Authorization: session.authorization };

    const traces = new WebTracerProvider({
        resource,
        spanProcessors: [new BatchSpanProcessor(new OTLPTraceExporter({ url: endpoints.traces, headers }))],
    });

    const meters = new MeterProvider({
        resource,
        readers: [
            new PeriodicExportingMetricReader({
                exporter: new OTLPMetricExporter({ url: endpoints.metrics, headers }),
            }),
        ],
    });

    const loggers = new LoggerProvider({
        resource,
        processors: [new BatchLogRecordProcessor({ exporter: new OTLPLogExporter({ url: endpoints.logs, headers }) })],
    });

    traces.register();
    metrics.setGlobalMeterProvider(meters);
    logs.setGlobalLoggerProvider(loggers);

    return () => {
        trace.disable();
        metrics.disable();
        logs.disable();

        void traces.shutdown();
        void meters.shutdown();
        void loggers.shutdown();
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
