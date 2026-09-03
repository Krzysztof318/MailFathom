// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { render } from '@testing-library/react';
import { useEffect } from 'react';
import { describe, expect, it, vi } from 'vitest';
import { BlockingContext, useBlocking, type Blocking } from './useBlocking';

// The application-level call itself, which no screen makes yet: the operations that block the client arrive later, and
// what this proves is that the one way to reach the surface is the one they will find. Both cases are what a wrong
// composition looks like from an operation's side — the provider is there and hands over what it was given, or it is
// absent and saying so loudly is better than an operation that quietly never blocks anything.

function Migration(): null {
    const { block } = useBlocking();

    useEffect(() => {
        block({
            title: 'Moving this mailbox',
            explanation: 'Everything is being moved to the new server.',
            stoppingLeavesBehind: 'What has already moved stays where it went.',
            stop: () => undefined,
        });
    }, [block]);

    return null;
}

describe('useBlocking', () => {
    it('hands an operation the way to block the client that the composition root supplied', () => {
        const blocking: Blocking = { block: vi.fn(), release: vi.fn() };

        render(
            <BlockingContext value={blocking}>
                <Migration />
            </BlockingContext>,
        );

        expect(blocking.block).toHaveBeenCalledWith(expect.objectContaining({ title: 'Moving this mailbox' }));
    });

    it('refuses to be reached from outside the composition root rather than never blocking anything', () => {
        // React reports the failed render on the console as well as throwing it, and the throw is what is being
        // asserted, so the report is silenced rather than left to look like a suite that logged an error.
        const reported = vi.spyOn(console, 'error').mockImplementation(() => undefined);

        expect(() => render(<Migration />)).toThrow(/outside the BlockingContext/u);

        reported.mockRestore();
    });
});
