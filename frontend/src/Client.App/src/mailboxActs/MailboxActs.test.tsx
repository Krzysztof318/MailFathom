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
import { MailboxActsProvider } from './MailboxActs';
import { useMailboxActs, type ActedMessage, type MailboxActs } from './useMailboxActs';

// The provider is driven the way a control drives it — through the hook — because what is being proven is what the
// deployment was asked for and what the person was told afterwards. Nothing here reaches a mail server: an act writes
// a record and answers, so every assertion is about the request that carried it and the toast that reported it.

const session: ClientSession = { baseAddress: 'https://mail.example.invalid', authorization: 'Basic dGVzdA==' };

const invoice: ActedMessage = { storedEmailId: 'message-1', account: 'work', folder: 'work-inbox' };
const receipt: ActedMessage = { storedEmailId: 'message-2', account: 'work', folder: 'work-inbox' };

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
            };

            return Promise.resolve({
                status,
                body: JSON.stringify({
                    results: [...(asked.changes ?? []), ...(asked.moves ?? [])].map(({ storedEmailId }) => ({
                        storedEmailId,
                        outcome: outcomes[storedEmailId] ?? 'recorded',
                    })),
                }),
                headers: {},
            });
        },
    };
}

function acting(
    deployment: Deployment,
    { flags = true, moves = true }: { flags?: boolean; moves?: boolean } = {},
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

    it('reads no folders for a credential that may not file mail, an act it refuses needing no destination', () => {
        const deployment = deploymentAnswering();

        acting(deployment, { moves: false });

        expect(deployment.requests).toStrictEqual([]);
    });
});
