// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { act, renderHook, waitFor } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import type { ClientRequest, ClientSession, MailFathomTransport } from '@mailfathom/client-backend';
import { useRunStopping } from './useRunStopping';

const session: ClientSession = {
    baseAddress: 'https://mail.example.invalid',
    authorization: 'Basic dGVzdA==',
};

const run = '6f1b0a8c-2d3e-4f50-9a1b-7c8d9e0f1a2b';
const anotherRun = 'a7c2f5d1-0e4b-4a62-8c31-2b5d6e7f8a90';

function answering(status: number): { transport: MailFathomTransport; requests: ClientRequest[] } {
    const requests: ClientRequest[] = [];

    return {
        requests,
        transport: (request) => {
            requests.push(request);

            return Promise.resolve({ status, body: '', headers: {} });
        },
    };
}

describe('useRunStopping', () => {
    it('asks the deployment to stop the run rather than only stopping the reading', async () => {
        const { transport, requests } = answering(204);

        const { result } = renderHook(() => useRunStopping(session, transport, run));

        act(() => {
            result.current.stop();
        });

        // Said before the deployment has answered, because somebody who pressed the control is owed the screen saying
        // so rather than a round trip of nothing happening.
        expect(result.current.stopping).toBe('asking');

        await waitFor(() => {
            expect(requests).toHaveLength(1);
        });

        expect(requests[0]?.method).toBe('DELETE');
        expect(requests[0]?.path).toContain(run);
        expect(result.current.stopping).toBe('asking');
    });

    it('says the run is still going when the stop did not reach the deployment', async () => {
        const { result } = renderHook(() => useRunStopping(session, () => Promise.reject(new Error('down')), run));

        act(() => {
            result.current.stop();
        });

        await waitFor(() => {
            expect(result.current.stopping).toBe('refused');
        });
    });

    it('leaves a run the deployment no longer holds as stopped, which is what the reading says as well', async () => {
        const { transport, requests } = answering(404);

        const { result } = renderHook(() => useRunStopping(session, transport, run));

        act(() => {
            result.current.stop();
        });

        await waitFor(() => {
            expect(requests).toHaveLength(1);
        });

        expect(result.current.stopping).toBe('asking');
    });

    it('asks nothing while the screen is drawing no run', () => {
        const { transport, requests } = answering(204);

        const { result } = renderHook(() => useRunStopping(session, transport, null));

        act(() => {
            result.current.stop();
        });

        expect(result.current.stopping).toBe('idle');
        expect(requests).toHaveLength(0);
    });

    it('asks nothing while nobody is signed in', () => {
        const { transport, requests } = answering(204);

        const { result } = renderHook(() => useRunStopping(null, transport, run));

        act(() => {
            result.current.stop();
        });

        expect(result.current.stopping).toBe('idle');
        expect(requests).toHaveLength(0);
    });

    it('never draws one run stopping against another', () => {
        const { transport } = answering(204);

        const { result, rerender } = renderHook(
            ({ following }: { following: string }) => useRunStopping(session, transport, following),
            { initialProps: { following: run } },
        );

        act(() => {
            result.current.stop();
        });

        expect(result.current.stopping).toBe('asking');

        rerender({ following: anotherRun });

        expect(result.current.stopping).toBe('idle');
    });
});
