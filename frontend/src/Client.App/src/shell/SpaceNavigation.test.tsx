// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it } from 'vitest';
import { LocalizationProvider } from '../localization/Localization';
import { spaces, type Space } from '../routing/spaces';
import { SpaceNavigation } from './SpaceNavigation';

const handedTheAccount = 'The account control this navigation was handed.';
const handedTheBell = 'The bell this navigation was handed.';

// The one thing about this component jsdom cannot answer for, because it decides which of the two shapes is in the
// document rather than how one of them is laid out. It answers narrow to every query unless a test says otherwise, so
// a test about the rail states the width it is about instead of inheriting one.
function atWorkspaceWidth(wide: boolean): void {
    Object.defineProperty(window, 'matchMedia', {
        configurable: true,
        value: (query: string) => ({
            media: query,
            matches: wide,
            addEventListener: () => undefined,
            removeEventListener: () => undefined,
        }),
    });
}

function renderNavigation(offered: readonly Space[] = spaces, current: Space | null = 'mail'): void {
    render(
        <LocalizationProvider>
            <SpaceNavigation
                offered={offered}
                current={current}
                account={<button>{handedTheAccount}</button>}
                notifications={<button>{handedTheBell}</button>}
                onPointerDown={() => undefined}
                onClickCapture={() => undefined}
            />
        </LocalizationProvider>,
    );
}

/** Every link in the document, the overflow's included: jsdom draws a closed popover hidden, exactly as a browser does. */
function everyLink(): [string | null, string | null][] {
    return screen
        .getAllByRole('link', { hidden: true })
        .map((space) => [space.textContent, space.getAttribute('href')]);
}

describe('SpaceNavigation', () => {
    afterEach(() => {
        atWorkspaceWidth(false);
    });

    it('offers every space as a link, each at an address of its own', () => {
        atWorkspaceWidth(true);
        renderNavigation();

        expect(everyLink()).toEqual([
            ['Discover', '#/discover'],
            ['Mail', '#/mail'],
            ['Cases', '#/cases'],
            ['Agent', '#/agent'],
            ['Tasks', '#/tasks'],
            ['Calendar', '#/calendar'],
            ['People', '#/people'],
        ]);
    });

    it('says in a placeholder link’s own name that there is nothing behind it yet', () => {
        atWorkspaceWidth(true);
        renderNavigation();

        expect(screen.getByRole('link', { name: 'Tasks — not built yet' })).toBeDefined();
    });

    it('leaves a space that is built to be named by what it is, with nothing appended', () => {
        renderNavigation();

        expect(screen.getByRole('link', { name: 'Mail' })).toBeDefined();
    });

    it('marks the space being shown as the current one, and no other', () => {
        atWorkspaceWidth(true);
        renderNavigation();

        expect(screen.getByRole('link', { current: 'page' }).textContent).toBe('Mail');
    });

    it('is named, so it is one landmark a reader can move to rather than three loose links', () => {
        renderNavigation();

        expect(screen.getByRole('navigation', { name: 'Spaces' })).toBeDefined();
    });

    it('offers only what it was given, so a space this credential may not open is absent from the rail', () => {
        atWorkspaceWidth(true);
        renderNavigation(['mail', 'cases']);

        expect(everyLink().map(([name]) => name)).toEqual(['Mail', 'Cases']);
    });

    it('places the account control after the spaces, so it is last in the rail', () => {
        atWorkspaceWidth(true);
        renderNavigation();

        const navigation = screen.getByRole('navigation', { name: 'Spaces' });
        const account = screen.getByRole('button', { name: handedTheAccount });
        const lastLink = screen.getAllByRole('link').at(-1);

        expect(navigation.contains(account)).toBe(true);
        expect(lastLink?.compareDocumentPosition(account)).toBe(Node.DOCUMENT_POSITION_FOLLOWING);
    });

    it('stands the bell beside the account, after every space and before the account itself', () => {
        atWorkspaceWidth(true);
        renderNavigation();

        const bell = screen.getByRole('button', { name: handedTheBell });
        const account = screen.getByRole('button', { name: handedTheAccount });
        const lastLink = screen.getAllByRole('link').at(-1);

        expect(lastLink?.compareDocumentPosition(bell)).toBe(Node.DOCUMENT_POSITION_FOLLOWING);
        expect(bell.compareDocumentPosition(account)).toBe(Node.DOCUMENT_POSITION_FOLLOWING);
    });

    it('still places the account control while the deployment has not said which spaces there are', () => {
        atWorkspaceWidth(true);
        renderNavigation([], null);

        expect(screen.queryAllByRole('link')).toEqual([]);
        expect(screen.getByRole('button', { name: handedTheAccount })).toBeDefined();
    });

    // The bar has five places and the rail has as many as the session offers, which is the one difference between the
    // two shapes that is not a matter of laying the same nodes out differently.
    it('spends the bar’s five places on three spaces, the bell, and the overflow', () => {
        renderNavigation();

        const bar = screen.getByRole('navigation', { name: 'Spaces' });

        // The mark is drawn in the rail alone, and the overflow's own sheet is a child of the navigation without
        // standing in it — the platform draws a closed popover nowhere and an open one in the top layer.
        const places = Array.from(bar.children).filter(
            (place) => place.tagName !== 'IMG' && !place.hasAttribute('popover'),
        );

        expect(places.map((place) => place.textContent)).toEqual(['Discover', 'Mail', 'Agent', handedTheBell, 'More']);
    });

    it('hands the bar’s overflow every space it had no place for, and the account after them', () => {
        renderNavigation();

        const sheet = document.getElementById('space-overflow');
        const behindIt = screen.getAllByRole('link', { hidden: true }).filter((link) => sheet?.contains(link) === true);

        expect(behindIt.map((link) => link.textContent)).toEqual(['Tasks', 'Cases', 'Calendar', 'People']);
        expect(sheet?.contains(screen.getByRole('button', { name: handedTheAccount, hidden: true }))).toBe(true);
    });

    it('keeps the overflow even where every offered space fits the bar, the account standing behind it', () => {
        renderNavigation(['mail', 'agent'], 'mail');

        expect(everyLink().map(([name]) => name)).toEqual(['Mail', 'Agent']);
        expect(screen.getByRole('button', { name: 'More' })).toBeDefined();
    });
});
