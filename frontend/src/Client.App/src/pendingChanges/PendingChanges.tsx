// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useEffect, useEffectEvent, useState, type ReactNode } from 'react';
import {
    mostRecordsPerRead,
    readMailMutationRecords,
    type ClientSession,
    type MailFathomTransport,
    type MailMutationOutcome,
} from '@mailfathom/client-backend';
import { useLocalization } from '../localization/useLocalization';
import { useToasts } from '../toasts/useToasts';
import { refusalReasons, refusalTitles, standingReasons } from './changeWording';
import { readSubmission, standingOf, type ChangeSubmission, type PendingChange } from './changeStandings';
import {
    followedChangeInterval,
    mostFollowingAttempts,
    PendingChangesContext,
    type ChangeResolution,
    type PendingChanges,
    type UndecidedChange,
} from './usePendingChanges';

// What this client has asked its mailbox for since it was opened, followed until each change lands or turns into a
// question. It holds nothing durable, which is
// [ADR 0028](../../../../docs/decisions/0028-no-mail-on-the-device-and-an-honest-client-with-no-route-to-its-deployment.md)'s
// decision rather than this module's: a change reaches the deployment or it does not happen, so what is followed here
// is always a record the deployment already wrote down, and this goes when the tab does.
//
// Following one is worth doing because writing it down is not the same as making it. The deployment answers a
// submission the moment the record exists, and the account's own reconciliation pass is what tells the mail server
// minutes later — so between those two moments a person has changed something and nobody has told them whether it took.
// `changeStandings.ts` holds the rule deciding which of those endings anybody hears about.
//
// The rule's other half is at submission time and is stated here because it is about an answer rather than a record: a
// message the deployment refused wrote nothing down, so it never enters the queue, and what it is owed is one sentence
// per reason rather than a card each. `already-in-destination` is the one refusal nobody hears, because the mailbox
// already says what was asked for — two clients asking for the same thing is not a collision, and the screen was right.

/** What is held, and whose it is, so one person's changes never outlive the credential they were asked for under. */
interface Held {
    readonly session: ClientSession | null;
    readonly followed: readonly FollowedChange[];
    readonly undecided: readonly HeldUndecided[];
}

/** A change being followed, beside what its producer said asking again and letting go mean for it. */
interface FollowedChange extends PendingChange {
    readonly askAgain: () => void;
    readonly letGo: () => void;
}

interface HeldUndecided extends UndecidedChange {
    readonly askAgain: () => void;
    readonly letGo: () => void;
}

const nothingHeld: Held = { session: null, followed: [], undecided: [] };

