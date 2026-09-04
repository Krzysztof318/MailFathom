// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useEffect, useRef, useState } from 'react';
import {
    readClientPreferences,
    unsetClientPreferences,
    writeClientPreferences,
    type ClientPreferences,
    type ClientSession,
    type MailFathomTransport,
} from '@mailfathom/client-backend';
import type { ThemeChoice } from '../theme/themeChoice';
import { useTheme } from '../theme/useTheme';
import { rememberedTelemetry, rememberTelemetry } from './rememberedTelemetry';

// The settings that follow the person rather than the machine, read from the deployment once there is a session to
// read it with and written back whole whenever one of them changes.
//
// The theme is the half that exists on both sides, and the order between them is deliberate. The device answers first,
// because `main.tsx` paints a theme above the sign-in screen where there is no session to ask; the deployment's answer
// then replaces it once one exists, and that replacement happens once per read rather than on every render — a screen
// that reconciled the two continuously would fight whichever of them a person changed last.
//
// Language is not here. It is chosen above the sign-in screen as well as behind it, and the rule in
// `frontend/src/AGENTS.md` § *State* is what keeps it where the earlier of those two can reach it.

/** What the client is set to, and how a person changes one of the settings that follow them. */
export interface ClientPreferencesInForce {
    /** Whether opening a message opens a tab rather than replacing what is on the screen. */
    readonly openMailInTabs: boolean;

    /**
     * Whether opening a message marks it read on the person's own mail server, which ADR 0026 defaults to on.
     *
     * Read here rather than chosen here: the control that moves it belongs to the settings screen, and what the frame
     * needs is the value in force. Somebody with nothing read yet gets the unset answer, which is the same one the
     * deployment will confirm — so a message opened in the first moments of a session marks read rather than waiting
     * for a preference nobody has changed.
     */
    readonly markReadOnOpen: boolean;

    /**
     * Whether this deployment may be told what the person's client is doing.
     *
     * Answered before the deployment has said anything about this person, from what this device was last told about
     * them, so that a client which had been turned off does not record and export for the second the first read takes.
     * Everything else here is the deployment's answer alone.
     */
    readonly telemetryEnabled: boolean;

    /**
     * Whether opening a conversation draws every message in it rather than the one it was opened at.
     *
     * Unset reads as off, which is the conversation the client has always drawn: the message somebody came for, with
     * the history behind it one control away.
     */
    readonly expandWholeThread: boolean;

    /**
     * Whether an open message draws the sender's own markup inline rather than the reduced text.
     *
     * Unset reads as off, which is what this client has always drawn: the closed document tree ADR 0024 takes, with the
     * sender's own markup on the second surface one control away.
     */
    readonly embeddedHtmlMessages: boolean;

    /** Whether the deployment refused the last change, which is the one thing about this a screen has to say out loud. */
    readonly notStated: boolean;

    readonly chooseTheme: (choice: ThemeChoice) => void;
    readonly chooseTabMode: (openMailInTabs: boolean) => void;
    readonly chooseTelemetry: (telemetryEnabled: boolean) => void;
    readonly chooseThreadExpansion: (expandWholeThread: boolean) => void;
    readonly chooseMessageView: (embeddedHtmlMessages: boolean) => void;
}

// What is held, and whose it is. The session is carried beside the document rather than trusted to have stayed the
// same, because this hook outlives a sign-out: it is mounted by the frame, and the frame is what a person signs out of.
interface HeldPreferences {
    readonly session: ClientSession | null;
    readonly preferences: ClientPreferences;
    readonly notStated: boolean;
}

const heldForNobody: HeldPreferences = {
    session: null,
    preferences: unsetClientPreferences,
    notStated: false,
};

/**
 * Holds what the signed-in person set about their own client.
 *
 * @param session Who is asking and where, or `null` where there is nothing to ask with — nobody signed in, no network,
 * or a credential this deployment does not let read — in which case nothing is read and both settings are the
 * device's alone.
 * @param transport How a request reaches the deployment.
 * @param person Who is signed in, or `null` where nobody is. It is asked for beside the session rather than read out
 * of it because the two are absent at different moments: a session is withheld from this hook while the client is
 * offline or the grant does not let it read, and somebody is still signed in through all of that. It decides one thing
 * only — whose remembered telemetry answer the device is asked for — and getting it wrong is what would hand the next
 * person the last person's answer.
 * @returns The settings in force, and the five ways of changing one.
 */
