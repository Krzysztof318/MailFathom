// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { context, metrics, SpanStatusCode, trace } from '@opentelemetry/api';
import type { ClientFailureReason, ClientResult } from './failure';
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
 * Records one request to the client surface answered as a `ClientResult`, which is most of them.
 *
 * It is `reported` with the failure read off the result this package's own contract carries, and it exists as a name of
 * its own because that is the shape all but five operations here answer in: repeating the reading at each of them would
 * be one more place for the outcome vocabulary to drift.
 *
 * @param request The route template this asks for, method first, which is the dimension every record here is grouped
 * by. It is a template rather than the composed path: a message identifier in a span name is one name per message.
 * @param ask The operation, which answers a value rather than throwing for anything it expected.
 */
export function spanned<TValue>(
    request: string,
    ask: () => Promise<ClientResult<TValue>>,
): Promise<ClientResult<TValue>> {
    return reported(request, ask, (result) => (result.outcome === 'failed' ? result.failure.reason : null));
}

/**
 * Records one request to the client surface, and answers exactly what the operation answered.
 *
 * Every request this package composes goes through this rather than reporting for itself, which is what keeps one span
 * and one pair of measurements per request whatever shape the operation answers in. The span ends where the outcome is
 * decided rather than where the response arrived, because the failure a screen acts on — a body this package refused
 * as unreadable among them — is not known until then.
 *
 * The span is also the active context the operation runs under, which is what carries it onto the wire: `headersFor`
 * writes the W3C trace context of whatever is active into the request's headers, so the deployment's span for that
 * request is this span's child and one trace covers the screen, the request, the use case, and the query beneath it.
 * A request the application puts on the wire itself is reported the same way, because what `ask` runs for those is the
 * composition *and* the send: the operation that binds them is here, so the span's name and its end are this package's
 * decision rather than the caller's, and the composition happens inside the active context like every other one.
 *
 * @param request The route template this asks for, method first, which is the dimension every record here is grouped
 * by. It is a template rather than the composed path: a message identifier in a span name is one name per message.
 * @param ask The operation, which answers a value rather than throwing for anything it expected.
 * @param failureOf Which of the four failure reasons the answer amounts to, or `null` where the client got an answer it
 * acts on. It is the operation's reading rather than a status, and the distinction it draws is whether an answer
 * arrived rather than whether the answer was yes: a name this deployment will not record, a person with no portrait
 * stored, and a download somebody stopped are each answered and acted on, so each is a `read`.
 */
export async function reported<TOutcome>(
    request: string,
    ask: () => Promise<TOutcome>,
    failureOf: (outcome: TOutcome) => ClientFailureReason | null,
): Promise<TOutcome> {
    // Both registries are asked for per request rather than held at module scope. A provider is registered once the
    // person has signed in, which is after this module was first evaluated, and an instrument taken from the registry
    // that stood before it would report to nothing for the rest of the run. Neither lookup does more than read a map.
    const span = trace.getTracer(telemetryName).startSpan(request);
    const meter = metrics.getMeter(telemetryName);
    const startedAt = Date.now();

    try {
        // The operation runs with this span as the active context, which is what `headersFor` reads the trace context
        // out of and therefore what joins this span to the one the deployment opens for the request. Without it the
        // span would still be recorded and exported, and would sit beside the service's work rather than above it.
        const answer = await context.with(trace.setSpan(context.active(), span), ask);
        const failure = failureOf(answer);
        const outcome =
            failure === null
                ? { 'mailfathom.client.request': request, 'mailfathom.client.outcome': 'read' }
                : {
                      'mailfathom.client.request': request,
                      'mailfathom.client.outcome': 'failed',
                      'mailfathom.client.failure': failure,
                  };

        span.setAttributes(outcome);

        if (failure !== null) {
            // No message on the status: what a failure was is already the dimension beside it, and anything longer
            // would be a sentence composed here about an answer this package deliberately does not carry out of itself.
            span.setStatus({ code: SpanStatusCode.ERROR });
        }

        meter.createCounter(requestCount).add(1, outcome);
        meter.createHistogram(requestDuration, { unit: 's' }).record((Date.now() - startedAt) / 1_000, outcome);

        return answer;
    } finally {
        span.end();
    }
}
