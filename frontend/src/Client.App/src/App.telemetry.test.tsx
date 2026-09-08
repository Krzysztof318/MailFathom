// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { fireEvent, screen, waitFor } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { telemetryKey } from './device/deviceStore';
import {
    accepted,
    deploymentAnswering,
    framed,
    heldPerson,
    heldSession,
    openSettings,
    preferencesAnswering,
    renderApp,
    resetsBetweenTests,
    servedFrom,
    servingAddress,
    sessionAnswering,
    signIn,
    signOut,
    storeKeeping,
    telemetryRecording,
    typedSession,
} from './App.harness';
import { writeKeptSession } from './signIn/keptSession';

// What the frame records, who it records it for, and every answer that stops it recording. The arrangement is
// `App.harness`, which the rest of this family shares.

resetsBetweenTests();

describe('App telemetry', () => {
    it('exports under the session that is signed in, and records that it began', async () => {
        const recording = telemetryRecording();

        renderApp(servedFrom, heldSession, deploymentAnswering(), storeKeeping(), recording.telemetry);
        await framed();

        // Waited for rather than read once the frame is up, for the reason the deployment's own answer is below: what
        // the pipeline is started under is the session, and the summary `framed` waits for is the accounts.
        await waitFor(() => {
            expect(recording.exportedFor.map((session) => session?.baseAddress)).toContain(servingAddress.baseAddress);
        });
        expect(recording.events).toContain('session_started');
    });

    it('stops exporting when the person signs out', async () => {
        const recording = telemetryRecording();

        renderApp(servedFrom, heldSession, deploymentAnswering(), storeKeeping(), recording.telemetry);
        await framed();
        await signOut();

        // Nothing is exported for somebody who is not signed in, so the pipeline the session held is asked to end and
        // what replaces it is asked to export for nobody.
        await waitFor(() => {
            expect(recording.stopped.length).toBeGreaterThan(0);
        });
        expect(recording.exportedFor.at(-1)).toBeNull();
    });

    it('records a credential the deployment has stopped accepting', async () => {
        const recording = telemetryRecording();
        const credentials = storeKeeping();
        await credentials.keep(servingAddress, writeKeptSession(typedSession));

        renderApp(
            servedFrom,
            typedSession,
            deploymentAnswering({ status: 401, body: '' }),
            credentials,
            recording.telemetry,
        );
        await screen.findByText('This deployment has stopped accepting the sign-in that was kept. Sign in again.');

        expect(recording.events).toContain('credential_no_longer_accepted');
    });

    // A deployment that forwards nothing is not known to forward nothing until it says so, and until then the client
    // records into the buffer #1230 holds — which is why what is asserted is where this ends rather than that it never
    // permitted anything. The pipeline throws away what it held when that answer arrives, which `exporting.test.ts`
    // and `holding.test.ts` are what prove.
    it('stops recording against a deployment that says it forwards no telemetry', async () => {
        const recording = telemetryRecording();

        renderApp(
            servedFrom,
            heldSession,
            deploymentAnswering(undefined, sessionAnswering(['mailfathom.mail.read', 'mailfathom.mail.ask'], false)),
            storeKeeping(),
            recording.telemetry,
        );
        await framed();

        // Waited for rather than read once the frame is up: the summary `framed` waits for is the accounts arriving,
        // and what carries the deployment's answer about telemetry is the session — a second read whose answer the
        // frame does not hold the summary back for. Reading the record at that moment reports the value the pipeline
        // started with rather than the one the deployment sent.
        await waitFor(() => {
            expect(recording.permitted.at(-1)).toBe(false);
        });
    });

    // What a restart owes somebody who turned it off on this machine: the decision is honoured from the first effect
    // rather than for the second it takes an answer to come back, and it stands for as long as no answer does.
    it('honours a decision this device remembers while the deployment has answered nothing', async () => {
        window.localStorage.setItem(telemetryKey(heldPerson), 'false');

        const recording = telemetryRecording();

        renderApp(servedFrom, heldSession, deploymentAnswering(), storeKeeping(), recording.telemetry);
        await framed();

        expect(recording.permitted).not.toContain(true);
        expect(recording.events).not.toContain('session_started');
    });

    // The other half of that: the device's copy is a cache and not a second opinion, so the deployment's own answer
    // replaces it — which is what makes a decision taken on one machine reach this one.
    it('lets the deployment replace what this device remembers once it answers', async () => {
        window.localStorage.setItem(telemetryKey(heldPerson), 'false');

        const recording = telemetryRecording();

        renderApp(
            servedFrom,
            heldSession,
            deploymentAnswering(undefined, accepted, preferencesAnswering(true)),
            storeKeeping(),
            recording.telemetry,
        );
        await framed();

        expect(recording.permitted[0]).toBe(false);
        await waitFor(() => {
            expect(recording.permitted.at(-1)).toBe(true);
        });
    });

    // What began is the session rather than the recording, so moving the switch off and on again reports nothing a
    // second time. The guard is a ref rather than a derived value because the record is an event and not a state.
    it('reports a session beginning once across a switch moved twice', async () => {
        const recording = telemetryRecording();

        renderApp(
            servedFrom,
            heldSession,
            deploymentAnswering(undefined, accepted, preferencesAnswering(true)),
            storeKeeping(),
            recording.telemetry,
        );
        await framed();
        openSettings();

        const withhold = screen.getByRole('switch', { name: /Do not send telemetry/ });

        fireEvent.click(withhold);
        await waitFor(() => {
            expect(recording.permitted.at(-1)).toBe(false);
        });

        fireEvent.click(withhold);
        await waitFor(() => {
            expect(recording.permitted.at(-1)).toBe(true);
        });

        expect(recording.events.filter((event) => event === 'session_started')).toHaveLength(1);
    });

    // What began is a session rather than a person, so the same person at the same deployment begins a second one by
    // signing in again. Nothing unmounts this frame on the way out — it renders the sign-in screen — so the guard has
    // to be cleared there or the commonest path of all records nothing: the deployment refuses the kept session and
    // somebody signs straight back in.
    it('reports a session beginning again when the same person signs back in', async () => {
        const recording = telemetryRecording();

        renderApp(servedFrom, heldSession, deploymentAnswering(), storeKeeping(), recording.telemetry);
        await framed();
        await signOut();

        signIn(heldPerson);
        await framed();

        await waitFor(() => {
            expect(recording.events.filter((event) => event === 'session_started')).toHaveLength(2);
        });
    });
});
