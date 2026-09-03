// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { fireEvent, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { LocalizationProvider } from '../localization/Localization';
import { storeLocale } from '../localization/locale';
import { tabFor, type OpenTab } from './openTabs';
import { TabStrip } from './TabStrip';

const quarterly = tabFor('thread', 'message-1', 'The quarterly figures');
const invoice = tabFor('thread', 'message-2', 'The invoice');
const rota = tabFor('thread', 'message-3', 'The rota');
const draft = tabFor('draft', 'draft-1', 'A message being written');

const declaredShowModal = Object.getOwnPropertyDescriptor(HTMLDialogElement.prototype, 'showModal');
const declaredClose = Object.getOwnPropertyDescriptor(HTMLDialogElement.prototype, 'close');

// The confirmation is the platform's own modal dialog, which jsdom implements none of. What is stood in for here is
// only what the assertions below read, and all of it is what the platform's own close algorithm does: the `open`
// attribute it sets and clears, the return value an answer leaves behind, and the `close` event that carries the
// answer back to the page. Focus restoration is the one part not modelled, because jsdom has no top layer to restore
// from — what a test can hold is that both answers leave through `close()`, which is what the platform's own
// restoration hangs off.
function withModalDialogs(): void {
    Object.defineProperty(HTMLDialogElement.prototype, 'showModal', {
        configurable: true,
        value(this: HTMLDialogElement) {
            this.setAttribute('open', '');
        },
    });
    Object.defineProperty(HTMLDialogElement.prototype, 'close', {
        configurable: true,
        value(this: HTMLDialogElement, returnValue?: string) {
            this.removeAttribute('open');

            if (returnValue !== undefined) {
                this.returnValue = returnValue;
            }

            this.dispatchEvent(new Event('close'));
        },
    });
}

interface Acts {
    readonly activate: (key: string) => void;
    readonly close: (key: string) => void;
    readonly closeEverything: () => void;
}

function renderStrip(tabs: readonly OpenTab[], active: string | null): Acts {
    withModalDialogs();

    const acts: Acts = { activate: vi.fn(), close: vi.fn(), closeEverything: vi.fn() };

    render(
        <LocalizationProvider>
            <TabStrip
                tabs={tabs}
                active={active}
                onActivate={acts.activate}
                onClose={acts.close}
                onCloseEverything={acts.closeEverything}
            />
        </LocalizationProvider>,
    );

    return acts;
}

function tab(name: string): HTMLElement {
    return screen.getByRole('tab', { name });
}

afterEach(() => {
    if (declaredShowModal === undefined) {
        Reflect.deleteProperty(HTMLDialogElement.prototype, 'showModal');
    } else {
        Object.defineProperty(HTMLDialogElement.prototype, 'showModal', declaredShowModal);
    }

    if (declaredClose === undefined) {
        Reflect.deleteProperty(HTMLDialogElement.prototype, 'close');
    } else {
        Object.defineProperty(HTMLDialogElement.prototype, 'close', declaredClose);
    }

    window.localStorage.clear();
    window.sessionStorage.clear();
    vi.restoreAllMocks();
});

