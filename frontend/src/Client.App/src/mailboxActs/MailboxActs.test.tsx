// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import type { ReactNode } from 'react';
import { act, fireEvent, renderHook, screen, waitFor, within } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import type {
    ClientRequest,
    ClientSession,
    MailFathomTransport,
    MailMutationOutcome,
    MailMutationRecordState,
} from '@mailfathom/client-backend';
import { LocalizationProvider } from '../localization/Localization';
import { PendingChangeLines } from '../pendingChanges/PendingChangeLines';
import { PendingChangesProvider } from '../pendingChanges/PendingChanges';
import { followedChangeInterval } from '../pendingChanges/usePendingChanges';
import { ToastsProvider } from '../toasts/Toasts';
import { toastLeaving, toastLifetime } from '../toasts/useToasts';
import { mostMessagesPerMutation } from '@mailfathom/client-backend';
import { MailboxActsProvider } from './MailboxActs';
import type { MoveDestination } from './mailboxDestinations';
import { useMailboxActs, type ActedMessage, type MailboxAct, type MailboxActs } from './useMailboxActs';

// The provider is driven the way a control drives it — through the hook — because what is being proven is what the
// deployment was asked for and what the person was told afterwards. Nothing here reaches a mail server: an act writes
// a record and answers, so every assertion is about the request that carried it and the toast that reported it.
//
// The pending-changes queue is mounted above it as the frame mounts it, because what became of an act beyond being
// written down — a refusal, a submission that never arrived, a change the account stopped retrying — is the queue's
// to say, and saying it once for every act is what is being proven alongside the act.

const session: ClientSession = { baseAddress: 'https://mail.example.invalid', authorization: 'Basic dGVzdA==' };

const invoice: ActedMessage = { storedEmailId: 'message-1', account: 'work', folder: 'work-inbox', unread: false };
const receipt: ActedMessage = { storedEmailId: 'message-2', account: 'work', folder: 'work-inbox', unread: false };
const discarded: ActedMessage = { storedEmailId: 'message-3', account: 'work', folder: 'work-trash', unread: false };

const folders = JSON.stringify({
    synchronizationEnabled: true,
    accounts: [
        {
            account: {
                id: 'work',
                displayName: 'Work',
                synchronizationState: 'Synchronized',
                lastSynchronizedAt: null,
                behind: false,
            },
            folders: [
                {
                    alias: 'work-inbox',
                    role: 'Inbox',
                    path: ['INBOX'],
                    storedEmailCount: 0,
                    unreadEmailCount: 0,
                    synchronizationState: 'Synchronized',
                    lastSynchronizedAt: null,
                    behind: false,
                },
                {
                    alias: 'work-archive',
                    role: 'Archive',
                    path: ['Archive'],
                    storedEmailCount: 0,
                    unreadEmailCount: 0,
                    synchronizationState: 'Synchronized',
                    lastSynchronizedAt: null,
                    behind: false,
                },
                {
                    alias: 'work-trash',
                    role: 'Trash',
                    path: ['Trash'],
                    storedEmailCount: 0,
                    unreadEmailCount: 0,
                    synchronizationState: 'Synchronized',
                    lastSynchronizedAt: null,
                    behind: false,
                },
            ],
        },
    ],
});

interface Deployment {
    readonly transport: MailFathomTransport;
    readonly requests: ClientRequest[];
}

/**
 * A deployment answering the folders it has, and every submitted batch with the outcomes it was told to answer.
 *
 * A recorded message is answered with one record named after it, so a withdrawal or a release can be read back as the
 * records it named. A withdrawal cancels every record except the ones `takenInHand` names, which the deployment has
 * already begun and refuses to cancel; a release leaves each record pending, the answer the route gives. A record read
 * back by the queue stands at `standing`.
 */
