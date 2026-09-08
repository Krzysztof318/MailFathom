// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { fireEvent, render, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { LocalizationProvider } from '../localization/Localization';
import { WrittenMessage } from './WrittenMessage';
import type { WrittenNode } from './writtenText';

// Two things this component asks the platform for are not in jsdom, and both are stated here rather than mocked away:
// what the pointer can do, which decides whether the bar or the toggle is drawn, and the editing command itself, which
// is the browser's own and is recorded so that each control can be shown to perform its own act.

const declaredMatchMedia = Object.getOwnPropertyDescriptor(window, 'matchMedia');
const declaredExecCommand = Object.getOwnPropertyDescriptor(document, 'execCommand');

let pointerIsCoarse = false;
let commanded: { readonly command: string; readonly argument: string | undefined }[] = [];
let selectedWhenCommanded: string[] = [];

/** What was selected at the moment the region was asked to act, which is what every one of these commands reads. */
function selectedNow(): string {
    const selection = window.getSelection();

    return selection === null || selection.rangeCount === 0 ? '' : selection.getRangeAt(0).toString();
}

/** The editing acts the region was asked to perform, without the styling switch every act sets first. */
function acts(): readonly { readonly command: string; readonly argument: string | undefined }[] {
    return commanded.filter((act) => act.command !== 'styleWithCSS');
}

beforeEach(() => {
    pointerIsCoarse = false;
    commanded = [];
    selectedWhenCommanded = [];

    Object.defineProperty(window, 'matchMedia', {
        configurable: true,
        value: (query: string) => ({
            media: query,
            matches: pointerIsCoarse && query.includes('coarse'),
            addEventListener: () => undefined,
            removeEventListener: () => undefined,
        }),
    });

    Object.defineProperty(document, 'execCommand', {
        configurable: true,
        value: (command: string, _showUserInterface: boolean, argument?: string) => {
            commanded.push({ command, argument });

            if (command !== 'styleWithCSS') {
                selectedWhenCommanded.push(selectedNow());
            }

            return true;
        },
    });
});

afterEach(() => {
    if (declaredMatchMedia === undefined) {
        Reflect.deleteProperty(window, 'matchMedia');
    } else {
        Object.defineProperty(window, 'matchMedia', declaredMatchMedia);
    }

    if (declaredExecCommand === undefined) {
        Reflect.deleteProperty(document, 'execCommand');
    } else {
        Object.defineProperty(document, 'execCommand', declaredExecCommand);
    }
});

function drawWords(opened: readonly WrittenNode[] = []): {
    written: ReturnType<typeof vi.fn>;
    sendAsked: ReturnType<typeof vi.fn>;
} {
    const written = vi.fn();
    const sendAsked = vi.fn();

    render(
        <LocalizationProvider>
            <WrittenMessage opened={opened} onWritten={written} onSendAsked={sendAsked} />
        </LocalizationProvider>,
    );

    return { written, sendAsked };
}

function words(): HTMLElement {
    return screen.getByRole('textbox', { name: 'Message' });
}

function press(name: string): void {
    fireEvent.click(screen.getByRole('button', { name }));
}

/** Selects one run of what is written, the way somebody dragging across it would. */
function select(text: string): void {
    const walk = document.createTreeWalker(words(), NodeFilter.SHOW_TEXT);
    const range = document.createRange();

    while (walk.nextNode()) {
        if (walk.currentNode.nodeValue === text) {
            range.selectNodeContents(walk.currentNode);
        }
    }

    const selection = window.getSelection();

    selection?.removeAllRanges();
    selection?.addRange(range);
}

describe('WrittenMessage', () => {
    it('opens holding what this tab was already writing', () => {
        drawWords([{ element: 'div', address: null, holds: [{ text: 'Half a sentence' }] }]);

        expect(words().textContent).toBe('Half a sentence');
    });

    it('reports what was typed as the closed set the message is kept in', () => {
        const { written } = drawWords();

        words().textContent = 'Here it is.';
        fireEvent.input(words());

        expect(written).toHaveBeenCalledWith([{ text: 'Here it is.' }]);
    });

    it('asks the send question from the design’s own shortcut', () => {
        const { sendAsked } = drawWords();

        fireEvent.keyDown(words(), { key: 'Enter', ctrlKey: true });

        expect(sendAsked).toHaveBeenCalledTimes(1);
    });

    it('takes what was pasted as text rather than as whatever markup it arrived in', () => {
        drawWords();

        fireEvent.paste(words(), { clipboardData: { getData: () => 'Pasted from somewhere else' } });

        expect(acts()).toEqual([{ command: 'insertText', argument: 'Pasted from somewhere else' }]);
    });
});

