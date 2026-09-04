// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { act, fireEvent, renderHook, screen, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { swipeDistance, swipeDrift } from '../controls/swipeDismissal';
import { LocalizationProvider } from '../localization/Localization';
import { ToastsProvider } from './Toasts';
import type { ReactNode } from 'react';
import {
    mostToastsShown,
    toastLeaving,
    toastLifetime,
    useToasts,
    type Operation,
    type OperationSettled,
    type Toast,
    type ToastSurface,
} from './useToasts';

// The surface is driven the way a screen drives it — through the hook — rather than by rendering a card with props,
// because what is being proven is what a caller gets: what stands, for how long, and what an answer to the question
// does about the operation behind it. The clock is fake throughout, so a lifetime is asserted rather than waited for.

let surface: ToastSurface | null = null;

function Surrounded({ children }: { readonly children: ReactNode }) {
    return (
        <LocalizationProvider>
            <ToastsProvider>{children}</ToastsProvider>
        </LocalizationProvider>
    );
}

function drawSurface(): void {
    // The surface is built once for the life of the provider, so what the first render answered with is what every
    // screen under it holds — which is what lets a test keep it rather than read it again per act.
    surface = renderHook(() => useToasts(), { wrapper: Surrounded }).result.current;
}

function raise(toast: Toast): void {
    act(() => {
        surface?.raise(toast);
    });
}

function raiseOperation(operation: Operation): OperationSettled {
    const settled: OperationSettled[] = [];

    act(() => {
        const settle = surface?.raiseOperation(operation);

        if (settle !== undefined) {
            settled.push(settle);
        }
    });

    return (outcome) => {
        act(() => {
            settled[0]?.(outcome);
        });
    };
}

function pass(milliseconds: number): void {
    act(() => {
        vi.advanceTimersByTime(milliseconds);
    });
}

function standing(): readonly (string | null)[] {
    return screen.queryAllByRole('listitem').map((item) => item.textContent);
}

/** The control on the card itself, which shares its words with the one inside the question it may open. */
function closeControl(name: string): HTMLElement {
    const [control] = screen.getAllByRole('button', { name });

    if (control === undefined) {
        throw new Error(`No control named ${name} is on the screen.`);
    }

    return control;
}

function answer(name: string): void {
    fireEvent.click(within(screen.getByRole('dialog')).getByRole('button', { name }));
}

/** The bar the toast's remaining life is drawn as, or nothing where the card carries none. */
function lifetimeBar(): HTMLElement | null {
    return screen.getByRole('listitem').querySelector<HTMLElement>('span[aria-hidden="true"]');
}

/** A finger landing on the card, travelling, and lifting again. What it lands on is anything the card draws. */
function swipe(swiped: HTMLElement, across: number, down: number, pointerType = 'touch'): void {
    const landed = { pointerId: 1, pointerType, clientX: 0, clientY: 0 };
    const travelled = { pointerId: 1, pointerType, clientX: across, clientY: down };

    fireEvent.pointerDown(swiped, landed);
    fireEvent.pointerMove(swiped, travelled);
    fireEvent.pointerUp(swiped, travelled);
}

const packing: Operation = {
    title: 'Packing attachments',
    body: '14 files',
    stoppingLeavesBehind: 'The archive is half written, and stopping now throws it away.',
    stop: () => undefined,
};

beforeEach(() => {
    vi.useFakeTimers();
});

afterEach(() => {
    surface = null;
    vi.useRealTimers();
});

