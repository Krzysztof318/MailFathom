// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { createContext, useContext } from 'react';
import { metrics, trace } from '@opentelemetry/api';
import { logs, SeverityNumber } from '@opentelemetry/api-logs';
import { telemetryName, type ClientSession } from '@mailfathom/client-backend';

// The client's one telemetry pipeline: the three signals, one resource, and one place any of it is composed. Every
// screen below receives what it needs as the value this module publishes rather than reaching for a registry of its
// own, which is what keeps a component from deciding anything about how this client is observed.
//
// It begins when somebody has signed in, and that is the shape rather than an omission. The exporter reaches the
// deployment's own OTLP receiver on the client surface and authenticates there exactly as every read does, so there is
// no destination to export to and no credential to present until a session exists. What a client records before that
// is #1230's, and turning any of this off is #1232's.
//
// Nothing here records what was on the screen. A space is named, a route template is named, and an occurrence is
// named; no address, no message, no correspondent, no search text, and no part of a credential reaches a span, a
// measurement, or a log record.

/** Something that happened to this client's session, which is an occurrence rather than a quantity to measure. */
export type ClientEvent = 'session_started' | 'credential_no_longer_accepted';

/**
 * What the application may report about itself.
 *
 * It is one value handed down from the composition root, so a screen that reports something takes this and a screen
 * that reports nothing never learns the pipeline exists. `noTelemetry` is what it is before anything composes one, and
 * it is the default rather than a throwing context on purpose: telemetry is the one thing in this client that must
 * never be the reason a screen fails to render.
 */
export interface ClientTelemetry {
    /**
     * Begins exporting everything this client records for one signed-in session, and answers with what ends it.
     *
     * A session of `null` exports nothing and answers with a teardown that does nothing, which is what a client that
     * has signed out or has not signed in yet passes.
     */
    readonly exportFor: (session: ClientSession | null) => () => void;

    /** A person reached a space, having asked for it at `askedAt` — an epoch instant in milliseconds. */
    readonly navigated: (space: string, askedAt: number) => void;

    /** Records that something happened to the session, with no measurement attached to it. */
    readonly happened: (event: ClientEvent) => void;
}

export const noTelemetry: ClientTelemetry = {
    exportFor: () => () => undefined,
    navigated: () => undefined,
    happened: () => undefined,
};

export const TelemetryContext = createContext<ClientTelemetry>(noTelemetry);

export function useTelemetry(): ClientTelemetry {
    return useContext(TelemetryContext);
}

const nothingToStop = (): void => undefined;

/** Composes the client's pipeline for the head this bundle is running in, which is the whole of the composition. */
export function clientTelemetryForThisApplication(): ClientTelemetry {
    // The document arrives once per run, so what it cost is reported once rather than on every session a run holds.
    let arrivalReported = false;

    // Starting and stopping are put in a queue rather than run where they were asked for, and that is a correctness
    // rule rather than tidiness. The SDK behind a pipeline is fetched rather than bundled — see `exporting.ts` for
    // why — so a start does not finish in the turn it was asked in, while signing out and signing in again asks for a
    // stop and the next start in the same turn. Left to race, the stop would land after the second start and take the
    // registries away from the session that is now signed in, and the run would export nothing for the rest of its
    // life. Serialized, each step sees the one before it finished, and only one pipeline is ever registered — which is
    // the other half of it, since a second registration over a live one is refused rather than replacing it.
    let queued: Promise<() => void> = Promise.resolve(nothingToStop);

    function next(step: (stopRunning: () => void) => Promise<() => void> | (() => void)): void {
        // Telemetry is never the reason a screen fails, and the chunk this waits on is fetched over a network that can
        // refuse it. A step that threw therefore leaves the queue with nothing running rather than a rejection nothing
        // handles, and the client goes on recording into registries that answer no one.
        queued = queued.then(step).catch(() => nothingToStop);
    }

    return {
        exportFor(session) {
            if (session === null) {
                return nothingToStop;
            }

            next(async (stopRunning) => {
                stopRunning();

                const { startExporting } = await import('./exporting');
                const stop = startExporting(session);

                if (!arrivalReported) {
                    arrivalReported = true;
                    reportArrival(session);
                }

                return stop;
            });

            return () => {
                next((stopRunning) => {
                    stopRunning();

                    return nothingToStop;
                });
            };
        },

        navigated(space, askedAt) {
            const at = { 'mailfathom.client.space': space };
            const reached = performance.timeOrigin + performance.now();

            trace
                .getTracer(telemetryName)
                .startSpan(`navigate ${space}`, { startTime: askedAt, attributes: at })
                .end(reached);

            const meter = metrics.getMeter(telemetryName);
            meter.createCounter('mailfathom.client.navigations').add(1, at);
            meter
                .createHistogram('mailfathom.client.navigation.duration', { unit: 's' })
                .record((reached - askedAt) / 1_000, at);
        },

        happened(event) {
            logs.getLogger(telemetryName).emit({
                severityNumber: severities[event],
                body: bodies[event],
                attributes: { 'mailfathom.client.event': event },
            });
        },
    };
}

const severities: Readonly<Record<ClientEvent, SeverityNumber>> = {
    session_started: SeverityNumber.INFO,
    credential_no_longer_accepted: SeverityNumber.WARN,
};

// Written for whoever reads a collector rather than for anybody on a screen, which is why these are not catalogue
// entries: a log record is an operator's, and the deployment it reaches reads in one language.
const bodies: Readonly<Record<ClientEvent, string>> = {
    session_started: 'A client session began.',
    credential_no_longer_accepted: 'The deployment stopped accepting the credential this session held.',
};

/**
 * Reports how long this client took to arrive, where that is a question about the deployment rather than about a disk.
 *
 * The browser times every document it loads, so what decides whether that number means anything is where the document
 * came from: the entry measures a deployment answering only when the deployment is what served it. A desktop shell
 * serves the same document out of the bundle it packages, and a development server serves it beside a deployment it
 * merely points at — both would put a disk read on a histogram of network arrivals, which is worse than a histogram
 * they do not contribute to. So the measurement is absent there rather than reported as a zero.
 *
 * What is asked is therefore whether the document's own origin is the deployment this session is signed in to, which
 * is a comparison of two addresses rather than a question about a head — a shell that serves the bundle over
 * `http://tauri.localhost` answers it exactly as one serving from a scheme of its own does.
 */
function reportArrival(session: ClientSession): void {
    if (window.location.origin !== new URL(session.baseAddress).origin) {
        return;
    }

    const [arrival] = performance.getEntriesByType('navigation');

    if (arrival === undefined || arrival.duration <= 0) {
        return;
    }

    metrics
        .getMeter(telemetryName)
        .createHistogram('mailfathom.client.arrival.duration', { unit: 's' })
        .record(arrival.duration / 1_000);
}