export function useClientPreferences(
    session: ClientSession | null,
    transport: MailFathomTransport,
    person: string | null,
): ClientPreferencesInForce {
    const { setThemeChoice } = useTheme();
    const [held, setHeld] = useState<HeldPreferences>(heldForNobody);

    // The latest document, reachable from a handler whichever render built it. A handler closes over the render it
    // belongs to, and the answer that replaces this document arrives between renders — so a write composed out of a
    // render's own copy can state a document the deployment has already moved past, turning somebody's telemetry
    // decision back on because a switch beside it moved. Nothing renders this; it is what the two writes below read.
    const latest = useRef(held);

    // The read in flight, so that a choice made while it is still out can abandon it. The two are answers to the same
    // question and the newer one wins: a person who moved a switch before the deployment answered has said what they
    // want more recently than the document being fetched, and applying that answer on arrival would put the setting
    // back and then have to be undone by a second write nobody asked for.
    const reading = useRef<AbortController | null>(null);

    // Everything below reads through this rather than out of the state directly, which is what keeps one person's
    // answers off the next person's screen without a reset anywhere. Signing out and back in on one tab keeps this
    // hook mounted, so what was read for the credential before is still in state when the new one arrives — and it
    // would otherwise be drawn until their own answer landed and, worse, merged into the first write they made, which
    // states the whole document. Derived rather than cleared, because a value that no longer belongs to anything is
    // not state to correct: it is state to stop reading.
    const inForce = held.session === session ? held : heldForNobody;

    useEffect(() => {
        if (session === null) {
            return;
        }

        // Abandoning travels on a controller rather than on a flag, for the reason `shell/useConnection.ts` gives: it
        // is asked through a function, so nothing decides at the first check what can only become true at the second.
        const attempted = new AbortController();
        const abandoned = (): boolean => attempted.signal.aborted;

        reading.current = attempted;

        void (async () => {
            const answer = await readClientPreferences(session, transport);

            if (abandoned() || answer.outcome !== 'read') {
                return;
            }

            const read = { session, preferences: answer.value, notStated: false };

            latest.current = read;
            rememberTelemetry(person, answer.value.telemetryEnabled);
            setHeld(read);

            // The deployment's answer replaces the device's, which is the whole of why these two settings are held
            // there: somebody who chose a theme on their laptop has chosen it on this machine too.
            setThemeChoice(answer.value.theme);
        })();

        return () => {
            attempted.abort();
        };
    }, [session, transport, person, setThemeChoice]);

    // What is on the screen changes because a person pressed something, so it changes in the handler rather than in an
    // effect watching what the handler set. The write states the whole document — the route takes nothing less — which
    // is why what was last read is kept: a preference this screen does not offer is sent back unchanged rather than
    // reset to its unset answer by a client that had no opinion about it.
    function state(preferences: ClientPreferences): void {
        reading.current?.abort();

        const chosen = { session, preferences, notStated: false };

        latest.current = chosen;
        rememberTelemetry(person, preferences.telemetryEnabled);
        setHeld(chosen);

        if (session === null) {
            return;
        }

        void writeClientPreferences(session, transport, preferences).then((answer) => {
            setHeld((current) =>
                current.session === session ? { ...current, notStated: answer.outcome !== 'read' } : current,
            );
        });
    }

    // What the next write is composed out of: the document last read or last chosen, and the unset one where that
    // belongs to a session this is no longer.
    //
    // One value is not taken from the unset document there, and it is the same one the return below derives rather
    // than holds. A write states the whole document, and the account menu offers the theme and the tab mode from the
    // moment it is drawn — so somebody who had declined telemetry and changes either of those before the read comes
    // back would send `telemetryEnabled: true` under their own credential, abandon the read that would have said
    // otherwise, and have the device's copy rewritten to match. Nobody stated that value; it is the shape of the
    // route's answer standing in for an answer, and the device is holding the one they actually gave.
    function composedFrom(): ClientPreferences {
        if (latest.current.session === session) {
            return latest.current.preferences;
        }

        return { ...unsetClientPreferences, telemetryEnabled: rememberedTelemetry(person) };
    }

    return {
        openMailInTabs: inForce.preferences.openMailInTabs,
        markReadOnOpen: inForce.preferences.markReadOnOpen,
        // The one setting answered from the device while the deployment has said nothing about this person, for the
        // reason `rememberedTelemetry.ts` gives. Every other one takes its unset answer, which a screen can draw.
        //
        // Derived here rather than held in state, and asked for under the person rather than for the machine. Both
        // halves are the same rule `inForce` above is: a value that no longer belongs to whoever is signed in is not
        // state to correct but state to stop reading, and holding this one would mean carrying the last person's
        // answer across a sign-out on the same tab — which is the one direction a privacy answer may not be wrong in.
        // It costs a storage read on the renders before an answer has arrived and none afterwards, the branch not
        // being taken once there is one.
        telemetryEnabled: inForce.session === null ? rememberedTelemetry(person) : inForce.preferences.telemetryEnabled,
        expandWholeThread: inForce.preferences.expandWholeThread,
        embeddedHtmlMessages: inForce.preferences.embeddedHtmlMessages,
        notStated: inForce.notStated,
        chooseTheme: (choice) => {
            setThemeChoice(choice);
            state({ ...composedFrom(), theme: choice });
        },
        chooseTabMode: (openMailInTabs) => {
            state({ ...composedFrom(), openMailInTabs });
        },
        chooseTelemetry: (telemetryEnabled) => {
            state({ ...composedFrom(), telemetryEnabled });
        },
        chooseThreadExpansion: (expandWholeThread) => {
            state({ ...composedFrom(), expandWholeThread });
        },
        chooseMessageView: (embeddedHtmlMessages) => {
            state({ ...composedFrom(), embeddedHtmlMessages });
        },
    };
}