describe('ToastsProvider', () => {
    it('says what happened, in a live region that was in the document before there was anything to say', () => {
        drawSurface();

        const region = screen.getByRole('list', { name: 'Notices' });

        expect(region.getAttribute('aria-live')).toBe('polite');
        expect(standing()).toHaveLength(0);

        raise({ kind: 'success', title: 'Message sent', body: 'To: anna@example.test' });

        expect(standing()[0]).toContain('Message sent');
        expect(standing()[0]).toContain('To: anna@example.test');
    });

    it('names each kind for somebody who cannot see the symbol, and announces an error assertively', () => {
        drawSurface();

        // One at a time and read off the top of the stack, because five kinds is more than the stack shows at once —
        // the bound is a rule of its own and is proven by the test beneath this one.
        const named = [
            { kind: 'neutral', title: 'Three threads archived', said: 'Confirmation' },
            { kind: 'success', title: 'Message sent', said: 'Success' },
            { kind: 'warning', title: 'Two recipients skipped', said: 'Warning' },
            { kind: 'info', title: 'The index now covers attachments', said: 'Information' },
            { kind: 'error', title: 'The message was not sent', said: 'Error' },
        ] as const;

        for (const { kind, title, said } of named) {
            raise({ kind, title });

            expect(standing()[0]).toContain(`${said} ${title}`);
        }

        expect(screen.getByRole('alert').textContent).toContain('The message was not sent');
    });

    it('drops the oldest once the stack is full', () => {
        drawSurface();

        for (let raised = 1; raised <= mostToastsShown + 1; raised += 1) {
            raise({ kind: 'neutral', title: `Toast ${String(raised)}` });
        }

        expect(standing()).toHaveLength(mostToastsShown);
        expect(standing().join(' ')).not.toContain('Toast 1');
        expect(standing()[0]).toContain(`Toast ${String(mostToastsShown + 1)}`);
    });

    it('takes a toast away at the end of its own lifetime', () => {
        drawSurface();

        raise({ kind: 'neutral', title: 'Three threads archived' });

        pass(toastLifetime - 1);

        expect(standing()).toHaveLength(1);

        pass(1 + toastLeaving);

        expect(standing()).toHaveLength(0);
    });

    it('draws that lifetime as a bar running for exactly as long as the toast is held', () => {
        drawSurface();

        raise({ kind: 'neutral', title: 'Three threads archived' });

        expect(lifetimeBar()?.style.animationDuration).toBe(`${String(toastLifetime)}ms`);
    });

    it('draws no bar on an operation, which has no lifetime to run out', () => {
        drawSurface();

        raiseOperation(packing);

        expect(lifetimeBar()).toBeNull();
    });

    it('offers the one thing there is to do about it', () => {
        drawSurface();

        const undone = vi.fn();

        raise({ kind: 'neutral', title: 'Three threads archived', action: { label: 'Undo', take: undone } });

        fireEvent.click(screen.getByRole('button', { name: 'Undo' }));
        pass(toastLeaving);

        expect(undone).toHaveBeenCalledTimes(1);

        // Taking the action is what the toast was standing for, so it goes with it rather than staying to be pressed
        // a second time.
        expect(standing()).toHaveLength(0);
    });

    it('closes one standing for something that already happened, without asking', () => {
        drawSurface();

        raise({ kind: 'neutral', title: 'Three threads archived' });
        fireEvent.click(closeControl('Close'));
        pass(toastLeaving);

        expect(standing()).toHaveLength(0);
    });

    it('is dismissed by a finger swiped across it, in either direction', () => {
        drawSurface();

        raise({ kind: 'neutral', title: 'Three threads archived' });
        swipe(screen.getByText('Three threads archived'), swipeDistance, 0);
        pass(toastLeaving);

        expect(standing()).toHaveLength(0);

        raise({ kind: 'neutral', title: 'Three threads archived' });
        swipe(screen.getByText('Three threads archived'), -swipeDistance, 0);
        pass(toastLeaving);

        expect(standing()).toHaveLength(0);
    });

    it('stays where a finger was scrolling rather than swiping', () => {
        drawSurface();

        raise({ kind: 'neutral', title: 'Three threads archived' });
        swipe(screen.getByText('Three threads archived'), swipeDistance, swipeDrift + 1);
        pass(toastLeaving);

        expect(standing()).toHaveLength(1);
    });

    it('stays where a mouse was dragged across it, the close control being what a pointer has', () => {
        drawSurface();

        raise({ kind: 'neutral', title: 'Three threads archived' });
        swipe(screen.getByText('Three threads archived'), swipeDistance, 0, 'mouse');
        pass(toastLeaving);

        expect(standing()).toHaveLength(1);
    });

    it('leaves an operation standing however long it runs', () => {
        drawSurface();

        raiseOperation(packing);

        pass(toastLifetime * 4);

        expect(standing()[0]).toContain('In progress Packing attachments');
    });

    it('asks before closing an operation, and stops nothing while the question is unanswered', () => {
        drawSurface();

        const stopped = vi.fn();

        raiseOperation({ ...packing, stop: stopped });
        fireEvent.click(closeControl('Stop the operation'));

        const asked = screen.getByRole('dialog').textContent;

        expect(asked).toContain('Stop the operation?');
        expect(asked).toContain('stopping now throws it away');
        expect(stopped).not.toHaveBeenCalled();
    });

    it('keeps the operation running where that is the answer', () => {
        drawSurface();

        const stopped = vi.fn();

        raiseOperation({ ...packing, stop: stopped });
        fireEvent.click(closeControl('Stop the operation'));
        answer('Keep going');

        expect(stopped).not.toHaveBeenCalled();
        expect(standing()[0]).toContain('Packing attachments');
    });

    it('stops it on the other answer, and says that nothing was written', () => {
        drawSurface();

        const stopped = vi.fn();

        raiseOperation({ ...packing, stop: stopped });
        fireEvent.click(closeControl('Stop the operation'));
        answer('Stop the operation');
        pass(toastLeaving);

        expect(stopped).toHaveBeenCalledTimes(1);
        expect(standing()).toEqual([expect.stringContaining('Warning Stopped')]);
        expect(standing()[0]).toContain('nothing changed');
    });

    it('asks the same question of a swipe, so no gesture aborts an operation more quietly than the button', () => {
        drawSurface();

        const stopped = vi.fn();

        raiseOperation({ ...packing, stop: stopped });
        swipe(screen.getByText('Packing attachments'), swipeDistance, 0);

        expect(screen.getByRole('dialog').textContent).toContain('Stop the operation?');
        expect(stopped).not.toHaveBeenCalled();

        answer('Stop the operation');
        pass(toastLeaving);

        expect(stopped).toHaveBeenCalledTimes(1);
    });

    it('closes the question itself where the operation settles while it stands', () => {
        drawSurface();

        const stopped = vi.fn();

        const settle = raiseOperation({ ...packing, stop: stopped });

        fireEvent.click(closeControl('Stop the operation'));

        expect(screen.queryByRole('dialog')).not.toBeNull();

        // Finishing is not an answer to the question, and the question is no longer worth answering — so it goes
        // through the dialog's own close rather than being taken off the screen with focus inside it.
        settle({ kind: 'success', title: 'Archive ready' });

        expect(screen.queryByRole('dialog')).toBeNull();
        expect(stopped).not.toHaveBeenCalled();
        expect(standing()[0]).toContain('Success Archive ready');
    });

    it('becomes the outcome in place, and behaves like any other toast from there', () => {
        drawSurface();

        const settle = raiseOperation(packing);

        settle({ kind: 'success', title: 'Archive ready', body: 'attachments.zip' });

        expect(standing()).toEqual([expect.stringContaining('Success Archive ready')]);
        expect(standing()[0]).toContain('attachments.zip');

        pass(toastLifetime + toastLeaving);

        expect(standing()).toHaveLength(0);
    });
});