describe('TabStrip', () => {
    it('names every open tab and reports the one being read as the selected one', () => {
        renderStrip([quarterly, invoice], invoice.key);

        expect(screen.getByRole('tablist', { name: 'Open tabs' })).toBeDefined();
        expect(tab('The quarterly figures').getAttribute('aria-selected')).toBe('false');
        expect(tab('The invoice').getAttribute('aria-selected')).toBe('true');
    });

    it('names a message that arrived without a subject rather than drawing an unlabelled tab', () => {
        renderStrip([tabFor('thread', 'message-4', null)], null);

        expect(tab('No subject')).toBeDefined();
    });

    it('draws a tab of each kind, so what a tab holds is legible before it is read', () => {
        renderStrip(
            [
                quarterly,
                tabFor('attachment', 'file-1', 'The contract'),
                tabFor('fullHtml', 'message-1', 'As sent'),
                draft,
            ],
            quarterly.key,
        );

        expect(screen.getAllByRole('tab')).toHaveLength(4);
    });

    it('brings a tab forward when it is pressed', () => {
        const acts = renderStrip([quarterly, invoice], invoice.key);

        fireEvent.click(tab('The quarterly figures'));

        expect(acts.activate).toHaveBeenCalledWith(quarterly.key);
    });

    it('holds one stop in the tab order, on the tab being read', () => {
        renderStrip([quarterly, invoice], invoice.key);

        expect(tab('The quarterly figures').getAttribute('tabindex')).toBe('-1');
        expect(tab('The invoice').getAttribute('tabindex')).toBe('0');
    });

    it('offers the first tab to the keyboard where nothing is being read yet', () => {
        renderStrip([quarterly, invoice], null);

        expect(tab('The quarterly figures').getAttribute('tabindex')).toBe('0');
    });

    it('moves focus along the strip without reading anything, so arrowing past a tab starts no read', () => {
        const acts = renderStrip([quarterly, invoice, rota], quarterly.key);

        fireEvent.keyDown(tab('The quarterly figures'), { key: 'ArrowRight' });
        fireEvent.keyDown(tab('The invoice'), { key: 'ArrowRight' });

        expect(document.activeElement).toBe(tab('The rota'));
        expect(acts.activate).not.toHaveBeenCalled();
    });

    it('stops at each end rather than wrapping, and reaches either end in one press', () => {
        renderStrip([quarterly, invoice, rota], quarterly.key);

        fireEvent.keyDown(tab('The quarterly figures'), { key: 'ArrowLeft' });
        expect(document.activeElement).toBe(tab('The quarterly figures'));

        fireEvent.keyDown(tab('The quarterly figures'), { key: 'End' });
        expect(document.activeElement).toBe(tab('The rota'));

        fireEvent.keyDown(tab('The rota'), { key: 'ArrowRight' });
        expect(document.activeElement).toBe(tab('The rota'));

        fireEvent.keyDown(tab('The rota'), { key: 'Home' });
        expect(document.activeElement).toBe(tab('The quarterly figures'));
    });

    it('closes the tab the keyboard is on when Delete is pressed', () => {
        const acts = renderStrip([quarterly, invoice], invoice.key);

        fireEvent.keyDown(tab('The invoice'), { key: 'Delete' });

        expect(acts.close).toHaveBeenCalledWith(invoice.key);
    });

    it('leaves a key it does not answer to the platform', () => {
        const acts = renderStrip([quarterly, invoice], invoice.key);

        fireEvent.keyDown(tab('The invoice'), { key: 'a' });

        expect(acts.close).not.toHaveBeenCalled();
        expect(acts.activate).not.toHaveBeenCalled();
    });

    it('closes one tab from a control that names which tab it closes', () => {
        const acts = renderStrip([quarterly, invoice], invoice.key);

        fireEvent.click(screen.getByRole('button', { name: 'Close The quarterly figures' }));

        expect(acts.close).toHaveBeenCalledWith(quarterly.key);
    });

    it('leaves the keyboard on the tab that takes over when the one being read is closed', () => {
        renderStrip([quarterly, invoice], invoice.key);

        fireEvent.keyDown(tab('The invoice'), { key: 'Delete' });

        expect(document.activeElement).toBe(tab('The quarterly figures'));
    });

    it('asks before closing everything, and closes nothing when the question is refused', () => {
        const acts = renderStrip([quarterly, invoice], invoice.key);

        fireEvent.click(screen.getByRole('button', { name: 'Close everything that is open' }));

        expect(screen.getByRole('heading', { name: 'Close every tab?' })).toBeDefined();
        expect(screen.getByText('Open tabs: 2.')).toBeDefined();

        fireEvent.click(screen.getByRole('button', { name: 'Cancel' }));

        expect(acts.closeEverything).not.toHaveBeenCalled();
        expect(screen.queryByRole('heading', { name: 'Close every tab?' })).toBeNull();
    });

    it('closes everything once the question is answered', () => {
        const acts = renderStrip([quarterly, invoice], invoice.key);

        fireEvent.click(screen.getByRole('button', { name: 'Close everything that is open' }));
        fireEvent.click(screen.getByRole('button', { name: 'Close them all' }));

        expect(acts.closeEverything).toHaveBeenCalledTimes(1);
        expect(screen.queryByRole('heading', { name: 'Close every tab?' })).toBeNull();
    });

    it('counts one open tab in the form one takes rather than in the plural', () => {
        renderStrip([quarterly], quarterly.key);

        fireEvent.click(screen.getByRole('button', { name: 'Close everything that is open' }));

        expect(screen.getByText('Open tab: 1.')).toBeDefined();
    });

    // Polish is where a count actually has to be read: it takes one form at one, a second at two through four, and a
    // third above that, and a screen that interpolated a number into one sentence would be wrong at two of the three.
    it.each([
        [[quarterly], 'Otwarta zakładka: 1.'],
        [[quarterly, invoice], 'Otwarte zakładki: 2.'],
        [[quarterly, invoice, rota, draft, tabFor('thread', 'message-5', 'A fifth')], 'Otwartych zakładek: 5.'],
    ])('counts what is open in the form Polish takes at that number', (open, said) => {
        storeLocale('pl');

        renderStrip(open, null);

        fireEvent.click(screen.getByRole('button', { name: 'Zamknij wszystko, co jest otwarte' }));

        expect(screen.getByText(new RegExp(said.replaceAll('.', '\\.')))).toBeDefined();
    });

    it('says the words in an unsent draft go with it, and says so only where one is open', () => {
        renderStrip([quarterly, draft], draft.key);

        fireEvent.click(screen.getByRole('button', { name: 'Close everything that is open' }));

        expect(screen.getByText(/An unsent draft will be discarded\./)).toBeDefined();
    });

    it('says nothing about a draft where none is open', () => {
        renderStrip([quarterly, invoice], invoice.key);

        fireEvent.click(screen.getByRole('button', { name: 'Close everything that is open' }));

        expect(screen.queryByText(/An unsent draft will be discarded\./)).toBeNull();
    });
});
