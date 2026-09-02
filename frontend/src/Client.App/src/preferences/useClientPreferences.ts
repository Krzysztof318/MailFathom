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

// The two settings that follow the person rather than the machine, read from the deployment once there is a session to
// read it with and written back whole whenever one of them changes.
//
// The theme is the half that exists on both sides, and the order between them is deliberate. The device answers first,
// because `main.tsx` paints a theme above the sign-in screen where there is no session to ask; the deployment's answer
// then replaces it once one exists, and that replacement happens once per read rather than on every render — a screen
// that reconciled the two continuously would fight whichever of them a person changed last.
//
// Language is not here. It stays on the device, because what a person reads a client in is a fact about the machine
// they are at rather than about them.

/** What the client is set to, and how a person changes one of the two settings that follow them. */
export interface ClientPreferencesInForce {
    /** Whether opening a message opens a tab rather than replacing what is on the screen. */
    readonly openMailInTabs: boolean;

    /** Whether the deployment refused the last change, which is the one thing about this a screen has to say out loud. */
    readonly notStated: boolean;

    readonly chooseTheme: (choice: ThemeChoice) => void;
    readonly chooseTabMode: (openMailInTabs: boolean) => void;
}

/**
 * Holds what the signed-in person set about their own client.
 *
 * @param session Who is asking and where, or `null` where there is nothing to ask with — nobody signed in, no network,
 * or a credential this deployment does not let read — in which case nothing is read and both settings are the
 * device's alone.
 * @param transport How a request reaches the deployment.
 * @returns The settings in force, and the two ways of changing one.
 */
export function useClientPreferences(
    session: ClientSession | null,
    transport: MailFathomTransport,
): ClientPreferencesInForce {
    const { setThemeChoice } = useTheme();
    const [stated, setStated] = useState<ClientPreferences>(unsetClientPreferences);
    const [notStated, setNotStated] = useState(false);

    // The latest document, reachable from a handler whichever render built it. A handler closes over the render it
    // belongs to, and the answer that replaces this document arrives between renders — so a write composed out of a
    // render's own copy can state a document the deployment has already moved past, turning somebody's telemetry
    // decision back on because a switch beside it moved. Nothing renders this; it is what the two writes below read.
    const held = useRef(stated);

    useEffect(() => {
        if (session === null) {
            return;
        }

        // Abandoning travels on a controller rather than on a flag, for the reason `shell/useConnection.ts` gives: it
        // is asked through a function, so nothing decides at the first check what can only become true at the second.
        const attempted = new AbortController();
        const abandoned = (): boolean => attempted.signal.aborted;

        void (async () => {
            const answer = await readClientPreferences(session, transport);

            if (abandoned() || answer.outcome !== 'read') {
                return;
            }

            held.current = answer.value;
            setStated(answer.value);

            // The deployment's answer replaces the device's, which is the whole of why these two settings are held
            // there: somebody who chose a theme on their laptop has chosen it on this machine too.
            setThemeChoice(answer.value.theme);
        })();

        return () => {
            attempted.abort();
        };
    }, [session, transport, setThemeChoice]);

    // What is on the screen changes because a person pressed something, so it changes in the handler rather than in an
    // effect watching what the handler set. The write states the whole document — the route takes nothing less — which
    // is why what was last read is kept: a preference this screen does not offer is sent back unchanged rather than
    // reset to its unset answer by a client that had no opinion about it.
    function state(preferences: ClientPreferences): void {
        held.current = preferences;
        setStated(preferences);

        if (session === null) {
            return;
        }

        void writeClientPreferences(session, transport, preferences).then((answer) => {
            setNotStated(answer.outcome !== 'read');
        });
    }

    return {
        openMailInTabs: stated.openMailInTabs,
        notStated,
        chooseTheme: (choice) => {
            setThemeChoice(choice);
            state({ ...held.current, theme: choice });
        },
        chooseTabMode: (openMailInTabs) => {
            state({ ...held.current, openMailInTabs });
        },
    };
}
