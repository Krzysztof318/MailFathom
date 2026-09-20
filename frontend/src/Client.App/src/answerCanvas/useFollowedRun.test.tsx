// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { act, renderHook, waitFor } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import type { ReactNode } from 'react';
import type {
    ClientRequest,
    ClientSession,
    MailFathomTransport,
    RunFollowingSchedule,
} from '@mailfathom/client-backend';
import { SignalledChangesContext, type SignalListener, type SignalledChanges } from '../signals/signalledChanges';
import { useFollowedRun } from './useFollowedRun';

const session: ClientSession = {
    baseAddress: 'https://mail.example.invalid',
    authorization: 'Basic dGVzdA==',
};

const run = '6f1b0a8c-2d3e-4f50-9a1b-7c8d9e0f1a2b';

// A wait that never ends, so nothing here is read again on the follower's own interval: what each test drives is the
// read on mount and the reads a statement causes.
const neverPolls: RunFollowingSchedule = { wait: () => new Promise<void>(() => undefined) };

function tailOf(events: readonly unknown[], running = false): string {
    return JSON.stringify({ running, events });
}

function answering(bodies: readonly string[]): { transport: MailFathomTransport; requests: ClientRequest[] } {
    const requests: ClientRequest[] = [];

    return {
        requests,
        transport: (request) => {
            const body = bodies[Math.min(requests.length, bodies.length - 1)] ?? tailOf([]);
            requests.push(request);

            return Promise.resolve({ status: 200, body, headers: {} });
        },
    };
}

function hearing(): { changes: SignalledChanges; tell: (change: Parameters<SignalListener>[0]) => void } {
    const listeners = new Set<SignalListener>();

    return {
        changes: {
            listen: (listener) => {
                listeners.add(listener);

                return () => listeners.delete(listener);
            },
            refresh: () => undefined,
        },
        tell: (change) => {
            for (const listener of [...listeners]) {
                listener(change);
            }
        },
    };
}

function following(transport: MailFathomTransport, changes: SignalledChanges, followed: string | null = run) {
    function wrapper({ children }: { readonly children: ReactNode }) {
        return <SignalledChangesContext.Provider value={changes}>{children}</SignalledChangesContext.Provider>;
    }

    return renderHook(() => useFollowedRun(session, transport, followed, neverPolls), { wrapper });
}

describe('useFollowedRun', () => {
    it('reads the run on mount, so a run already composed draws with no hub at all', async () => {
        const { transport } = answering([
            tailOf([
                { event: 'started', sequence: 1, planSchemaVersion: 2 },
                { event: 'block', sequence: 2, block: { type: 'answer' } },
            ]),
        ]);

        const { result } = following(transport, hearing().changes);

        await waitFor(() => {
            expect(result.current.running).toBe(false);
        });

        expect(result.current.blocks.map((arrived) => arrived.block.named)).toEqual(['answer']);
        expect(result.current.planSchemaVersion).toBe(2);
    });

    it('reads again from its cursor when the deployment says the run advanced', async () => {
        const { transport, requests } = answering([
            tailOf([{ event: 'block', sequence: 2, block: { type: 'answer' } }], true),
            tailOf([{ event: 'block', sequence: 3, block: { type: 'timeline' } }]),
        ]);

        const heard = hearing();
        const { result } = following(transport, heard.changes);

        await waitFor(() => {
            expect(result.current.blocks).toHaveLength(1);
        });

        act(() => {
            heard.tell({ kind: 'discovery.run.advanced', run, sequence: 3 });
        });

        await waitFor(() => {
            expect(result.current.blocks.map((arrived) => arrived.block.named)).toEqual(['answer', 'timeline']);
        });

        expect(requests[1]?.path).toContain('?since=2');
    });

    it('leaves a run alone when another run advanced', async () => {
        const { transport, requests } = answering([
            tailOf([{ event: 'block', sequence: 2, block: { type: 'answer' } }], true),
        ]);

        const heard = hearing();
        const { result } = following(transport, heard.changes);

        await waitFor(() => {
            expect(result.current.blocks).toHaveLength(1);
        });

        act(() => {
            heard.tell({ kind: 'discovery.run.advanced', run: 'another', sequence: 9 });
        });

        expect(requests).toHaveLength(1);
    });

    it('reads again when the client says something may have been missed', async () => {
        const { transport, requests } = answering([tailOf([], true)]);

        const heard = hearing();
        following(transport, heard.changes);

        await waitFor(() => {
            expect(requests).toHaveLength(1);
        });

        act(() => {
            heard.tell({ kind: 'refresh' });
        });

        await waitFor(() => {
            expect(requests).toHaveLength(2);
        });
    });

    it('reads nothing while the screen is drawing no run', async () => {
        const { transport, requests } = answering([tailOf([])]);

        const { result } = following(transport, hearing().changes, null);

        await waitFor(() => {
            expect(result.current.running).toBe(true);
        });

        expect(requests).toHaveLength(0);
    });

    it('stops reading a run this person does not hold', async () => {
        const requests: ClientRequest[] = [];
        const transport: MailFathomTransport = (request) => {
            requests.push(request);

            return Promise.resolve({ status: 404, body: '', headers: {} });
        };

        const heard = hearing();
        const { result } = following(transport, heard.changes);

        await waitFor(() => {
            expect(result.current.running).toBe(false);
        });

        act(() => {
            heard.tell({ kind: 'discovery.run.advanced', run, sequence: 3 });
        });

        expect(requests).toHaveLength(1);
    });

    it('says the deployment is out of reach rather than dropping what arrived', async () => {
        const transport: MailFathomTransport = () => Promise.reject(new Error('down'));

        const { result } = following(transport, hearing().changes);

        await waitFor(() => {
            expect(result.current.unreachable).toBe(true);
        });

        expect(result.current.running).toBe(true);
    });

    it('stops reading once the screen showing the run goes away', async () => {
        const { transport, requests } = answering([tailOf([], true)]);

        const heard = hearing();
        const { unmount } = following(transport, heard.changes);

        await waitFor(() => {
            expect(requests).toHaveLength(1);
        });

        unmount();

        act(() => {
            heard.tell({ kind: 'discovery.run.advanced', run, sequence: 9 });
        });

        expect(requests).toHaveLength(1);
    });
});
