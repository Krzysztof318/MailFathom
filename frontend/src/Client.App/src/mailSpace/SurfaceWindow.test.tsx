// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { fireEvent, render, renderHook, screen, within } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { ScreenLayersContext, useScreenLayerStack } from '../shell/screenLayers';
import { SurfaceWindow } from './SurfaceWindow';

// What this suite can say and what it cannot is the division `settings/Settings.test.tsx` works under: jsdom carries
// the element and its `close` event, and carries none of what a modal actually is — the top layer, the backdrop, the
// focus trap, and the focus handed back as it closes. So what is proven here is that every way out of the window leads
// through the element, which is what earns the platform those four; the browser suite is where they are asked of a
// real one. The two sizes the design draws are in the token layer and are held against the design by a parity capture,
// jsdom computing no styles at all.

// The surface standing in the window is a stand-in rather than either of the two real ones, because what this file is
// about is the window: a real surface would bring a read, a transport, and every state of its own to a test that
// asserts nothing about any of them. Its words are named here rather than translated for the same reason — nobody
// reads them, and the catalogue describes what somebody reads.
const named = 'What stands in it';
const wayOut = 'Close this view';

function drawWindow(onClosed: () => void): { readonly closeTop: () => boolean; readonly depth: () => number } {
    const { result } = renderHook(() => useScreenLayerStack());

    render(
        <ScreenLayersContext value={result.current}>
            <SurfaceWindow label={named} drawn="markup" onClosed={onClosed}>
                {(close) => (
                    <section aria-label={named}>
                        <button type="button" onClick={close}>
                            {wayOut}
                        </button>
                    </section>
                )}
            </SurfaceWindow>
        </ScreenLayersContext>,
    );

    return { closeTop: () => result.current.closeTop(), depth: () => result.current.depth };
}

function theWindow(): HTMLElement {
    return screen.getByRole('dialog', { name: named });
}

describe('SurfaceWindow', () => {
    it('opens as a modal dialog named for what stands in it, rather than as a panel drawn to look like one', () => {
        drawWindow(vi.fn());

        expect(theWindow().hasAttribute('open')).toBe(true);
    });

    it('draws the surface it was given inside that window', () => {
        drawWindow(vi.fn());

        expect(within(theWindow()).getByRole('button', { name: wayOut })).toBeDefined();
    });

    it('answers the surface own way out through the element, so the platform hands focus back', () => {
        const closed = vi.fn();

        drawWindow(closed);

        const standing = theWindow();

        fireEvent.click(screen.getByRole('button', { name: wayOut }));

        expect(standing.hasAttribute('open')).toBe(false);
        expect(closed).toHaveBeenCalledTimes(1);
    });

    it('stands over the screen as one layer, so one press of the back gesture closes it and no more', () => {
        const closed = vi.fn();
        const layers = drawWindow(closed);
        const standing = theWindow();

        expect(layers.depth()).toBe(1);
        expect(layers.closeTop()).toBe(true);

        expect(standing.hasAttribute('open')).toBe(false);
        expect(closed).toHaveBeenCalledTimes(1);
    });
});