describe('WrittenMessage, the formatting bar', () => {
    it.each([
        ['Bold', 'bold', undefined],
        ['Italic', 'italic', undefined],
        ['Underline', 'underline', undefined],
        ['Strikethrough', 'strikeThrough', undefined],
        ['Bulleted list', 'insertUnorderedList', undefined],
        ['Numbered list', 'insertOrderedList', undefined],
        ['Quote', 'formatBlock', 'blockquote'],
        ['Clear formatting', 'removeFormat', undefined],
    ])('performs %s on the selection', (named, command, argument) => {
        drawWords();

        press(named);

        expect(acts()).toEqual([{ command, argument }]);
    });

    it('draws the design’s nine controls in the design’s own order', () => {
        drawWords();

        const named = screen
            .getAllByRole('button')
            .map((control) => control.getAttribute('aria-label'))
            .filter((name) => name !== null);

        expect(named).toEqual([
            'Bold',
            'Italic',
            'Underline',
            'Strikethrough',
            'Bulleted list',
            'Numbered list',
            'Quote',
            'Insert a link',
            'Clear formatting',
        ]);
    });

    it('reports what the message became once a control has acted on it', () => {
        const { written } = drawWords();

        words().textContent = 'Here it is.';
        press('Bold');

        expect(written).toHaveBeenCalledWith([{ text: 'Here it is.' }]);
    });

    it('asks where a link goes before it makes one', () => {
        drawWords();

        press('Insert a link');

        expect(screen.getByLabelText('Link address')).toBeDefined();
        expect(acts()).toEqual([]);
    });

    it('makes the link once the address is written', () => {
        drawWords();

        press('Insert a link');
        fireEvent.change(screen.getByLabelText('Link address'), {
            target: { value: 'https://example.invalid/invoice' },
        });
        fireEvent.keyDown(screen.getByLabelText('Link address'), { key: 'Enter' });

        expect(acts()).toEqual([{ command: 'createLink', argument: 'https://example.invalid/invoice' }]);
        expect(screen.queryByLabelText('Link address')).toBeNull();
    });

    it('makes no link to somewhere this client would not follow', () => {
        drawWords();

        press('Insert a link');
        fireEvent.change(screen.getByLabelText('Link address'), { target: { value: 'javascript:alert(1)' } });
        fireEvent.keyDown(screen.getByLabelText('Link address'), { key: 'Enter' });

        expect(acts()).toEqual([]);
        expect(screen.queryByLabelText('Link address')).toBeNull();
    });

    it('acts on what is selected now, not on what was selected when a link was given up', () => {
        drawWords([
            { element: 'div', address: null, holds: [{ text: 'alpha' }] },
            { element: 'div', address: null, holds: [{ text: 'beta' }] },
        ]);

        select('alpha');
        press('Insert a link');
        fireEvent.keyDown(screen.getByLabelText('Link address'), { key: 'Escape' });

        select('beta');
        press('Bold');

        expect(selectedWhenCommanded).toEqual(['beta']);
    });

    it('gives the address up where somebody presses escape', () => {
        drawWords();

        press('Insert a link');
        fireEvent.keyDown(screen.getByLabelText('Link address'), { key: 'Escape' });

        expect(screen.queryByLabelText('Link address')).toBeNull();
        expect(acts()).toEqual([]);
    });
});

describe('WrittenMessage, the two pointer compositions', () => {
    it('draws the bar outright where a mouse drives the screen, with the hint beside it', () => {
        drawWords();

        expect(screen.getByRole('group', { name: 'Text formatting' })).toBeDefined();
        expect(screen.getByText('Formatting acts on whatever you have selected.')).toBeDefined();
        expect(screen.queryByRole('button', { name: 'Text formatting' })).toBeNull();
    });

    it('draws the toggle in place of the bar where a finger drives the screen', () => {
        pointerIsCoarse = true;

        drawWords();

        expect(screen.getByRole('button', { name: 'Text formatting' })).toBeDefined();
        expect(screen.queryByRole('group', { name: 'Text formatting' })).toBeNull();
        expect(screen.queryByText('Formatting acts on whatever you have selected.')).toBeNull();
    });

    it('draws the bar under a finger once the toggle asks for it', () => {
        pointerIsCoarse = true;

        drawWords();
        press('Text formatting');

        expect(screen.getByRole('group', { name: 'Text formatting' })).toBeDefined();
        expect(screen.getByRole('button', { name: 'Hide formatting' })).toBeDefined();
    });
});
