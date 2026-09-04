// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { fireEvent, render, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { LocalizationProvider } from '../localization/Localization';
import { Containment } from './Containment';

// Whether what is contained is failing this render. It is a module-level value rather than a prop because a retry
// changes no prop — what it does is mount the region again — so a region that recovers is one that stops throwing
// between two renders of the same element.
let failing = true;

// What the contained region draws when it can, and what stands beside it. Both are written as values rather than as
// words in the markup, which is what the catalogue rule refuses there — neither is a sentence anybody reads in the
// client, and neither belongs in a catalogue.
const drawn = 'Open the next message';
const beside = 'The list beside it';

function Fragile() {
    if (failing) {
        throw new TypeError('A message this pane cannot draw.');
    }

    return <button type="button">{drawn}</button>;
}

function contained(drawing?: string) {
    return (
        <LocalizationProvider>
            <p>{beside}</p>

            <Containment drawing={drawing} region="reading_pane">
                <Fragile />
            </Containment>
        </LocalizationProvider>
    );
}

function renderContained(drawing?: string) {
    return render(contained(drawing));
}

describe('Containment', () => {
    beforeEach(() => {
        failing = true;

        // React writes every contained failure to the console, which is a page of stack per test here and says
        // nothing a failing expectation would not. What is asserted is what the screen did about it.
        vi.spyOn(console, 'error').mockImplementation(() => undefined);
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    it('leaves everything around a region that failed on the screen and usable', () => {
        renderContained();

        expect(screen.getByText(beside)).toBeDefined();
        expect(screen.queryByRole('button', { name: drawn })).toBeNull();
    });

    it('says the region stopped working, in the language the client is read in', () => {
        renderContained();

        expect(screen.getByRole('alert').textContent).toContain(
            'This part of MailFathom stopped working. Everything around it is unaffected.',
        );
    });

    it('puts the reader at the start of what replaced the region', () => {
        renderContained();

        expect(document.activeElement).toBe(screen.getByRole('alert'));
    });

    it('draws the region again when it is retried, and hands the keyboard back into it', () => {
        renderContained();

        failing = false;
        fireEvent.click(screen.getByRole('button', { name: 'Try again' }));

        const region = screen.getByRole('button', { name: drawn });

        expect(screen.queryByRole('alert')).toBeNull();
        expect(document.activeElement?.contains(region)).toBe(true);
    });

    it('says a region that failed again did so, rather than repeating the first sentence', () => {
        renderContained();

        fireEvent.click(screen.getByRole('button', { name: 'Try again' }));

        expect(screen.getByRole('alert').textContent).toContain(
            'This part of MailFathom stopped working again. Everything around it is unaffected.',
        );
    });

    it('draws the next thing the region is asked for, rather than holding a failure over it', () => {
        const { rerender } = renderContained('the message that failed');

        failing = false;
        rerender(contained('the next message'));

        expect(screen.queryByRole('alert')).toBeNull();
        expect(screen.getByRole('button', { name: drawn })).toBeDefined();
    });

    it('reads a failure on the next thing as a first failure rather than as a repeat of the last one', () => {
        const { rerender } = renderContained('the message that failed');

        rerender(contained('the next message'));

        expect(screen.getByRole('alert').textContent).toContain(
            'This part of MailFathom stopped working. Everything around it is unaffected.',
        );
    });

    it('puts the reader at the start of the surface again when the next thing fails in its turn', () => {
        const { rerender } = renderContained('the message that failed');

        screen.getByRole('alert').blur();
        expect(document.activeElement).toBe(document.body);
        rerender(contained('the next message'));

        expect(document.activeElement).toBe(screen.getByRole('alert'));
    });

    it('offers the way out again after it failed a second time, rather than leaving nothing to press', () => {
        renderContained();

        fireEvent.click(screen.getByRole('button', { name: 'Try again' }));

        expect(screen.getByRole('button', { name: 'Try again' })).toBeDefined();
    });
});
