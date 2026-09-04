// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { act, fireEvent, renderHook, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type {
    ClientRequest,
    ClientSession,
    MailFathomTransport,
    MailMutationRecordState,
    MailMutationResult,
} from '@mailfathom/client-backend';
import { LocalizationProvider } from '../localization/Localization';
import { ToastsProvider } from '../toasts/Toasts';
import { toastLeaving, toastLifetime } from '../toasts/useToasts';
import { PendingChangesProvider } from './PendingChanges';
import { PendingChangeLines } from './PendingChangeLines';
import {
    followedChangeInterval,
    mostFollowingAttempts,
    usePendingChanges,
    type PendingChanges,
} from './usePendingChanges';
import type { ChangeSubmission } from './changeStandings';
import type { ReactNode } from 'react';

// The queue is driven the way a screen drives it — one submission handed in, and the record read answered on the wire
// — because what is being proven is what somebody is told: which endings are silent, which are said out loud, and
// which are left as a question with both ways out. The clock is fake throughout, so the wait between reads is
// asserted rather than sat through.

const session: ClientSession = {
    baseAddress: 'https://mail.example.invalid',
    authorization: 'Basic dGVzdA==',
};

const somebodyElse: ClientSession = { ...session, authorization: 'Basic b3RoZXI=' };

const storedEmailId = '2f7d4f2a-6c1e-4e0a-9a2f-1b0c9d8e7f60';
const recordId = '0198f4a1-2b6c-7a1d-9f3e-4c5d6e7f8a91';

const asked: string[] = [];
const letGo: string[] = [];

function submission(results: readonly MailMutationResult[] | null, named: readonly string[] = [storedEmailId]) {
    return {
        act: 'markRead',
        asked: named,
        results,
        askAgain: (storedEmailIds) => asked.push(...storedEmailIds),
        letGo: (storedEmailIds) => letGo.push(...storedEmailIds),
    } satisfies ChangeSubmission;
}

function recorded(...recordIds: readonly string[]): readonly MailMutationResult[] {
    return [
        {
            storedEmailId,
            outcome: 'recorded',
            changes: recordIds.map((id) => ({ recordId: id, state: 'pending' as const })),
        },
    ];
}

/** A deployment that answers every record read with the state named, and remembers what it was asked for. */
function standingAt(
    state: MailMutationRecordState,
    outcomeUnknown = false,
): {
    transport: MailFathomTransport;
    requests: ClientRequest[];
} {
    const requests: ClientRequest[] = [];

    return {
        requests,
        transport: (request) => {
            requests.push(request);

            const named = [...new URL(request.path).searchParams.getAll('record')];

            return Promise.resolve({
                status: 200,
                headers: {},
                body: JSON.stringify({
                    changes: named.map((id) => ({ recordId: id, storedEmailId, state, outcomeUnknown })),
                }),
            });
        },
    };
}

function unreachable(): { transport: MailFathomTransport; requests: ClientRequest[] } {
    const requests: ClientRequest[] = [];

    return {
        requests,
        transport: (request) => {
            requests.push(request);

            return Promise.reject(new Error('the connection was refused'));
        },
    };
}

// The surface is read out of the tree rather than passed in, because what a producer holds is what the context gives
// it. It is rebuilt on every render, so what is kept is the live result rather than one render's value.
let queue: { current: PendingChanges } | null = null;

// Who the tree is signed in as. It is read while the wrapper renders and written only between renders, which is what
// lets one mounted tree be handed a second credential the way signing out and back in on one tab hands it one.
let signedInAs: ClientSession | null = session;

function following(
    transport: MailFathomTransport,
    signedIn: ClientSession | null = session,
): { signInAsSomebodyElse: () => void } {
    signedInAs = signedIn;

    const view = renderHook(() => usePendingChanges(), {
        wrapper: ({ children }: { readonly children: ReactNode }) => (
            <LocalizationProvider>
                <ToastsProvider>
                    <PendingChangesProvider session={signedInAs} transport={transport}>
                        <PendingChangeLines />
                        {children}
                    </PendingChangesProvider>
                </ToastsProvider>
            </LocalizationProvider>
        ),
    });

    queue = view.result;

    return {
        signInAsSomebodyElse: () => {
            signedInAs = somebodyElse;
            view.rerender();
        },
    };
}

