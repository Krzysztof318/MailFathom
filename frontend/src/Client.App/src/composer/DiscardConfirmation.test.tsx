// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { act, fireEvent, render, renderHook, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { LocalizationProvider } from '../localization/Localization';
import { ScreenLayersContext, useScreenLayerStack, type ScreenLayers } from '../shell/screenLayers';
import { DiscardConfirmation } from './DiscardConfirmation';

function drawConfirmation(written: boolean): {
    discarded: ReturnType<typeof vi.fn>;
    kept: ReturnType<typeof vi.fn>;
    shell: { readonly current: ScreenLayers };
} {
    const discarded = vi.fn();
    const kept = vi.fn();

    // The shell around it, because the composer is what the back gesture meets while it is open and this component is
    // where that is recorded. Its three functions are the same ones for the life of the stack, so the value handed to
    // the provider stays current however the count moves.
    const { result } = renderHook(() => useScreenLayerStack());

    render(
        <LocalizationProvider>
            <ScreenLayersContext value={result.current}>
                <DiscardConfirmation written={written} onDiscard={discarded} onKeep={kept} />
            </ScreenLayersContext>
        </LocalizationProvider>,
    );

    return { discarded, kept, shell: result };
}

/** One press of the back gesture, which is what the shell answers with the topmost surface it holds. */
function theGestureIsUsed(shell: { readonly current: ScreenLayers }): boolean {
    let reached = false;

    act(() => {
        reached = shell.current.closeTop();
    });

    return reached;
}

function close(): void {
    fireEvent.click(screen.getByRole('button', { name: 'Close the message' }));
}

describe('DiscardConfirmation', () => {
    it('closes without asking where there is nothing to lose', () => {
        const { discarded } = drawConfirmation(false);

        close();

        expect(discarded).toHaveBeenCalledTimes(1);
        expect(screen.queryByRole('dialog')).toBeNull();
    });

    it('asks before throwing away what somebody wrote, and names what goes', () => {
        const { discarded } = drawConfirmation(true);

        close();

        expect(discarded).not.toHaveBeenCalled();

        const asked = screen.getByRole('dialog').textContent;

        expect(asked).toContain('Discard this message?');
        expect(asked).toContain('along with the draft your deployment is holding for it');
    });

    it('says that nothing files the words first, so the cost of discarding is read before it is paid', () => {
        drawConfirmation(true);

        close();

        expect(screen.getByRole('dialog').textContent).toContain('there is no way back to these words');
    });

    it('throws it away once that is confirmed', () => {
        const { discarded, kept } = drawConfirmation(true);

        close();
        fireEvent.click(screen.getByRole('button', { name: 'Discard' }));

        expect(discarded).toHaveBeenCalledTimes(1);
        expect(kept).not.toHaveBeenCalled();
    });

    it('files it as a draft instead where that is what was asked for', () => {
        const { discarded, kept } = drawConfirmation(true);

        close();
        fireEvent.click(screen.getByRole('button', { name: 'Save draft' }));

        expect(kept).toHaveBeenCalledTimes(1);
        expect(discarded).not.toHaveBeenCalled();
    });

    it('does neither on the way back to writing', () => {
        const { discarded, kept } = drawConfirmation(true);

        close();
        fireEvent.click(screen.getByRole('button', { name: 'Back to writing' }));

        expect(discarded).not.toHaveBeenCalled();
        expect(kept).not.toHaveBeenCalled();
    });

    // The back gesture reaches the same decision the control does rather than a shorter one, which is the whole reason
    // this component registers itself: a message with words in it is never given up by a gesture.
    it('puts the question on the screen when the back gesture reaches the composer', () => {
        const { discarded, shell } = drawConfirmation(true);

        expect(theGestureIsUsed(shell)).toBe(true);

        expect(screen.getByRole('dialog').hasAttribute('open')).toBe(true);
        expect(discarded).not.toHaveBeenCalled();
    });

    it('closes on the gesture without asking where there is nothing to lose', () => {
        const { discarded, shell } = drawConfirmation(false);

        theGestureIsUsed(shell);

        expect(discarded).toHaveBeenCalledTimes(1);
        expect(screen.queryByRole('dialog')).toBeNull();
    });

    // The press that asked the question was spent on the composer, which is still on the screen behind it — so once
    // that question has been answered the next press has to find the composer there rather than fall past it.
    it('meets the gesture again once the question it asked has been answered', () => {
        const { shell } = drawConfirmation(true);

        theGestureIsUsed(shell);
        fireEvent.click(screen.getByRole('button', { name: 'Back to writing' }));

        expect(screen.queryByRole('dialog')).toBeNull();
        expect(theGestureIsUsed(shell)).toBe(true);
        expect(screen.getByRole('dialog').hasAttribute('open')).toBe(true);
    });
});
