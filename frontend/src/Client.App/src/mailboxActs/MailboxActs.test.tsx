// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import type { ReactNode } from 'react';
import { act, fireEvent, renderHook, screen, waitFor } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import type {
    ClientRequest,
    ClientSession,
    MailFathomTransport,
    MailMutationOutcome,
} from '@mailfathom/client-backend';
import { LocalizationProvider } from '../localization/Localization';
import { ToastsProvider } from '../toasts/Toasts';
import { mostMessagesPerMutation } from '@mailfathom/client-backend';
import { MailboxActsProvider } from './MailboxActs';
import { useMailboxActs, type ActedMessage, type MailboxActs } from './useMailboxActs';

// The provider is driven the way a control drives it — through the hook — because what is being proven is what the
// deployment was asked for and what the person was told afterwards. Nothing here reaches a mail server: an act writes
// a record and answers, so every assertion is about the request that carried it and the toast that reported it.

const session: ClientSession = { baseAddress: 'https://mail.example.invalid', authorization: 'Basic dGVzdA==' };

const invoice: ActedMessage = { storedEmailId: 'message-1', account: 'work', folder: 'work-inbox' };
const receipt: ActedMessage = { storedEmailId: 'message-2', account: 'work', folder: 'work-inbox' };
const discarded: ActedMessage = { storedEmailId: 'message-3', account: 'work', folder: 'work-trash' };

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

/** A deployment answering the folders it has, and every submitted batch with the outcomes it was told to answer. */
function deploymentAnswering(outcomes: Readonly<Record<string, MailMutationOutcome>> = {}, status = 200): Deployment {
    const requests: ClientRequest[] = [];

    return {
        requests,
        transport: (request) => {
            requests.push(request);

            if (request.path.endsWith('/folders')) {
                return Promise.resolve({ status: 200, body: folders, headers: {} });
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
                        ({ storedEmailId }) => ({
                            storedEmailId,
                            outcome: outcomes[storedEmailId] ?? 'recorded',
                        }),
                    ),
                }),
                headers: {},
            });
        },
    };
}

function acting(
    deployment: Deployment,
    { flags = true, moves = true, deletes = true }: { flags?: boolean; moves?: boolean; deletes?: boolean } = {},
): { readonly held: () => MailboxActs } {
    function Surrounded({ children }: { readonly children: ReactNode }) {
        return (
            <LocalizationProvider>
                <ToastsProvider>
                    <MailboxActsProvider
                        session={session}
                        transport={deployment.transport}
                        online
                        flags={flags}
                        moves={moves}
                        deletes={deletes}
                    >
                        {children}
                    </MailboxActsProvider>
                </ToastsProvider>
            </LocalizationProvider>
        );
    }

    const drawn = renderHook(() => useMailboxActs(), { wrapper: Surrounded });

    return { held: () => drawn.result.current };
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

describe('MailboxActsProvider', () => {
    it('asks a deployment to leave a flag where the act puts it, and offers no way back from a flag', async () => {
        const deployment = deploymentAnswering();
        const { held } = acting(deployment);

        perform(held, 'flag', [invoice]);

        await screen.findByText('Flagged');

        expect(submitted(deployment)).toStrictEqual([
            {
                path: 'https://mail.example.invalid/api/client/mutations/flags',
                body: { changes: [{ storedEmailId: 'message-1', flags: { flagged: true } }] },
            },
        ]);
        expect(screen.queryByRole('button', { name: 'Undo' })).toBeNull();
    });

    it('asks a deployment to take a flag off, which is the other direction of the same change', async () => {
        const deployment = deploymentAnswering();
        const { held } = acting(deployment);

        perform(held, 'unflag', [invoice]);

        await screen.findByText('Flag removed');

        expect(submitted(deployment)).toStrictEqual([
            {
                path: 'https://mail.example.invalid/api/client/mutations/flags',
                body: { changes: [{ storedEmailId: 'message-1', flags: { flagged: false } }] },
            },
        ]);
        expect(screen.queryByRole('button', { name: 'Undo' })).toBeNull();
    });

    it('marks unread by writing that flag alone, so a message that was starred stays starred', async () => {
        const deployment = deploymentAnswering();
        const { held } = acting(deployment);

        perform(held, 'markUnread', [invoice]);

        await screen.findByText('Marked unread');

        expect(submitted(deployment)[0]?.body).toStrictEqual({
            changes: [{ storedEmailId: 'message-1', flags: { seen: false } }],
        });
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

    it('deletes a message already in the trash from the mail server, naming no folder and offering no way back', async () => {
        const deployment = deploymentAnswering();
        const { held } = acting(deployment);

        await waitFor(() => {
            expect(held().deletesPermanently([discarded])).toBe(true);
        });

        perform(held, 'delete', [discarded]);

        await screen.findByText('Permanently deleted');

        expect(submitted(deployment)[0]).toStrictEqual({
            path: 'https://mail.example.invalid/api/client/mutations/deletes',
            body: { deletes: [{ storedEmailId: 'message-3' }] },
        });
        expect(screen.queryByRole('button', { name: 'Undo' })).toBeNull();
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
        await screen.findByText(
            'Some of those messages were not changed. Your deployment no longer serves them where the list drew them.',
        );

        expect(screen.getByText('1 message')).toBeDefined();
        await waitFor(() => {
            expect(held().asked.has('message-1')).toBe(false);
        });
        expect(held().asked.get('message-2')).toBe('archive');
    });

    it('says a message is being acted on from the press, which is what a row draws while an account is unreachable', async () => {
        const deployment = deploymentAnswering();
        const { held } = acting(deployment);

        perform(held, 'flag', [invoice]);

        expect(held().asked.get('message-1')).toBe('flag');

        await screen.findByText('Flagged');

        expect(held().asked.get('message-1')).toBe('flag');
    });

    it('stops claiming a message the deployment answered for without writing anything down', async () => {
        const deployment = deploymentAnswering({ 'message-2': 'message-not-found' });
        const { held } = acting(deployment);

        perform(held, 'flag', [invoice, receipt]);

        await screen.findByText(
            'Some of those messages were not changed. Your deployment no longer serves them where the list drew them.',
        );

        // Waited for rather than read straight after the toast: what a row draws and what a toast says are two
        // separate things this provider writes, and asserting one the instant the other appears is an assumption about
        // which render carried them.
        await waitFor(() => {
            expect(held().asked.has('message-2')).toBe(false);
        });

        expect(held().asked.get('message-1')).toBe('flag');
        expect(screen.getByText('1 message')).toBeDefined();
    });

    it('says why the act failed and claims nothing, rather than leaving a row saying it is being archived', async () => {
        const deployment = deploymentAnswering({}, 403);
        const { held } = acting(deployment);

        perform(held, 'flag', [invoice]);

        await screen.findByText('That change was not made: unauthorized.');

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

        await screen.findByText('That change was not made: unavailable.');

        expect(screen.getByText('Flagged')).toBeDefined();
        expect(screen.getByText('200 messages')).toBeDefined();

        await waitFor(() => {
            expect(held().asked.has(`message-${String(mostMessagesPerMutation)}`)).toBe(false);
        });

        expect(held().asked.get('message-0')).toBe('flag');
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
