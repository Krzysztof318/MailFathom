// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { LocalizationProvider } from '../localization/Localization';
import { BlockingOverlay } from './BlockingOverlay';
import type { BlockingOperation } from './useBlocking';

// The surface is the platform's own modal dialog, and `vitest.setup.ts` stands in for the two methods jsdom does not
// implement: the `open` state and the answer `close()` records. What that leaves reachable here is everything this file
// decides — which of the two variants is drawn, that nothing closes it but the control, and that releasing it takes it
// off the screen. What it leaves unreachable is everything the platform decides: the top layer, the inertness behind
// it, the focus moving into it, the focus held there, and the focus returned afterwards. jsdom has no top layer to
// hold or restore focus from, so those five belong to a browser rather than to an assertion written here — which is
// exactly the split `fullHtml/ShowFullHtml.test.tsx` records for the same reason.
//
// Nothing here reads a clock. The overlay runs nothing, times nothing, and animates nothing it decides itself: how far
// an operation has got is a value it is handed, so a fake clock would have no reading to fix.

function migrating(operation: Partial<BlockingOperation> = {}): BlockingOperation {
    return {
        title: 'Moving this mailbox',
        explanation: 'Everything is being moved to the new server, and stopping halfway would split it between two.',
        stoppingLeavesBehind: 'What has already moved stays on the new server, and the rest stays where it is.',
        stop: vi.fn(),
        ...operation,
    };
}

function drawing(operation: BlockingOperation | null): { rerender: (next: BlockingOperation | null) => void } {
    const drawn = render(
        <LocalizationProvider>
            <BlockingOverlay operation={operation} />
        </LocalizationProvider>,
    );

    return {
        rerender: (next) => {
            drawn.rerender(
                <LocalizationProvider>
                    <BlockingOverlay operation={next} />
                </LocalizationProvider>,
            );
        },
    };
}

describe('BlockingOverlay', () => {
    it('draws nothing at all while nothing is blocking the client', () => {
        drawing(null);

        expect(screen.queryByRole('heading', { name: 'Moving this mailbox' })).toBeNull();
        expect(screen.queryByRole('progressbar')).toBeNull();
    });

    it('says what is happening and why the client is blocked while it happens', () => {
        drawing(migrating());

        expect(screen.getByRole('heading', { name: 'Moving this mailbox' })).toBeDefined();
        expect(screen.getByText(/stopping halfway would split it between two/)).toBeDefined();
        expect(screen.getByText('Do not close the application — the operation is still running.')).toBeDefined();
    });

    it('advances the reading an operation that knows how far it has got supplies', () => {
        drawing(migrating({ progress: 0.46 }));

        expect(screen.getByRole('progressbar', { name: 'How far the operation has got' })).toBeDefined();
        expect(screen.getByText('46% — do not close this window')).toBeDefined();
    });

    it('holds a reading inside the bar it is drawn on, so an operation that counts past its own work says 100%', () => {
        drawing(migrating({ progress: 1.04 }));

        expect(screen.getByText('100% — do not close this window')).toBeDefined();
    });

    it('reports no progress at all for an operation that cannot say how far it has got', () => {
        drawing(migrating());

        expect(screen.getByRole('progressbar', { name: 'How far the operation has got' })).toBeDefined();
        expect(screen.getByText('No known finish time')).toBeDefined();
        expect(screen.queryByText(/%/)).toBeNull();
    });

    it('refuses a close request, so Escape leaves the operation running', () => {
        drawing(migrating());

        const closeRequested = new Event('cancel', { bubbles: true, cancelable: true });
        screen.getByRole('dialog', { name: 'Moving this mailbox' }).dispatchEvent(closeRequested);

        expect(closeRequested.defaultPrevented).toBe(true);
        expect(screen.getByRole('heading', { name: 'Moving this mailbox' })).toBeDefined();
    });

    it('stays where it is when something beside it is pressed', () => {
        drawing(migrating());

        fireEvent.click(document.body);

        expect(screen.getByRole('heading', { name: 'Moving this mailbox' })).toBeDefined();
    });

    it('asks before stopping rather than stopping, so one press on the control ends nothing', () => {
        const stopped = vi.fn();

        drawing(migrating({ stop: stopped }));
        fireEvent.click(screen.getByRole('button', { name: 'Cancel' }));

        expect(screen.getByRole('heading', { name: 'Are you sure you want to stop?' })).toBeDefined();
        expect(stopped).not.toHaveBeenCalled();
    });

    it('leaves the screen once the operation stops saying it is running', () => {
        const { rerender } = drawing(migrating());

        rerender(null);

        expect(screen.queryByRole('heading', { name: 'Moving this mailbox' })).toBeNull();
    });
});
