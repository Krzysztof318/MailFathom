// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { act, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { LocalizationProvider } from '../localization/Localization';
import { SignalledChangesContext, type SignalListener, type SignalledChanges } from '../signals/signalledChanges';
import { Refresh, refreshShownFor } from './Refresh';

/** Changes a test speaks for: a refresh is told to every listener, as the hook tells it, and each press is counted. */
function signalling(): { changes: SignalledChanges; refreshed: () => void; pressed: () => number } {
    const listeners = new Set<SignalListener>();
    let pressed = 0;

    function refreshed(): void {
        for (const listener of [...listeners]) {
            listener({ kind: 'refresh' });
        }
    }

    return {
        changes: {
            listen: (listener) => {
                listeners.add(listener);

                return () => {
                    listeners.delete(listener);
                };
            },
            refresh: () => {
                pressed += 1;
                refreshed();
            },
        },
        refreshed,
        pressed: () => pressed,
    };
}

function drawing(changes: SignalledChanges): HTMLElement {
    render(
        <LocalizationProvider>
            <SignalledChangesContext value={changes}>
                <Refresh />
            </SignalledChangesContext>
        </LocalizationProvider>,
    );

    return screen.getByRole('button', { name: 'Refresh' });
}

describe('Refresh', () => {
    afterEach(() => {
        vi.useRealTimers();
    });

    it('asks the client to refresh on a press, which every screen hears and reads again for', () => {
        const deployment = signalling();

        fireEvent.click(drawing(deployment.changes));

        expect(deployment.pressed()).toBe(1);
    });

    it('says a refresh nobody pressed is under way, exactly as it says one that was pressed', () => {
        const deployment = signalling();
        const control = drawing(deployment.changes);

        expect(control.getAttribute('aria-disabled')).toBe('false');

        act(() => {
            deployment.refreshed();
        });

        expect(control.getAttribute('aria-disabled')).toBe('true');
    });

    it('stops saying so after one beat', () => {
        vi.useFakeTimers();
        const deployment = signalling();
        const control = drawing(deployment.changes);

        fireEvent.click(control);
        act(() => {
            vi.advanceTimersByTime(refreshShownFor - 1);
        });
        expect(control.getAttribute('aria-disabled')).toBe('true');

        act(() => {
            vi.advanceTimersByTime(1);
        });
        expect(control.getAttribute('aria-disabled')).toBe('false');
    });

    it('refuses a second press while one is under way', () => {
        const deployment = signalling();
        const control = drawing(deployment.changes);

        fireEvent.click(control);
        fireEvent.click(control);

        expect(deployment.pressed()).toBe(1);
    });

    it('carries a name that says what it does rather than what it looks like', () => {
        expect(drawing(signalling().changes)).toBeDefined();
    });
});
