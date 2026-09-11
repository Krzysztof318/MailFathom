// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { browserSchedule } from './signalChannel';

// The wait between two connections, which ends early on the browser's two reasons to think the deployment is reachable
// again. The connection itself is SignalR's and is proven by the deployment it reaches, not here.

/** Starts a wait and answers whether it is over yet. */
function waiting(milliseconds: number): () => boolean {
    let over = false;

    void browserSchedule.wait(milliseconds).then(() => {
        over = true;
    });

    return () => over;
}

function windowIs(state: DocumentVisibilityState): void {
    vi.spyOn(document, 'visibilityState', 'get').mockReturnValue(state);
}

describe('browserSchedule', () => {
    beforeEach(() => {
        vi.useFakeTimers();
    });

    afterEach(() => {
        vi.useRealTimers();
        vi.restoreAllMocks();
    });

    it('ends a wait once its time has passed', async () => {
        const over = waiting(30_000);

        await vi.advanceTimersByTimeAsync(29_999);
        expect(over()).toBe(false);

        await vi.advanceTimersByTimeAsync(1);
        expect(over()).toBe(true);
    });

    it('ends a wait at once when the network comes back', async () => {
        const over = waiting(30_000);

        window.dispatchEvent(new Event('online'));
        await vi.advanceTimersByTimeAsync(0);

        expect(over()).toBe(true);
    });

    it('ends a wait at once when the window comes back to the front', async () => {
        const over = waiting(30_000);

        windowIs('visible');
        document.dispatchEvent(new Event('visibilitychange'));
        await vi.advanceTimersByTimeAsync(0);

        expect(over()).toBe(true);
    });

    it('goes on waiting when the window is put out of sight', async () => {
        const over = waiting(30_000);

        windowIs('hidden');
        document.dispatchEvent(new Event('visibilitychange'));
        await vi.advanceTimersByTimeAsync(0);

        expect(over()).toBe(false);
    });
});