export function PendingChangesProvider({
    session,
    transport,
    children,
}: {
    /** Who is asking and where, or `null` where there is nobody to follow a change for. */
    readonly session: ClientSession | null;
    readonly transport: MailFathomTransport;

    readonly children: ReactNode;
}) {
    const { locale, translate } = useLocalization();
    const toasts = useToasts();
    const [held, setHeld] = useState<Held>(nothingHeld);
    const [attempts, setAttempts] = useState(0);
    const [round, setRound] = useState(0);

    // Derived rather than cleared, for the reason `readMarking/ReadMarking.tsx` gives: signing out and back in on one
    // tab keeps this component mounted, and the previous person's changes would otherwise be followed under the next
    // person's credential.
    const inForce = held.session === session ? held : nothingHeld;
    const following = inForce.followed;
    const stoppedFollowing = attempts >= mostFollowingAttempts;

    // What re-arms the wait is a round of its own rather than the queue it read, and that is the whole reason `round`
    // exists. Adding a change gives `followed` a new array, so an effect depending on the queue would cancel its own
    // pending wait every time somebody opened another message — and a person reading faster than the interval would
    // defer the read indefinitely, which is exactly the bounded following this module promises not to do.
    const follows = following.length > 0;

    // Reads the queue, the credential, and the failure count as they are when the wait ends rather than as they were
    // when it was armed, which is what lets the wait be armed once and left alone. Adding a change while one is in
    // flight changes what the next read asks about; it never changes when that read happens.
    const readWhereChangesStand = useEffectEvent((stillWanted: () => boolean) => {
        if (session === null) {
            return;
        }

        const asking = session;

        // The oldest first and a page at a time, which is what replaying the queue in order means here: the route
        // names each record in the request line and refuses a longer read whole.
        const page = following.slice(0, mostRecordsPerRead);

        void (async () => {
            const answer = await readMailMutationRecords(
                asking,
                transport,
                page.map((change) => change.recordId),
            );

            if (!stillWanted()) {
                return;
            }

            setRound((current) => current + 1);

            if (answer.outcome === 'failed') {
                setAttempts(attempts + 1);

                // Said where the budget runs out rather than from a render reading that it has: the same sentence
                // raised by every later render would be the client telling somebody once a frame.
                if (attempts + 1 >= mostFollowingAttempts) {
                    toasts.raise({
                        kind: 'warning',
                        title: translate('pendingChange.stoppedFollowing'),
                        body: translate('pendingChange.stoppedFollowingBody', {
                            total: new Intl.NumberFormat(locale).format(mostFollowingAttempts),
                        }),
                        action: {
                            label: translate('pendingChange.followAgain'),
                            take: () => {
                                setAttempts(0);
                            },
                        },
                    });
                }

                return;
            }

            const standings = new Map(answer.value.map((record) => [record.recordId, standingOf(record)]));
            const stillWaiting: FollowedChange[] = [];
            const undecided: HeldUndecided[] = [];

            for (const change of page) {
                const standing = standings.get(change.recordId);

                // A record the deployment no longer answers for is absent from the answer rather than refused, which
                // is what a folder this credential may no longer read looks like from here. There is nothing left to
                // follow and nothing that failed, so it leaves the queue in silence.
                if (standing === undefined || standing === 'converged') {
                    continue;
                }

                if (standing === 'waiting') {
                    stillWaiting.push(change);
                } else {
                    undecided.push({ ...change, standing });
                }
            }

            setHeld((current) =>
                current.session === asking
                    ? {
                          session: asking,
                          followed: [...stillWaiting, ...current.followed.slice(page.length)],
                          undecided: [...current.undecided, ...undecided],
                      }
                    : current,
            );
            setAttempts(0);

            for (const change of undecided) {
                toasts.raise({
                    kind: change.standing === 'exhausted' ? 'error' : 'warning',
                    title: translate('pendingChange.undecided'),
                    body: translate(standingReasons[change.standing]),
                    action: { label: translate('pendingChange.askAgain'), take: change.askAgain },
                });
            }
        })();
    });

    useEffect(() => {
        if (session === null || !follows || attempts >= mostFollowingAttempts) {
            return;
        }

        let abandoned = false;
        const waiting = setTimeout(() => {
            readWhereChangesStand(() => !abandoned);
        }, followedChangeInterval);

        return () => {
            abandoned = true;
            clearTimeout(waiting);
        };
    }, [session, follows, attempts, round]);

    function report(submission: ChangeSubmission, outcome: MailMutationOutcome, storedEmailIds: readonly string[]) {
        const reason = refusalReasons[outcome];

        if (reason === null) {
            return;
        }

        // The screen stops claiming a change the deployment wrote nothing down for, before the sentence saying so: a
        // row that goes on showing what a mailbox refused is the silent loss this whole feature exists against.
        submission.letGo(storedEmailIds);

        toasts.raise({
            kind: 'warning',
            title: translate(
                refusalTitles[submission.act][new Intl.PluralRules(locale).select(storedEmailIds.length)],
                {
                    count: new Intl.NumberFormat(locale).format(storedEmailIds.length),
                },
            ),
            body: translate(reason),
        });
    }

    function follow(submission: ChangeSubmission): void {
        // Nothing reached the deployment, so nothing was written down and the mailbox is untouched. There is no queue
        // to put it in — a write is made or it does not happen — so what is owed is the sentence and the way back.
        if (submission.results === null) {
            submission.letGo(submission.asked);
            toasts.raise({
                kind: 'error',
                title: translate('pendingChange.notDelivered'),
                body: translate('pendingChange.notDeliveredBody'),
                action: {
                    label: translate('pendingChange.retry'),
                    take: () => {
                        submission.askAgain(submission.asked);
                    },
                },
            });

            return;
        }

        const reading = readSubmission(submission);

        for (const [outcome, storedEmailIds] of reading.refused) {
            report(submission, outcome, storedEmailIds);
        }

        if (reading.followed.length === 0) {
            return;
        }

        const arriving = reading.followed.map<FollowedChange>((change) => ({
            ...change,
            askAgain: () => {
                submission.askAgain([change.storedEmailId]);
            },
            letGo: () => {
                submission.letGo([change.storedEmailId]);
            },
        }));

        // The failure count is the poll's own and is cleared by the poll alone. A change arriving says nothing about
        // whether the deployment has started answering, and clearing it here would let somebody marking mail read
        // hold the client at four failures for ever — never stopping, and never saying it had.
        setHeld((current) => {
            const carried = current.session === session ? current : nothingHeld;

            return { session, followed: [...carried.followed, ...arriving], undecided: carried.undecided };
        });
    }

    function settle(recordId: string, resolution: ChangeResolution): void {
        const change = inForce.undecided.find((undecided) => undecided.recordId === recordId);

        if (change === undefined) {
            return;
        }

        setHeld((current) => ({
            ...current,
            undecided: current.undecided.filter((undecided) => undecided.recordId !== recordId),
        }));

        // Asking again is the same act submitted afresh rather than a record revived, so it travels the confirmation
        // and convergence path its first attempt did and arrives back here as a new change to follow.
        if (resolution === 'askAgain') {
            change.askAgain();
        } else {
            change.letGo();
        }
    }

    const value: PendingChanges = {
        waiting: inForce.followed,
        undecided: inForce.undecided,
        stoppedFollowing,
        follow,
        settle,
        followAgain: () => {
            setAttempts(0);
        },
    };

    return <PendingChangesContext value={value}>{children}</PendingChangesContext>;
}
