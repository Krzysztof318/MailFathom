// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { createContext, useContext } from 'react';
import { metrics, trace } from '@opentelemetry/api';
import { logs, SeverityNumber } from '@opentelemetry/api-logs';
import { telemetryName, type ClientSession } from '@mailfathom/client-backend';
import { isSpace } from '../routing/spaces';
import type { ClientPipeline } from './exporting';

// The client's one telemetry pipeline: the three signals, one resource, and one place any of it is composed. Every
// screen below receives what it needs as the value this module publishes rather than reaching for a registry of its
// own, which is what keeps a component from deciding anything about how this client is observed.
//
// Recording begins where this is composed and exporting begins when somebody signs in, and the gap between the two is
// the point of it. The exporter reaches the deployment's own OTLP receiver on the client surface and authenticates
// there exactly as every read does, so there is no destination and no credential to present until a session exists —
// but starting up, resolving which deployment this client belongs to, and a sign-in that did not succeed are exactly
// the failures somebody cannot describe, and they are invisible to the deployment because nothing reached it. So
// `holding.ts` holds them in memory, bounded, until there is a session to attribute them to.
//
// Whether any of it happens at all is the person's, and it arrives here beside the session through `exportFor`. Off
// stops the recording rather than filtering the export, and what was held from before that answer arrived is discarded
// rather than flushed: a client that has never been told stands on the deployment's unset answer and records into that
// buffer, so the first thing somebody who wants none of this says has to reach the buffer as well as the wire.
//
// Nothing here records what was on the screen. A space is named, a route template is named, and an occurrence is
// named; no address, no message, no correspondent, no search text, and no part of a credential reaches a span, a
// measurement, or a log record.

/** Something that happened to this client's session, which is an occurrence rather than a quantity to measure. */
export type ClientEvent = 'session_started' | 'credential_no_longer_accepted';