function hand(submitted: ChangeSubmission): void {
    act(() => {
        queue?.current.follow(submitted);
    });
}

async function pass(milliseconds: number): Promise<void> {
    await act(async () => {
        await vi.advanceTimersByTimeAsync(milliseconds);
    });
}

/** Long enough for the toast that announced something to have said it and gone, leaving the panel behind. */
async function afterTheToast(): Promise<void> {
    await pass(toastLifetime + toastLeaving);
}

/** Every attempt the client makes at a deployment that answers none of them, leaving it having given up. */
async function giveUp(): Promise<void> {
    for (let attempt = 0; attempt < mostFollowingAttempts; attempt += 1) {
        await pass(followedChangeInterval);
    }
}

beforeEach(() => {
    vi.useFakeTimers();
    asked.length = 0;
    letGo.length = 0;
});

afterEach(() => {
    queue = null;
    vi.useRealTimers();
});

describe('PendingChangesProvider', () => {
    it('says how many changes the mailbox has not taken yet', () => {
        following(standingAt('pending').transport);

        hand(submission(recorded(recordId)));

        expect(screen.getByText('One change has not reached your mailbox yet.')).toBeTruthy();
    });

    it('asks the deployment where each followed change stands, oldest first', async () => {
        const { transport, requests } = standingAt('converging');

        following(transport);
        hand(submission(recorded(recordId, 'second')));
        await pass(followedChangeInterval);

        expect([...new URL(requests[0]?.path ?? '').searchParams.getAll('record')]).toStrictEqual([recordId, 'second']);
    });

    // Somebody opening one message after another produces a change each time, and each one lands while the wait for
    // the last read is still running. The wait belongs to the round rather than to the queue, so it ends when it was
    // always going to end — a client that started the five seconds afresh on every change would never read at all.
    it('asks on time however often somebody adds another change while it is waiting', async () => {
        const { transport, requests } = standingAt('pending');

        following(transport);
        hand(submission(recorded(recordId)));
        await pass(followedChangeInterval - 1_000);

        hand(submission(recorded('second'), ['another']));
        await pass(1_000);

        expect(requests).toHaveLength(1);
    });

    // Marking another message read says nothing about whether the deployment has started answering, so it may not
    // buy the client another attempt: a person reading their mail would otherwise hold it one short of giving up for
    // as long as they kept reading, never stopping and never saying it had.
    it('does not let a new change clear the failures the deployment has earned', async () => {
        const { transport, requests } = unreachable();

        following(transport);
        hand(submission(recorded(recordId)));

        for (let attempt = 0; attempt < mostFollowingAttempts - 1; attempt += 1) {
            await pass(followedChangeInterval);
        }

        hand(submission(recorded('second'), ['another']));
        await pass(followedChangeInterval);
        await pass(followedChangeInterval * 3);

        expect(requests).toHaveLength(mostFollowingAttempts);
    });

    it('stops saying a change is waiting once the mailbox has taken it, without saying anything about it', async () => {
        following(standingAt('completed').transport);

        hand(submission(recorded(recordId)));
        await pass(followedChangeInterval);

        expect(screen.queryByText('One change has not reached your mailbox yet.')).toBeNull();
        expect(screen.queryByText('A change needs your decision.')).toBeNull();
    });

    // Somebody withdrew it, here or from another client of the same mailbox. Nothing is pending and nobody is owed a
    // question, so it leaves the queue exactly as a change the mailbox took does.
    it('drops a change somebody took back without putting it in front of anybody', async () => {
        following(standingAt('cancelled').transport);

        hand(submission(recorded(recordId)));
        await pass(followedChangeInterval);

        expect(screen.queryByText('One change has not reached your mailbox yet.')).toBeNull();
        expect(screen.queryByText('A change needs your decision.')).toBeNull();
    });

    it('says nothing about a message whose mailbox already agreed with the change', () => {
        following(standingAt('pending').transport);

        hand(submission([{ storedEmailId, outcome: 'already-in-destination', changes: [] }]));

        expect(screen.queryByText('One message was not marked read.')).toBeNull();
        expect(letGo).toStrictEqual([]);
    });

    it('says a message the deployment could not change was not changed, and stops the screen claiming it', () => {
        following(standingAt('pending').transport);

        hand(submission([{ storedEmailId, outcome: 'message-not-found', changes: [] }]));

        expect(screen.getByText('One message was not marked read.')).toBeTruthy();
        expect(screen.getByText('That mail is no longer in the mailbox this deployment reads.')).toBeTruthy();
        expect(letGo).toStrictEqual([storedEmailId]);
    });

    it('counts the messages one reason happened to rather than saying it once each', () => {
        following(standingAt('pending').transport);

        hand(
            submission([
                { storedEmailId: 'gone', outcome: 'message-not-found', changes: [] },
                { storedEmailId: 'also-gone', outcome: 'message-not-found', changes: [] },
            ]),
        );

        expect(screen.getByText('2 messages were not marked read.')).toBeTruthy();
    });

    it('says a change that never reached the deployment changed nothing, and offers it again', () => {
        following(standingAt('pending').transport);

        hand(submission(null));

        expect(screen.getByText('This change did not reach your deployment.')).toBeTruthy();
        expect(letGo).toStrictEqual([storedEmailId]);

        fireEvent.click(screen.getByRole('button', { name: 'Try again' }));

        expect(asked).toStrictEqual([storedEmailId]);
    });

    it('puts a change the account stopped retrying in front of the person, saying both sides', async () => {
        following(standingAt('dead-lettered').transport);

        hand(submission(recorded(recordId)));
        await pass(followedChangeInterval);

        expect(screen.getByText('A change needs your decision.')).toBeTruthy();

        await afterTheToast();

        expect(screen.getByText('Marked read')).toBeTruthy();
        expect(
            screen.getByText('Your mailbox would not take this change, and MailFathom has stopped trying to make it.'),
        ).toBeTruthy();
    });

    it('puts a change whose command was never answered in front of the person, whatever its state says', async () => {
        following(standingAt('completed', true).transport);

        hand(submission(recorded(recordId)));
        await pass(followedChangeInterval);
        await afterTheToast();

        expect(
            screen.getByText(
                'The change went out and your mailbox never answered, so it may or may not have been made.',
            ),
        ).toBeTruthy();
    });

    it('asks for the change afresh when the person says to ask again, and stops asking them', async () => {
        following(standingAt('dead-lettered').transport);

        hand(submission(recorded(recordId)));
        await pass(followedChangeInterval);
        await afterTheToast();

        fireEvent.click(screen.getByRole('button', { name: 'Ask again' }));

        expect(asked).toStrictEqual([storedEmailId]);
        expect(screen.queryByText('Marked read')).toBeNull();
    });

    it('stops the screen claiming a change the person let go of', async () => {
        following(standingAt('dead-lettered').transport);

        hand(submission(recorded(recordId)));
        await pass(followedChangeInterval);
        await afterTheToast();

        fireEvent.click(screen.getByRole('button', { name: 'Let it go' }));

        expect(letGo).toStrictEqual([storedEmailId]);
        expect(screen.queryByText('Marked read')).toBeNull();
    });

    it('stops asking where changes stand once the deployment has not answered enough times running', async () => {
        const { transport, requests } = unreachable();

        following(transport);
        hand(submission(recorded(recordId)));
        await giveUp();

        await pass(followedChangeInterval * 3);

        expect(requests).toHaveLength(mostFollowingAttempts);
    });

    // A row still saying a change is on its way, under a client that quietly stopped looking, is the dishonest state
    // this whole surface exists against — so the sentence and the way back outlive the toast that first said them.
    it('goes on saying it stopped, and offers the way back, once the toast that said so has gone', async () => {
        const { transport, requests } = unreachable();

        following(transport);
        hand(submission(recorded(recordId)));
        await giveUp();
        await afterTheToast();

        expect(screen.getByText('This client stopped checking where your changes have got to.')).toBeTruthy();

        fireEvent.click(screen.getByRole('button', { name: 'Check again' }));
        await pass(followedChangeInterval);

        expect(requests).toHaveLength(mostFollowingAttempts + 1);
    });

    it('follows nothing of the last person’s under the next person’s credential', () => {
        const { signInAsSomebodyElse } = following(standingAt('pending').transport);

        hand(submission(recorded(recordId)));
        act(signInAsSomebodyElse);

        expect(screen.queryByText('One change has not reached your mailbox yet.')).toBeNull();
    });

    it('follows nothing at all where nobody is signed in', () => {
        following(standingAt('pending').transport, null);

        expect(screen.queryByText('One change has not reached your mailbox yet.')).toBeNull();
    });
});