function deploymentAnswering(
    outcomes: Readonly<Record<string, MailMutationOutcome>> = {},
    status = 200,
    takenInHand: readonly string[] = [],
    standing: MailMutationRecordState = 'pending',
): Deployment {
    const requests: ClientRequest[] = [];

    return {
        requests,
        transport: (request) => {
            requests.push(request);

            if (request.path.endsWith('/folders')) {
                return Promise.resolve({ status: 200, body: folders, headers: {} });
            }

            if (request.path.includes('/mutations?')) {
                return Promise.resolve({
                    status: 200,
                    body: JSON.stringify({
                        changes: new URL(request.path).searchParams.getAll('record').map((recordId) => ({
                            recordId,
                            storedEmailId: recordId.replace('record-', ''),
                            state: standing,
                            outcomeUnknown: false,
                        })),
                    }),
                    headers: {},
                });
            }

            if (request.path.endsWith('/withdrawals') || request.path.endsWith('/releases')) {
                const { recordIds } = JSON.parse(request.body ?? '{}') as { recordIds: string[] };
                const withdrawing = request.path.endsWith('/withdrawals');

                return Promise.resolve({
                    status,
                    body: JSON.stringify({
                        changes: recordIds.map((recordId) => ({
                            recordId,
                            storedEmailId: recordId.replace('record-', ''),
                            state: withdrawing && !takenInHand.includes(recordId) ? 'cancelled' : 'pending',
                            outcomeUnknown: false,
                        })),
                    }),
                    headers: {},
                });
            }

            const asked = JSON.parse(request.body ?? '{}') as {
                changes?: { storedEmailId: string }[];
                moves?: { storedEmailId: string }[];
                deletes?: { storedEmailId: string }[];
            };

            return Promise.resolve({
                status,
                body: JSON.stringify({
                    results: [...(asked.changes ?? []), ...(asked.moves ?? []), ...(asked.deletes ?? [])].map(
                        ({ storedEmailId }) => {
                            const outcome = outcomes[storedEmailId] ?? 'recorded';

                            return {
                                storedEmailId,
                                outcome,
                                changes:
                                    outcome === 'recorded'
                                        ? [{ recordId: `record-${storedEmailId}`, state: 'pending' }]
                                        : [],
                            };
                        },
                    ),
                }),
                headers: {},
            });
        },
    };
}

/**
 * The same deployment with its answers to submissions held back from `hold` on until `answer`, which is how an answer
 * is made to arrive after somebody else has signed in.
 */
function answeringLate(deployment: Deployment): {
    readonly deployment: Deployment;
    readonly hold: () => void;
    readonly answer: () => void;
} {
    let holding = false;
    const waiting: (() => void)[] = [];

    return {
        deployment: {
            requests: deployment.requests,
            transport: (request) =>
                holding && request.path.includes('/mutations/')
                    ? new Promise((resolve) => {
                          waiting.push(() => {
                              resolve(deployment.transport(request));
                          });
                      })
                    : deployment.transport(request),
        },
        hold: () => {
            holding = true;
        },
        answer: () => {
            for (const release of waiting.splice(0)) {
                release();
            }
        },
    };
}

/** Somebody else, signed in on the same tab, which is a different credential handed to providers that stay mounted. */
const someoneElse: ClientSession = { baseAddress: 'https://mail.example.invalid', authorization: 'Basic b3RoZXI=' };

function acting(
    deployment: Deployment,
    { flags = true, moves = true, deletes = true }: { flags?: boolean; moves?: boolean; deletes?: boolean } = {},
): { readonly held: () => MailboxActs; readonly signIn: (next: ClientSession) => void } {
    let signedIn = session;

    function Surrounded({ children }: { readonly children: ReactNode }) {
        return (
            <LocalizationProvider>
                <ToastsProvider>
                    <PendingChangesProvider session={signedIn} transport={deployment.transport}>
                        <PendingChangeLines />
                        <MailboxActsProvider
                            session={signedIn}
                            transport={deployment.transport}
                            online
                            flags={flags}
                            moves={moves}
                            deletes={deletes}
                        >
                            {children}
                        </MailboxActsProvider>
                    </PendingChangesProvider>
                </ToastsProvider>
            </LocalizationProvider>
        );
    }

    const drawn = renderHook(() => useMailboxActs(), { wrapper: Surrounded });

    return {
        held: () => drawn.result.current,
        signIn: (next) => {
            signedIn = next;
            drawn.rerender();
        },
    };
}

/** What was submitted to a mutation route, which is the whole of what the deployment was asked to write down. */
function submitted(deployment: Deployment): { readonly path: string; readonly body: unknown }[] {
    return deployment.requests
        .filter((request) => request.path.includes('/mutations/'))
        .map((request) => ({ path: request.path, body: JSON.parse(request.body ?? '{}') as unknown }));
}

function perform(held: () => MailboxActs, ...asked: Parameters<MailboxActs['perform']>): void {
    act(() => {
        held().perform(...asked);
    });
}

