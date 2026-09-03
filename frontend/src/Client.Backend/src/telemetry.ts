// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { metrics, SpanStatusCode, trace } from '@opentelemetry/api';
import type { ClientResult } from './failure';
import { routeFor, type DeploymentAddress } from './session';

// What this package reports about the requests it makes. `@opentelemetry/api` is the one dependency here and it names
// no browser API: it is a registry and a set of interfaces, and the implementation behind it is registered once by the
// application. So the boundary this package holds is unchanged — the request still goes out through a transport the
// caller supplied, and what is added is a record of how it went.
//
// Nothing here writes a path, a message identifier, a body, a header, or a credential into a span or a measurement. A
// request is named by the route template it was composed from, which is what makes the name a dimension a dashboard
// can group by rather than one value per message.

/**
 * The one name MailFathom publishes spans and instruments under, on this stack as on the service's.
 *
 * `docs/operations/telemetry.md` § *What MailFathom publishes under its own name* is where that decision is recorded,
 * and it is the same string here for the reason it is one string there: an operator filters on one name to see
 * everything this product owns, and a second registration is one more thing to subscribe to before anything arrives.
 */
export const telemetryName = 'MailFathom';

// The prefix the client surface serves an OTLP receiver beneath, which the exporter appends the signal's own path to.
const telemetryRoute = '/telemetry';

/** Where each of the three signals is exported, for a deployment this client is signed in to. */
export function telemetryEndpoints(deployment: DeploymentAddress): {
    readonly traces: string;
    readonly metrics: string;
    readonly logs: string;
} {
    const prefix = routeFor(deployment, telemetryRoute);

    // The paths beneath the prefix are the OTLP specification's own rather than this surface's, which is why they are
    // spelled here instead of composed from anything: an exporter pointed at the prefix appends exactly these.
    return { traces: `${prefix}/v1/traces`, metrics: `${prefix}/v1/metrics`, logs: `${prefix}/v1/logs` };
}

const requestCount = 'mailfathom.client.requests';
const requestDuration = 'mailfathom.client.request.duration';

/**
 * Records one request to the client surface, and answers exactly what the operation answered.
 *
 * Every operation in this package goes through this rather than reporting for itself, which is what keeps one span and
 * one pair of measurements per request whatever the operation did with the answer. The span ends where the result is
 * decided rather than where the response arrived, because the failure a screen acts on — a body this package refused
 * as unreadable among them — is not known until then.
 *
 * @param request The route template this asks for, method first, which is the dimension every record here is grouped
 * by. It is a template rather than the composed path: a message identifier in a span name is one name per message.
 * @param ask The operation, which answers a value rather than throwing for anything it expected.
 */
export async function spanned<TValue>(
    request: string,
    ask: () => Promise<ClientResult<TValue>>,
): Promise<ClientResult<TValue>> {
    // Both registries are asked for per request rather than held at module scope. A provider is registered once the
    // person has signed in, which is after this module was first evaluated, and an instrument taken from the registry
    // that stood before it would report to nothing for the rest of the run. Neither lookup does more than read a map.
    const span = trace.getTracer(telemetryName).startSpan(request);
    const meter = metrics.getMeter(telemetryName);
    const startedAt = Date.now();

    try {
        const result = await ask();
        const outcome =
            result.outcome === 'read'
                ? { 'mailfathom.client.request': request, 'mailfathom.client.outcome': 'read' }
                : {
                      'mailfathom.client.request': request,
                      'mailfathom.client.outcome': 'failed',
                      'mailfathom.client.failure': result.failure.reason,
                  };

        span.setAttributes(outcome);

        if (result.outcome === 'failed') {
            // No message on the status: what a failure was is already the dimension beside it, and anything longer
            // would be a sentence composed here about an answer this package deliberately does not carry out of itself.
            span.setStatus({ code: SpanStatusCode.ERROR });
        }

        meter.createCounter(requestCount).add(1, outcome);
        meter.createHistogram(requestDuration, { unit: 's' }).record((Date.now() - startedAt) / 1_000, outcome);

        return result;
    } finally {
        span.end();
    }
}
