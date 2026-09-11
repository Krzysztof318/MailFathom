// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { context, metrics, SpanStatusCode, trace } from '@opentelemetry/api';
import { logs, SeverityNumber } from '@opentelemetry/api-logs';
import type { ClientFailureReason, ClientResult } from './failure';
import { routeFor, type DeploymentAddress } from './session';

// What this client reports about itself: the vocabulary of occurrences it may record, the severity each one is written
// at, the floor below which none of them is written at all, and the spans and measurements around the requests this
// package makes. `@opentelemetry/api` and `@opentelemetry/api-logs` are the two dependencies here and neither names a
// browser API: both are a registry and a set of interfaces, and the implementations behind them are registered once by
// the application. So the boundary this package holds is unchanged — the request still goes out through a transport the
// caller supplied, and what is added is a record of how it went.
//
// The vocabulary lives here rather than in the application because both halves of the client write into it, and a
// vocabulary declared where one of its writers happens to live is one that grows a second spelling the day the other
// one needs a name. The severity of an occurrence is decided here for the same reason: what an operator filters on is
// the severity, so it is a property of the occurrence rather than of the place that noticed it.
//
// Nothing here writes a path, a message identifier, a body, a header, or a credential into a span, a measurement, or a
// log record. A request is named by the route template it was composed from, which is what makes the name a dimension a
// dashboard can group by rather than one value per message.

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
 * The least severe log record a deployment asks its clients to write, or that it wants none at all.
 *
 * It is the deployment's rather than a person's or a device's, which is what makes it usable: the question the floor
 * answers is how much a collector serving many clients should be told, and only the deployment can see how many there
 * are. A person's own answer is the switch on the settings screen and is a different question — whether to be reported
 * on at all — so neither is read as the other, and the client stands on {@link defaultTelemetryLevel} until the
 * deployment has said.
 */
export type DeploymentTelemetryLevel = 'off' | 'trace' | 'debug' | 'info' | 'warn' | 'error' | 'fatal';

/** What a client records at before any deployment has answered, which is what a collector keeps by default. */
export const defaultTelemetryLevel: DeploymentTelemetryLevel = 'info';

const levelFloors: Readonly<Record<DeploymentTelemetryLevel, number>> = {
    trace: SeverityNumber.TRACE,
    debug: SeverityNumber.DEBUG,
    info: SeverityNumber.INFO,
    warn: SeverityNumber.WARN,
    error: SeverityNumber.ERROR,
    fatal: SeverityNumber.FATAL,

    // Above every severity rather than a flag beside them, so refusing everything is the same comparison as refusing
    // what is merely too quiet and there is no second way for a record to be admitted.
    off: Number.POSITIVE_INFINITY,
};

/** Whether a value is one of the levels a deployment may answer with, which is what an answer from the wire is read against. */
export function isDeploymentTelemetryLevel(value: unknown): value is DeploymentTelemetryLevel {
    return typeof value === 'string' && Object.hasOwn(levelFloors, value);
}

// Module state rather than a value handed down, for the reason the OpenTelemetry registries this module reads are: a
// record is written from wherever the thing happened, and threading a floor to every one of those places would be
// threading the pipeline itself through a package whose whole boundary is that it holds none of it.
let floor = levelFloors[defaultTelemetryLevel];

/**
 * States the floor every record written from either half of this client is held to from now on.
 *
 * @param level What the deployment asked for, or `off` where it forwards nothing or the person declined.
 */
export function recordDownTo(level: DeploymentTelemetryLevel): void {
    floor = levelFloors[level];
}

/** Whether a record of that severity would be written at all, which is what a caller asks before composing one. */
export function worthRecording(severity: SeverityNumber): boolean {
    // Widened rather than compared as it stands, because `off` has no member of the enum to be and is the ceiling
    // above every severity there is.
    const written: number = severity;

    return written >= floor;
}

/**
 * Something this client may record, which is the whole of the vocabulary and a closed set on purpose.
 *
 * A closed set because an occurrence name reaches a collector as a dimension an operator groups by: a name composed
 * where something happened is one time series per caller, and one spelled differently by the two halves of this client
 * is two panels for one thing. Adding a member is therefore a decision taken here, beside the severity it is written at
 * and the sentence a collector reads it as.
 */
export type ClientEvent =
    | 'session_started'
    | 'session_ended'
    | 'deployment_read'
    | 'signed_in'
    | 'sign_in_refused'
    | 'credential_no_longer_accepted'
    | 'render_failed'
    | 'request_completed'
    | 'request_failed'
    | 'signals_opened'
    | 'signals_dropped'
    | 'signals_refused'
    | 'signals_unreachable'
    | 'signal_received'
    | 'signal_refused'
    | 'navigated'
    | 'act_asked'
    | 'act_refused';

/**
 * What each occurrence is written at.
 *
 * Read it as the answer to *who is this for*. `INFO` is what a collector keeps by default from every signed-in client
 * at once, so exactly one occurrence is written there — a session beginning — and everything that happens more than
 * once a session sits below it. `DEBUG` is the client's own account of what it did, which is what an operator lowers
 * the floor to when somebody reports something; `TRACE` is the stream beneath that, a record per request and one per
 * move between screens, which is only ever worth having for one client at a time. Above `INFO` is what an operator
 * would act on rather than read: a credential a deployment stopped accepting, a hub that has been unreachable for long
 * enough that the interval is all a client has left, and an act a deployment refused after the screen had already drawn
 * it as done.
 */