/** Moves the fake clock on, letting every answer already on its way land first. */
async function pass(milliseconds: number): Promise<void> {
    await act(async () => {
        await vi.advanceTimersByTimeAsync(milliseconds);
    });
}

/** A folder chosen in the move dialog, which is what asking a move again has to name a second time. */
const chosen: MoveDestination = { alias: 'work-archive', name: 'Archive', role: 'Archive' };

afterEach(() => {
    vi.useRealTimers();
});

/** More messages in the trash than one call may name, which makes deleting them two submissions rather than one. */
const heapedInTrash: ActedMessage[] = Array.from({ length: mostMessagesPerMutation + 1 }, (_, at) => ({
    storedEmailId: `message-${String(at)}`,
    account: 'work',
    folder: 'work-trash',
    unread: false,
}));

/** The records each call to a delete's withdrawal or release route named, one list per call. */
function recordIdsPosted(deployment: Deployment, route: 'withdrawals' | 'releases'): string[][] {
    return submitted(deployment)
        .filter(({ path }) => path.endsWith(`/mutations/deletes/${route}`))
        .map(({ body }) => (body as { recordIds: string[] }).recordIds);
}

/**
 * What the toast surface is holding, which is how a case asserts that an act said nothing.
 *
 * The surface is in the document from the first paint and empty, so what says nothing was reported is that it holds no
 * card — never that the region is absent.
 */
function said(): readonly HTMLElement[] {
    return within(screen.getByRole('list', { name: 'Notices' })).queryAllByRole('listitem');
}

