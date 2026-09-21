// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { renderHook, waitFor } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import type { ClientRequest, ClientSession, MailFathomTransport } from '@mailfathom/client-backend';
import type { AskedQuestion } from '../workspace/askScope';
import { useStartedRun } from './useStartedRun';

const session: ClientSession = { baseAddress: 'https://mail.example.invalid', authorization: 'Basic dGVzdA==' };

const runId = '6f1b0a8c-2d3e-4f50-9a1b-7c8d9e0f1a2b';

// Each press of the field writes an entry of its own, which is what this hook reads as the ask — so a test asking twice
// builds two objects even where the words are the same, and every case below holds its entry across renders the way the
// workspace holds it. One built inside the render callback would be a new question on every render, which is a run
// started per render.
function asked(question: string): AskedQuestion {
    return { question, scope: { kind: 'mail', scope: { kind: 'everything' } } };
}

function accepting(runs: readonly string[]): {
    readonly transport: MailFathomTransport;
    readonly asks: ClientRequest[];
} {
    const asks: ClientRequest[] = [];

    return {
        asks,
        transport: (request) => {
            const answered = runs[Math.min(asks.length, runs.length - 1)] ?? runId;

            asks.push(request);

            return Promise.resolve({ status: 202, body: JSON.stringify({ runId: answered }), headers: {} });
        },
    };
}

describe('useStartedRun', () => {
    it('asks nothing where nothing has been asked', () => {
        const { transport, asks } = accepting([runId]);

        const { result } = renderHook(() => useStartedRun(session, transport, null));

        expect(result.current).toEqual({ run: null, starting: false, failure: null });
        expect(asks).toHaveLength(0);
    });

    it('reads a question as being asked from the render it was recorded in', () => {
        const { transport } = accepting([runId]);
        const question = asked('Where is the addendum?');

        const { result } = renderHook(() => useStartedRun(session, transport, question));

        expect(result.current.starting).toBe(true);
    });

    it('answers with the run the deployment accepted', async () => {
        const { transport } = accepting([runId]);
        const question = asked('Where is the addendum?');

        const { result } = renderHook(() => useStartedRun(session, transport, question));

        await waitFor(() => {
            expect(result.current).toEqual({ run: runId, starting: false, failure: null });
        });
    });

    it('asks once for one question however often the screen renders', async () => {
        const { transport, asks } = accepting([runId]);
        const question = asked('Where is the addendum?');

        const { result, rerender } = renderHook(() => useStartedRun(session, transport, question));

        await waitFor(() => {
            expect(result.current.run).toBe(runId);
        });

        rerender();
        rerender();

        expect(asks).toHaveLength(1);
    });

    it('starts a second run for the same words asked again, which is a second question', async () => {
        const { transport, asks } = accepting([runId, 'a-second-run']);
        let question = asked('Where is the addendum?');

        const { result, rerender } = renderHook(() => useStartedRun(session, transport, question));

        await waitFor(() => {
            expect(result.current.run).toBe(runId);
        });

        question = asked('Where is the addendum?');
        rerender();

        await waitFor(() => {
            expect(result.current.run).toBe('a-second-run');
        });

        expect(asks).toHaveLength(2);
    });

    it('draws the newer question as waiting rather than the older run it replaced', async () => {
        const { transport } = accepting([runId]);
        let question = asked('Where is the addendum?');

        const { result, rerender } = renderHook(() => useStartedRun(session, transport, question));

        await waitFor(() => {
            expect(result.current.run).toBe(runId);
        });

        question = asked('And what did it change?');
        rerender();

        expect(result.current).toEqual({ run: null, starting: true, failure: null });
    });

    it('says why a question was not accepted rather than waiting on a run that was never started', async () => {
        const refusing: MailFathomTransport = () => Promise.resolve({ status: 503, body: '', headers: {} });
        const question = asked('Where is the addendum?');

        const { result } = renderHook(() => useStartedRun(session, refusing, question));

        await waitFor(() => {
            expect(result.current).toEqual({ run: null, starting: false, failure: 'unavailable' });
        });
    });

    it('asks nothing with nobody signed in', () => {
        const { transport, asks } = accepting([runId]);
        const question = asked('Where is the addendum?');

        const { result } = renderHook(() => useStartedRun(null, transport, question));

        expect(result.current).toEqual({ run: null, starting: false, failure: null });
        expect(asks).toHaveLength(0);
    });
});