export const severityOf: Readonly<Record<ClientEvent, SeverityNumber>> = {
    session_started: SeverityNumber.INFO,

    session_ended: SeverityNumber.DEBUG,
    deployment_read: SeverityNumber.DEBUG,
    signed_in: SeverityNumber.DEBUG,
    sign_in_refused: SeverityNumber.DEBUG,
    request_failed: SeverityNumber.DEBUG,
    signals_opened: SeverityNumber.DEBUG,
    signals_dropped: SeverityNumber.DEBUG,
    signals_refused: SeverityNumber.DEBUG,
    signal_refused: SeverityNumber.DEBUG,
    act_asked: SeverityNumber.DEBUG,

    request_completed: SeverityNumber.TRACE,
    signal_received: SeverityNumber.TRACE,
    navigated: SeverityNumber.TRACE,

    credential_no_longer_accepted: SeverityNumber.WARN,
    signals_unreachable: SeverityNumber.WARN,
    act_refused: SeverityNumber.WARN,

    render_failed: SeverityNumber.ERROR,
};

// Written for whoever reads a collector rather than for anybody on a screen, which is why these are not catalogue
// entries: a log record is an operator's, and the deployment it reaches reads in one language.
const bodyOf: Readonly<Record<ClientEvent, string>> = {
    session_started: 'A client session began.',
    session_ended: 'A client session ended and the client stopped exporting for it.',
    deployment_read: 'A client read what the deployment says about itself and about the credential it presented.',
    signed_in: 'Somebody signed this client in and the deployment issued it a credential.',
    sign_in_refused: 'A sign-in did not produce a credential.',
    credential_no_longer_accepted: 'The deployment stopped accepting the credential this session held.',
    render_failed: 'A region of the client failed while it was being drawn, and the boundary around it contained it.',
    request_completed: 'The client made a request to the deployment and read what came back.',
    request_failed: 'A request the client made did not produce an answer it could act on.',
    signals_opened: 'A connection to the deployment signal hub stands.',
    signals_dropped: 'A connection to the deployment signal hub ended.',
    signals_refused: 'A connection to the deployment signal hub could not be opened.',
    signals_unreachable:
        'The deployment signal hub has refused every attempt for long enough that the client is reading on its own interval alone.',
    signal_received: 'The deployment said something changed.',
    signal_refused: 'The deployment sent a payload on the signal hub that this client does not act on.',
    navigated: 'Somebody moved to another space in the client.',
    act_asked: 'The client asked the deployment to change something about a mailbox.',
    act_refused: 'The deployment refused a change the client had already drawn, and the client put the screen back.',
};

/** One record, as the half of the client that noticed the occurrence describes it. */
export interface ClientEventRecord {
    /** Which occurrence it is, which reaches the collector as `mailfathom.client.event`. */
    readonly event: ClientEvent;

    /**
     * What else may be said about it, every value of which is drawn from a closed set or is a plain count.
     *
     * Nothing unbounded reaches one. No address, subject, correspondent, search text, message identifier, folder name,
     * or part of a credential is written here by anything, and what is written instead is the vocabulary a dashboard
     * already groups by — a route template, an outcome, a failure reason, a space name, a signal kind.
     */
    readonly attributes?: Readonly<Record<string, string | number>>;

    /**
     * What to write it at, where the occurrence alone does not settle it.
     *
     * Exactly one occurrence needs this and it is `render_failed`: the containment boundary around the whole
     * application failing is a client nobody can use rather than a region nobody can see, which is the one thing this
     * client has to say that `ERROR` understates. Two names for one occurrence would split a panel in half to record
     * the difference an attribute beside it already carries.
     */
    readonly severity?: SeverityNumber;

    /**
     * When it happened, as an epoch instant in milliseconds, where that is not when the record is written.
     *
     * The application queues its records behind whatever its pipeline is waiting on, so a record timestamped where it
     * was written would say a session began at the moment the client next got a word in.
     */
    readonly at?: number;
}

/**
 * Writes one record, unless the deployment asked for nothing that quiet.
 *
 * @param record What happened.
 */
export function writeClientEvent(record: ClientEventRecord): void {
    const severity = record.severity ?? severityOf[record.event];

    if (!worthRecording(severity)) {
        return;
    }

    logs.getLogger(telemetryName).emit({
        // Stamped here rather than left for the SDK, which would read the clock where the record reaches a processor.
        // For a record written from this package that is the same instant; for one the application queued behind its
        // pipeline it is not, which is why `at` is stated at all.
        timestamp: record.at ?? Date.now(),
        severityNumber: severity,
        body: bodyOf[record.event],
        attributes: { 'mailfathom.client.event': record.event, ...record.attributes },
    });
}

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
 * @param failureOf Which of the five failure reasons the answer amounts to, or `null` where the client got an answer it
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

        // Beside the span and the two measurements rather than instead of them, and below the floor a collector keeps
        // by default so that it costs an ordinary deployment nothing. What it adds is the one thing a span cannot be
        // read for after the fact: an operator reading one client's records in order sees what it asked for, in what
        // order, and where the sequence stopped making sense — which is the account somebody reporting a stuck screen
        // cannot give. A failure is a record of its own and one level above the rest, because the interesting requests
        // are rare enough to be worth having without the stream around them.
        writeClientEvent({
            event: failure === null ? 'request_completed' : 'request_failed',
            attributes: { ...outcome, 'mailfathom.client.request.duration_ms': Date.now() - startedAt },
        });

        return answer;
    } finally {
        span.end();
    }
}