describe('MailboxActsProvider', () => {
    it('asks a deployment to leave a flag where the act puts it, and offers no way back from a flag', async () => {
        const deployment = deploymentAnswering();
        const { held } = acting(deployment);

        perform(held, 'flag', [invoice]);

        await waitFor(() => {
            expect(submitted(deployment)).toStrictEqual([
                {
                    path: 'https://mail.example.invalid/api/client/mutations/flags',
                    body: { changes: [{ storedEmailId: 'message-1', flags: { flagged: true } }] },
                },
            ]);
        });

        expect(screen.queryByRole('button', { name: 'Undo' })).toBeNull();
    });

    it('asks a deployment to take a flag off, which is the other direction of the same change', async () => {
        const deployment = deploymentAnswering();
        const { held } = acting(deployment);

        perform(held, 'unflag', [invoice]);

        await waitFor(() => {
            expect(submitted(deployment)).toStrictEqual([
                {
                    path: 'https://mail.example.invalid/api/client/mutations/flags',
                    body: { changes: [{ storedEmailId: 'message-1', flags: { flagged: false } }] },
                },
            ]);
        });

        expect(screen.queryByRole('button', { name: 'Undo' })).toBeNull();
    });

    it('marks unread by writing that flag alone, so a message that was starred stays starred', async () => {
        const deployment = deploymentAnswering();
        const { held } = acting(deployment);

        perform(held, 'markUnread', [invoice]);

        await waitFor(() => {
            expect(submitted(deployment)[0]?.body).toStrictEqual({
                changes: [{ storedEmailId: 'message-1', flags: { seen: false } }],
            });
        });
    });

    it('marks read by writing that flag alone, which is the other direction of the same change', async () => {
        const deployment = deploymentAnswering();
        const { held } = acting(deployment);

        perform(held, 'markRead', [invoice]);

        await waitFor(() => {
            expect(submitted(deployment)[0]?.body).toStrictEqual({
                changes: [{ storedEmailId: 'message-1', flags: { seen: true } }],
            });
        });
    });

    // What an act that writes a flag does is the mark the row draws from the press, so there is nothing left for a card
    // in the corner to tell anybody. The three that file a message elsewhere report, because the message has left the
    // screen it was on and the toast is the only place the way back is offered.
    it.each<{ named: string; asked: MailboxAct }>([
        { named: 'flagging', asked: 'flag' },
        { named: 'taking a flag off', asked: 'unflag' },
        { named: 'marking unread', asked: 'markUnread' },
        { named: 'marking read', asked: 'markRead' },
    ])('says nothing in the corner about $named, the row having drawn the mark already', async ({ asked }) => {
        const deployment = deploymentAnswering();
        const { held } = acting(deployment);

        perform(held, asked, [invoice]);

        await waitFor(() => {
            expect(submitted(deployment).length).toBe(1);
        });
        await act(async () => {
            await Promise.resolve();
        });

        expect(said()).toStrictEqual([]);
    });

    it('archives into the folder the account labels as its archive, and says how many went', async () => {
        const deployment = deploymentAnswering();
        const { held } = acting(deployment);

        await waitFor(() => {
            expect(held().refusalOf('archive', [invoice])).toBeNull();
        });

        perform(held, 'archive', [invoice, receipt]);

        await screen.findByText('Archived');

        expect(submitted(deployment)[0]).toStrictEqual({
            path: 'https://mail.example.invalid/api/client/mutations/moves',
            body: {
                moves: [
                    { storedEmailId: 'message-1', destinationFolder: 'work-archive' },
                    { storedEmailId: 'message-2', destinationFolder: 'work-archive' },
                ],
            },
        });
        expect(screen.getByText('2 messages')).toBeDefined();
    });

    it('files a delete in the trash folder while the message is somewhere else, and offers the way back', async () => {
        const deployment = deploymentAnswering();
        const { held } = acting(deployment);

        await waitFor(() => {
            expect(held().refusalOf('delete', [invoice])).toBeNull();
        });

        perform(held, 'delete', [invoice]);

        await screen.findByText('Moved to the trash');

        expect(submitted(deployment)[0]).toStrictEqual({
            path: 'https://mail.example.invalid/api/client/mutations/moves',
            body: { moves: [{ storedEmailId: 'message-1', destinationFolder: 'work-trash' }] },
        });
        expect(screen.getByRole('button', { name: 'Undo' })).toBeDefined();
    });

    it('deletes a message already in the trash from the mail server, naming no folder and offering a way back', async () => {
        const deployment = deploymentAnswering();
        const { held } = acting(deployment);

        await waitFor(() => {
            expect(held().deletesPermanently([discarded])).toBe(true);
        });

        perform(held, 'delete', [discarded]);

        await screen.findByText('Deleting permanently…');

        expect(submitted(deployment)[0]).toStrictEqual({
            path: 'https://mail.example.invalid/api/client/mutations/deletes',
            body: { deletes: [{ storedEmailId: 'message-3' }] },
        });
        expect(screen.getByRole('button', { name: 'Undo' })).toBeDefined();
        expect(held().asked.get('message-3')).toStrictEqual({ act: 'delete', from: 'work-trash', leaves: false });
    });

    // The delete waits for exactly as long as the toast stands, so taking it back is cancelling the records it wrote
    // rather than asking for the message again: nothing has reached the mail server to be reversed.
    it('takes a permanent delete back by withdrawing the records it wrote, and stops saying the message is going', async () => {
        const deployment = deploymentAnswering();
        const { held } = acting(deployment);

        await waitFor(() => {
            expect(held().deletesPermanently([discarded])).toBe(true);
        });

        perform(held, 'delete', [discarded]);

        fireEvent.click(await screen.findByRole('button', { name: 'Undo' }));

        await screen.findByText('Kept — nothing was deleted');

        expect(submitted(deployment)).toStrictEqual([
            {
                path: 'https://mail.example.invalid/api/client/mutations/deletes',
                body: { deletes: [{ storedEmailId: 'message-3' }] },
            },
            {
                path: 'https://mail.example.invalid/api/client/mutations/deletes/withdrawals',
                body: { recordIds: ['record-message-3'] },
            },
        ]);
        expect(held().asked.has('message-3')).toBe(false);
    });

    it('says so once where the deployment had already begun the delete it was asked to take back', async () => {
        const deployment = deploymentAnswering({}, 200, ['record-message-3']);
        const { held } = acting(deployment);

        await waitFor(() => {
            expect(held().deletesPermanently([discarded])).toBe(true);
        });

        perform(held, 'delete', [discarded]);

        fireEvent.click(await screen.findByRole('button', { name: 'Undo' }));

        await screen.findByText(/Some of those messages were not changed/);

        expect(screen.queryByText('Kept — nothing was deleted')).toBeNull();
        expect(held().asked.get('message-3')?.act).toBe('delete');
    });

    // A notification closed without the way back taken is the person letting the delete go, so the deployment is told
    // at once rather than left to sit out a window nobody is watching any more.
    it('asks the deployment to stop waiting once the toast goes without the way back taken', async () => {
        const deployment = deploymentAnswering();
        const { held } = acting(deployment);

        await waitFor(() => {
            expect(held().deletesPermanently([discarded])).toBe(true);
        });

        perform(held, 'delete', [discarded]);

        await screen.findByText('Deleting permanently…');
        fireEvent.click(screen.getByRole('button', { name: 'Close' }));

        await waitFor(() => {
            expect(submitted(deployment)[1]).toStrictEqual({
                path: 'https://mail.example.invalid/api/client/mutations/deletes/releases',
                body: { recordIds: ['record-message-3'] },
            });
        });
    });

    // And the row goes with it, which is the other half of the same moment. Until the way back closes the message is
    // still where it was — somebody may take the delete back, and the row has to be there to come back to — so what
    // takes it out of the folder is the offer closing rather than the press. It is the one act in this client where
    // those are two different moments, and a row left saying it was being deleted forever was the defect.
    it('takes the row out of the folder once the way back has closed', async () => {
        const deployment = deploymentAnswering();
        const { held } = acting(deployment);

        await waitFor(() => {
            expect(held().deletesPermanently([discarded])).toBe(true);
        });

        perform(held, 'delete', [discarded]);

        await screen.findByText('Deleting permanently…');

        expect(held().asked.get('message-3')?.leaves).toBe(false);

        fireEvent.click(screen.getByRole('button', { name: 'Close' }));

        await waitFor(() => {
            expect(held().asked.get('message-3')?.leaves).toBe(true);
        });
    });

    it('never releases a delete it took back, the way back and the release being one moment read two ways', async () => {
        const deployment = deploymentAnswering();
        const { held } = acting(deployment);

        await waitFor(() => {
            expect(held().deletesPermanently([discarded])).toBe(true);
        });

        perform(held, 'delete', [discarded]);

        fireEvent.click(await screen.findByRole('button', { name: 'Undo' }));

        await screen.findByText('Kept — nothing was deleted');

        expect(submitted(deployment).map(({ path }) => path)).not.toContain(
            'https://mail.example.invalid/api/client/mutations/deletes/releases',
        );
    });

    // Taken back and let go of a batch at a time, as it was submitted: every record the delete wrote is named exactly
    // once, and no call names more than the route takes.
    it('takes back a delete of more messages than one call may name, a batch at a time', async () => {
        const deployment = deploymentAnswering();
        const { held } = acting(deployment);

        await waitFor(() => {
            expect(held().deletesPermanently(heapedInTrash)).toBe(true);
        });

        perform(held, 'delete', heapedInTrash);

        fireEvent.click(await screen.findByRole('button', { name: 'Undo' }));

        await waitFor(() => {
            expect(recordIdsPosted(deployment, 'withdrawals')).toHaveLength(2);
        });

        const posted = recordIdsPosted(deployment, 'withdrawals');

        expect(posted.map((batch) => batch.length).sort((left, right) => left - right)).toStrictEqual([
            1,
            mostMessagesPerMutation,
        ]);
        expect(new Set(posted.flat())).toStrictEqual(
            new Set(heapedInTrash.map(({ storedEmailId }) => `record-${storedEmailId}`)),
        );
    });

    it('lets go of a delete of more messages than one call may name, a batch at a time', async () => {
        const deployment = deploymentAnswering();
        const { held } = acting(deployment);

        await waitFor(() => {
            expect(held().deletesPermanently(heapedInTrash)).toBe(true);
        });

        perform(held, 'delete', heapedInTrash);

        await screen.findByRole('button', { name: 'Undo' });
        fireEvent.click(screen.getByRole('button', { name: 'Close' }));

        await waitFor(() => {
            expect(recordIdsPosted(deployment, 'releases')).toHaveLength(2);
        });

        const posted = recordIdsPosted(deployment, 'releases');

        expect(posted.map((batch) => batch.length).sort((left, right) => left - right)).toStrictEqual([
            1,
            mostMessagesPerMutation,
        ]);
        expect(new Set(posted.flat())).toStrictEqual(
            new Set(heapedInTrash.map(({ storedEmailId }) => `record-${storedEmailId}`)),
        );
    });

    it('files a selection reaching outside the trash rather than destroying the part of it already there', async () => {
        const deployment = deploymentAnswering();
        const { held } = acting(deployment);

        await waitFor(() => {
            expect(held().refusalOf('delete', [invoice])).toBeNull();
        });

        perform(held, 'delete', [discarded, invoice]);

        await screen.findByText('Moved to the trash');

        expect(submitted(deployment)[0]?.path).toBe('https://mail.example.invalid/api/client/mutations/moves');
    });

    it('takes an archive back by filing each message where it was, rather than by unsaying the first record', async () => {
        const deployment = deploymentAnswering();
        const { held } = acting(deployment);

        await waitFor(() => {
            expect(held().refusalOf('archive', [invoice])).toBeNull();
        });

        perform(held, 'archive', [invoice]);

        fireEvent.click(await screen.findByRole('button', { name: 'Undo' }));

        await screen.findByText('Put back where it was');

        expect(submitted(deployment)[1]).toStrictEqual({
            path: 'https://mail.example.invalid/api/client/mutations/moves',
            body: { moves: [{ storedEmailId: 'message-1', destinationFolder: 'work-inbox' }] },
        });
    });

    // The mailbox may have moved on between the act and the press, so the way back is read per message exactly as the
    // act is: a row whose reverse move was not written down is still on its way to where the act put it.
    it('counts only the messages the way back was written down for, and goes on claiming the rest', async () => {
        const archived = deploymentAnswering();
        const refusingTheWayBack = deploymentAnswering({ 'message-2': 'message-not-found' });

        // The archive is written down for both, and the reverse move — the one naming the folder they came from — is
        // refused for one of them, which is the mailbox having moved on between the press and the way back.
        const deployment: Deployment = {
            requests: archived.requests,
            transport: (request) =>
                (request.body ?? '').includes('work-inbox')
                    ? refusingTheWayBack.transport(request)
                    : archived.transport(request),
        };

        const { held } = acting(deployment);

        await waitFor(() => {
            expect(held().refusalOf('archive', [invoice])).toBeNull();
        });

        perform(held, 'archive', [invoice, receipt]);

        fireEvent.click(await screen.findByRole('button', { name: 'Undo' }));

        await screen.findByText('Put back where it was');
        await screen.findByText('One message was not put back.');

        expect(screen.getByText('That mail is no longer in the mailbox this deployment reads.')).toBeDefined();
        expect(screen.getByText('1 message')).toBeDefined();
        await waitFor(() => {
            expect(held().asked.has('message-1')).toBe(false);
        });
        expect(held().asked.get('message-2')?.act).toBe('archive');
    });

    it('says a message is being acted on from the press, which is what a row draws while an account is unreachable', async () => {
        const deployment = deploymentAnswering();
        const { held } = acting(deployment);

        perform(held, 'flag', [invoice]);

        expect(held().asked.get('message-1')?.act).toBe('flag');

        await waitFor(() => {
            expect(submitted(deployment).length).toBe(1);
        });

        expect(held().asked.get('message-1')?.act).toBe('flag');
    });

    it('stops claiming a message the deployment answered for without writing anything down', async () => {
        const deployment = deploymentAnswering({ 'message-2': 'message-not-found' });
        const { held } = acting(deployment);

        perform(held, 'flag', [invoice, receipt]);

        await screen.findByText('One message was not flagged.');

        // Waited for rather than read straight after the toast: what a row draws and what a toast says are two
        // separate things this provider writes, and asserting one the instant the other appears is an assumption about
        // which render carried them.
        await waitFor(() => {
            expect(held().asked.has('message-2')).toBe(false);
        });

        expect(held().asked.get('message-1')?.act).toBe('flag');
    });

    it('says an act that never reached the deployment changed nothing, and claims nothing about the message', async () => {
        const deployment = deploymentAnswering({}, 403);
        const { held } = acting(deployment);

        perform(held, 'flag', [invoice]);

        await screen.findByText('This change did not reach your deployment.');

        await waitFor(() => {
            expect(held().asked.has('message-1')).toBe(false);
        });
    });

    // A submission over the bound is several batches and they do not answer together. What one of them was written
    // down for is written down, however the batch beside it ended: a row cleared on the strength of somebody else's
    // failure is a client disagreeing with the mailbox about mail the deployment is holding.
    it('keeps what one batch wrote down when the batch beside it never reached the deployment', async () => {
        const requests: ClientRequest[] = [];
        const spread = Array.from({ length: mostMessagesPerMutation + 1 }, (_, at) => ({
            storedEmailId: `message-${String(at)}`,
            account: 'work',
            folder: 'work-inbox',
            unread: false,
        }));

        const deployment: Deployment = {
            requests,
            transport: (request) => {
                requests.push(request);

                if (request.path.endsWith('/folders')) {
                    return Promise.resolve({ status: 200, body: folders, headers: {} });
                }

                const asked = JSON.parse(request.body ?? '{}') as { changes?: { storedEmailId: string }[] };
                const changes = asked.changes ?? [];

                // The full batch is written down and the one message left over never gets an answer at all.
                return Promise.resolve(
                    changes.length === mostMessagesPerMutation
                        ? {
                              status: 200,
                              body: JSON.stringify({
                                  results: changes.map(({ storedEmailId }) => ({ storedEmailId, outcome: 'recorded' })),
                              }),
                              headers: {},
                          }
                        : { status: 503, body: '', headers: {} },
                );
            },
        };

        const { held } = acting(deployment);

        perform(held, 'flag', spread);

        await screen.findByText('This change did not reach your deployment.');

        await waitFor(() => {
            expect(held().asked.has(`message-${String(mostMessagesPerMutation)}`)).toBe(false);
        });

        expect(held().asked.get('message-0')?.act).toBe('flag');
    });

    it.each<{ named: string; act: MailboxAct; message: ActedMessage; destination?: MoveDestination }>([
        { named: 'a flag', act: 'flag', message: invoice },
        { named: 'a flag taken off', act: 'unflag', message: invoice },
        { named: 'a message marked unread', act: 'markUnread', message: invoice },
        { named: 'an archive', act: 'archive', message: invoice },
        { named: 'a delete into the trash', act: 'delete', message: invoice },
        { named: 'a delete out of the trash', act: 'delete', message: discarded },
        { named: 'a move', act: 'move', message: invoice, destination: chosen },
    ])('follows $named it wrote down until the mailbox has taken it', async ({ act, message, destination }) => {
        const { held } = acting(deploymentAnswering());

        await waitFor(() => {
            expect(held().refusalOf(act, [message])).toBeNull();
        });

        perform(held, act, [message], destination);

        expect(await screen.findByText('One change has not reached your mailbox yet.')).toBeDefined();
    });

    // One sentence per outcome whichever surface asked: the reason is the deployment's answer and the same for every
    // act, and what did not happen is the act's own.
    it.each<{ act: MailboxAct; destination?: MoveDestination; said: string }>([
        { act: 'flag', said: 'One message was not flagged.' },
        { act: 'unflag', said: 'The flag was not removed from one message.' },
        { act: 'markUnread', said: 'One message was not marked unread.' },
        { act: 'archive', said: 'One message was not archived.' },
        { act: 'delete', said: 'One message was not deleted.' },
        { act: 'move', destination: chosen, said: 'One message was not filed.' },
    ])('words a refused $act the way the queue words every refusal', async ({ act, destination, said }) => {
        const { held } = acting(deploymentAnswering({ 'message-1': 'message-not-found' }));

        await waitFor(() => {
            expect(held().refusalOf(act, [invoice])).toBeNull();
        });

        perform(held, act, [invoice], destination);

        expect(await screen.findByText(said)).toBeDefined();
        expect(screen.getByText('That mail is no longer in the mailbox this deployment reads.')).toBeDefined();
    });

    // Asking again is the act performed afresh, so a move names the folder it named the first time rather than
    // whatever a second reading of the account would pick.
    it('asks a move the account stopped retrying again, into the folder the move first named', async () => {
        vi.useFakeTimers();

        const deployment = deploymentAnswering({}, 200, [], 'dead-lettered');
        const { held } = acting(deployment);

        await pass(0);
        perform(held, 'move', [invoice], chosen);
        await pass(0);
        await pass(followedChangeInterval);
        await pass(toastLifetime + toastLeaving);

        expect(screen.getByText('Filed in another folder')).toBeDefined();

        fireEvent.click(screen.getByRole('button', { name: 'Ask again' }));
        await pass(0);

        const filedInto = { moves: [{ storedEmailId: 'message-1', destinationFolder: 'work-archive' }] };

        expect(submitted(deployment).map(({ body }) => body)).toStrictEqual([filedInto, filedInto]);
        expect(held().asked.get('message-1')).toStrictEqual({ act: 'move', from: 'work-inbox', leaves: true });
    });

    it('stops claiming an act the account stopped retrying once the person lets it go', async () => {
        vi.useFakeTimers();

        const { held } = acting(deploymentAnswering({}, 200, [], 'dead-lettered'));

        await pass(0);
        perform(held, 'archive', [invoice]);
        await pass(0);
        await pass(followedChangeInterval);
        await pass(toastLifetime + toastLeaving);

        fireEvent.click(screen.getByRole('button', { name: 'Let it go' }));

        expect(held().asked.has('message-1')).toBe(false);
        expect(screen.queryByText('Archived')).toBeNull();
    });

    // An act answers after the render that asked has gone, and somebody else may have signed in on the tab by then:
    // what the first person's act came to is neither the next person's to be told about nor their queue's to follow.
    it('tells nobody what an act came to when somebody else signed in before it answered', async () => {
        vi.useFakeTimers();

        const late = answeringLate(deploymentAnswering());
        const { held, signIn } = acting(late.deployment);

        await pass(0);
        late.hold();
        perform(held, 'flag', [invoice]);
        signIn(someoneElse);
        late.answer();
        await pass(followedChangeInterval);

        expect(screen.queryByText('Flagged')).toBeNull();
        expect(screen.queryByText('One change has not reached your mailbox yet.')).toBeNull();
    });

    it('tells nobody a message was put back when somebody else signed in before the way back answered', async () => {
        vi.useFakeTimers();

        const late = answeringLate(deploymentAnswering());
        const { held, signIn } = acting(late.deployment);

        await pass(0);
        perform(held, 'archive', [invoice]);
        await pass(0);
        late.hold();
        fireEvent.click(screen.getByRole('button', { name: 'Undo' }));
        signIn(someoneElse);
        late.answer();
        await pass(followedChangeInterval);

        expect(screen.queryByText('Put back where it was')).toBeNull();
    });

    it('asks a deployment for nothing where the credential may not write what the act writes', () => {
        const deployment = deploymentAnswering();
        const { held } = acting(deployment, { flags: false });

        perform(held, 'flag', [invoice]);

        expect(submitted(deployment)).toStrictEqual([]);
    });

    it('says the folders were not read and offers the read again, rather than refusing as if the account had none', async () => {
        const deployment = deploymentAnswering();
        let answered = 503;

        const failing: Deployment = {
            requests: deployment.requests,
            transport: (request) => {
                if (request.path.endsWith('/folders') && answered !== 200) {
                    deployment.requests.push(request);

                    return Promise.resolve({ status: answered, body: '', headers: {} });
                }

                return deployment.transport(request);
            },
        };

        const { held } = acting(failing);

        await screen.findByText('Your folders were not read: unavailable.');

        expect(held().refusalOf('archive', [invoice])).toBe('foldersUnknown');

        answered = 200;
        fireEvent.click(screen.getByRole('button', { name: 'Try again' }));

        await waitFor(() => {
            expect(held().refusalOf('archive', [invoice])).toBeNull();
        });
    });

    // What a folder is *for* is the account's own label rather than anything about its name, and it is the same
    // reading `archive` and `delete` are resolved through — which is why the answer comes from here rather than from
    // a second reader beside the one screen that asks.
    it('names what the account labels the folder a message is in, which is how a drafts folder is recognised', async () => {
        const { held } = acting(deploymentAnswering());

        await waitFor(() => {
            expect(held().folderRoleOf(invoice)).toBe('Inbox');
        });
    });

    it('names none for a folder the account never described, rather than guessing one', async () => {
        const { held } = acting(deploymentAnswering());

        await waitFor(() => {
            expect(held().folderRoleOf(invoice)).toBe('Inbox');
        });

        expect(held().folderRoleOf({ ...invoice, folder: 'work-clients' })).toBeNull();
        expect(held().folderRoleOf({ ...invoice, account: 'nobody' })).toBeNull();
    });

    // A session that may neither file mail nor delete it reads no folders at all, so nothing here says what any of
    // them is for.
    it('names none where the folders were never read, which is a session that files and deletes nothing', () => {
        const { held } = acting(deploymentAnswering(), { moves: false, deletes: false });

        expect(held().folderRoleOf(invoice)).toBeNull();
    });

    it('reads no folders for a credential that may neither file mail nor delete it, both acts being refused', () => {
        const deployment = deploymentAnswering();

        acting(deployment, { moves: false, deletes: false });

        expect(deployment.requests).toStrictEqual([]);
    });

    it('reads the folders for a credential that may only delete, which is what says where the trash is', async () => {
        const deployment = deploymentAnswering();
        const { held } = acting(deployment, { moves: false });

        await waitFor(() => {
            expect(held().refusalOf('delete', [discarded])).toBeNull();
        });

        expect(held().refusalOf('delete', [invoice])).toBe('notOffered');
    });
});
