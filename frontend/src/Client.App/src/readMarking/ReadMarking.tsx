// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useRef, useState, type ReactNode } from 'react';
import {
    markMailRead,
    mostMessagesPerMutation,
    type ClientSession,
    type MailFathomTransport,
} from '@mailfathom/client-backend';
import {
    ReadMarkingContext,
    nothingMarkedRead,
    type MarkedIn,
    type MessageOpened,
    type ReadMarking,
} from './useReadMarking';

// Marking a message read because somebody opened it, which ADR 0026 settles as an ordinary flag mutation their act
// authors rather than an effect of the read that served it. Nothing here reaches a mail server: the submission writes a
// durable record and answers, and the account's own pass is what tells the server — so an unreachable account leaves
// the reading pending instead of failing the open.
//
// Three things decide whether it happens at all, and all three are the frame's rather than a screen's: the reader's own
// setting, the grant the credential signed in under, and there being a session to submit over. Where any is missing
// every component below reads `nothingMarkedRead`, so no screen asks the question twice.
//
// It marks nothing twice and marks nothing already read. What has been submitted is held in a ref rather than in the
// state below it, because two bodies drawn in one commit both read it before either render happens — and because React
// invokes an effect twice on mount under `StrictMode`, which is exactly the shape a state-only guard lets through.

/** What is held, and whose it is, so one person's markings never outlive the credential they were made under. */
interface HeldMarkings {
    readonly session: ClientSession | null;
    readonly marked: Map<string, MarkedIn>;
}

/** What a client belonging to nobody has marked, which is what a session change falls back to until the next marking. */
const emptyMarkings: ReadonlyMap<string, MarkedIn> = new Map();

export function ReadMarkingProvider({
    session,
    transport,
    marking,
    children,
}: {
    /** Who is asking and where, or `null` where there is nobody to submit for. */
    readonly session: ClientSession | null;
    readonly transport: MailFathomTransport;

    /** Whether this reader asked for opening a message to mark it read, and holds the grant that writes a flag. */
    readonly marking: boolean;

    readonly children: ReactNode;
}) {
    const [drawn, setDrawn] = useState<HeldMarkings>({ session: null, marked: new Map() });
    const submitted = useRef<HeldMarkings>({ session: null, marked: new Map() });

    // The markings waiting for the batch they will travel in. Nothing waits for a body that has not been drawn — the
    // flush runs on the microtask after the one that filled it — so what a batch coalesces is the bodies that were
    // ready together rather than bodies still being read.
    const waiting = useRef<string[]>([]);
    const flushing = useRef(false);

    // Derived rather than cleared, for the reason `preferences/useClientPreferences.ts` gives: signing out and back in
    // on one tab keeps this component mounted, and the previous person's markings would otherwise be drawn over the
    // next person's mail. The session the markings were made under travels beside them for that comparison; the ref
    // below holds the same pair and is emptied in the handler, where a session change is somebody's act.
    const inForce = drawn.session === session ? drawn.marked : emptyMarkings;

    function keep(): void {
        setDrawn({ session: submitted.current.session, marked: new Map(submitted.current.marked) });
    }

    function forget(storedEmailIds: readonly string[]): void {
        for (const storedEmailId of storedEmailIds) {
            submitted.current.marked.delete(storedEmailId);
        }

        keep();
    }

    function flush(): void {
        flushing.current = false;

        const batch = waiting.current;

        waiting.current = [];

        if (session === null) {
            return;
        }

        // Split rather than truncated: the route refuses a longer batch whole, and a marking silently dropped here
        // would be a row drawn read against a mailbox nobody told.
        for (let from = 0; from < batch.length; from += mostMessagesPerMutation) {
            submit(session, batch.slice(from, from + mostMessagesPerMutation));
        }
    }

    function submit(asking: ClientSession, batch: readonly string[]): void {
        void markMailRead(asking, transport, batch).then((answer) => {
            // A marking the deployment did not write down is one the row must stop claiming. Both cases arrive here:
            // a submission that never reached it, and a message it answered about with anything but `recorded` — mail
            // that has moved on since the list drew it, or an account it no longer serves.
            const recorded =
                answer.outcome === 'read'
                    ? new Set(
                          answer.value
                              .filter((result) => result.outcome === 'recorded')
                              .map((result) => result.storedEmailId),
                      )
                    : new Set<string>();

            const refused = batch.filter((storedEmailId) => !recorded.has(storedEmailId));

            if (refused.length > 0 && submitted.current.session === asking) {
                forget(refused);
            }
        });
    }

    function markRead(message: MessageOpened): void {
        if (session === null || !message.unread) {
            return;
        }

        if (submitted.current.session !== session) {
            submitted.current = { session, marked: new Map() };
            waiting.current = [];
        }

        if (submitted.current.marked.has(message.storedEmailId)) {
            return;
        }

        submitted.current.marked.set(message.storedEmailId, {
            account: message.account,
            folder: message.folder,
        });
        keep();

        waiting.current.push(message.storedEmailId);

        if (!flushing.current) {
            flushing.current = true;
            queueMicrotask(flush);
        }
    }

    const value: ReadMarking = marking ? { marked: inForce, markRead } : nothingMarkedRead;

    return <ReadMarkingContext value={value}>{children}</ReadMarkingContext>;
}