/** What a move is reported as where the address named something this client does not publish as a space. */
export const unnamedSpace = 'other';

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
     * Begins exporting everything this client has recorded and records next for one signed-in session, and answers
     * with what ends it.
     *
     * A session of `null` exports nothing and answers with a teardown that does nothing, which is what a client that
     * has signed out or has not signed in yet passes. The pipeline goes on recording either way.
     *
     * @param session Who is signed in and where their telemetry goes, or `null` where nobody is.
     * @param permitted Whether this person has agreed to be reported on at all. It is stated here rather than through
     * a setter of its own because it answers the same question as which session is exporting — one caller states both
     * from one effect, and two ways of saying "stop" would be two orderings to reason about. `false` records nothing
     * from the moment it is stated and discards what was held before it, which is the difference between a switch and
     * a filter on the way out.
     */
    readonly exportFor: (session: ClientSession | null, permitted: boolean) => () => void;

    /**
     * A person reached a space, having asked for it at `askedAt` — an epoch instant in milliseconds.
     *
     * The space is redacted to {@link unnamedSpace} unless it is one the client publishes, so an address naming
     * something else — a message, a folder, anything a later route could carry — cannot become a span name or a
     * dimension value. The argument is a string rather than the space type deliberately: a compiler refusal would hold
     * only for what is written today, and what this has to survive is a caller reached from an address.
     */
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
    // It latches on the measurement having been taken on, never on a session merely having asked: a run can be pointed
    // at another deployment without reloading, so the session that first asks may be one this measurement is not about
    // and the next one the one it is.
    let arrivalReported = false;

    // Recording starts here, which is one call into the composition root and before any screen exists. The SDK behind
    // it is fetched rather than bundled — see `exporting.ts` for why — so the registries answer no one for as long as
    // that takes, and a run whose network refuses the chunk records nothing rather than failing.
    let running: Promise<ClientPipeline | null> = import('./exporting')
        .then(({ startRecording }) => startRecording())
        .catch(() => null);

    // Whether anything may be recorded at all. It begins permitted because that is the deployment's own unset answer,
    // and the frame states the remembered one on its first effect — before any request has come back, which is what
    // makes a restart honour a decision rather than record until it is confirmed.
    let permitted = true;

    // Everything this module does is put in a queue rather than run where it was asked for, and that is a correctness
    // rule rather than tidiness. A registry answers whoever is registered at the instant it is asked, and a record
    // written before the pipeline arrives is dropped for good — there is no retroactive delivery once the real
    // provider is there. A screen reports the moment it has something to report, which for the session beginning is
    // the same turn the deployment was resolved in, so writing where it was asked would drop exactly the records a
    // cold start produces. Serializing also settles signing out and straight back in, which asks for the hold and the
    // next export in one turn: left to race, the hold would land last and leave the session that is signed in holding.
    function next(step: (pipeline: ClientPipeline | null) => void | Promise<void>): void {
        running = running.then(async (pipeline) => {
            try {
                await step(pipeline);
            } catch {
                // Telemetry is never the reason a screen fails. It is a record that was not written, which is what
                // the deployment sees too.
            }

            return pipeline;
        });
    }

    // A record goes through the same queue and is refused before it joins it, which is what makes the switch a switch:
    // nothing is written into a provider, so there is nothing for a later export to carry and nothing in the buffer
    // for a person to have to trust an exporter about.
    function record(write: () => void): void {
        if (!permitted) {
            return;
        }

        next(write);
    }

    return {
        exportFor(session, allowed) {
            permitted = allowed;

            if (!allowed) {
                // Read rather than exported, and read before anything else this queue holds: what was recorded while
                // the deployment had said nothing is exactly what a person turning this off has not agreed to, so it
                // leaves the buffer without ever having been addressed.
                next((pipeline) => pipeline?.discard());

                return nothingToStop;
            }

            if (session === null) {
                return nothingToStop;
            }

            next(async (pipeline) => {
                // Reported before the flush rather than after it, so the export a sign-in produces carries the start
                // it followed rather than leaving it for whatever goes out a minute later.
                if (!arrivalReported) {
                    arrivalReported = reportArrival(session);
                }

                await pipeline?.exportTo(session);
            });

            return () => {
                next((pipeline) => pipeline?.hold());
            };
        },

        navigated(space, askedAt) {
            const named = isSpace(space) ? space : unnamedSpace;
            const at = { 'mailfathom.client.space': named };

            // Read where the move ended rather than where it was written, so queueing the write moves when the record
            // reaches a registry and not what the record says.
            const reached = performance.timeOrigin + performance.now();

            record(() => {
                trace
                    .getTracer(telemetryName)
                    .startSpan(`navigate ${named}`, { startTime: askedAt, attributes: at })
                    .end(reached);

                const meter = metrics.getMeter(telemetryName);
                meter.createCounter('mailfathom.client.navigations').add(1, at);
                meter
                    .createHistogram('mailfathom.client.navigation.duration', { unit: 's' })
                    .record((reached - askedAt) / 1_000, at);
            });
        },

        happened(event) {
            // Read where it happened rather than where it was written, for the reason a move above is: the queue may
            // be waiting on an export the deployment is slow to answer, and a record timestamped then would say the
            // session began at the moment the client next got a word in.
            const at = performance.timeOrigin + performance.now();

            record(() => {
                logs.getLogger(telemetryName).emit({
                    timestamp: at,
                    severityNumber: severities[event],
                    body: bodies[event],
                    attributes: { 'mailfathom.client.event': event },
                });
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
 *
 * @returns Whether this run's measurement has now been taken on, which a session the measurement is not about never
 * answers: a client pointed somewhere else mid-run signs into a second deployment without the document being fetched
 * again, and the one that serves it may be the second.
 */
function reportArrival(session: ClientSession): boolean {
    if (window.location.origin !== new URL(session.baseAddress).origin) {
        return false;
    }

    if (document.readyState === 'complete') {
        recordArrival();

        return true;
    }

    // The navigation entry is not finished until the load event has, and a session restored from a stored credential
    // reaches this before that: read then, the entry describes a document still arriving and answers zero. Waiting is
    // what makes the measurement one a run either takes or genuinely cannot have, rather than one it happened to ask
    // for too early — a client signed in from storage gets exactly one sign-in, so a second attempt never comes.
    window.addEventListener('load', recordArrival, { once: true });

    return true;
}

function recordArrival(): void {
    const [arrival] = performance.getEntriesByType('navigation');

    if (arrival === undefined || arrival.duration <= 0) {
        return;
    }

    metrics
        .getMeter(telemetryName)
        .createHistogram('mailfathom.client.arrival.duration', { unit: 's' })
        .record(arrival.duration / 1_000);
}
