// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { LocalizationProvider } from '../localization/Localization';
import { spaces, type Space } from '../routing/spaces';
import { SpaceNavigation } from './SpaceNavigation';

const handedTheAccount = 'The account control this navigation was handed.';

function renderNavigation(offered: readonly Space[] = spaces, current: Space | null = 'mail'): void {
    render(
        <LocalizationProvider>
            <SpaceNavigation offered={offered} current={current} account={<button>{handedTheAccount}</button>} />
        </LocalizationProvider>,
    );
}

describe('SpaceNavigation', () => {
    it('offers every space as a link, each at an address of its own', () => {
        renderNavigation();

        const spaces = screen.getAllByRole('link');
        expect(spaces.map((space) => [space.textContent, space.getAttribute('href')])).toEqual([
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
        renderNavigation();

        expect(screen.getByRole('link', { name: 'Tasks — not built yet' })).toBeDefined();
    });

    it('leaves a space that is built to be named by what it is, with nothing appended', () => {
        renderNavigation();

        expect(screen.getByRole('link', { name: 'Mail' })).toBeDefined();
    });

    it('marks the space being shown as the current one, and no other', () => {
        renderNavigation();

        expect(screen.getByRole('link', { current: 'page' }).textContent).toBe('Mail');
    });

    it('is named, so it is one landmark a reader can move to rather than three loose links', () => {
        renderNavigation();

        expect(screen.getByRole('navigation', { name: 'Spaces' })).toBeDefined();
    });

    it('offers only what it was given, so a space this credential may not open is absent from the rail', () => {
        renderNavigation(['mail', 'cases']);

        expect(screen.getAllByRole('link').map((space) => space.textContent)).toEqual(['Mail', 'Cases']);
    });

    it('places the account control after the spaces, so it is last in the rail and in the bar alike', () => {
        renderNavigation();

        const navigation = screen.getByRole('navigation', { name: 'Spaces' });
        const account = screen.getByRole('button', { name: handedTheAccount });
        const lastLink = screen.getAllByRole('link').at(-1);

        expect(navigation.contains(account)).toBe(true);
        expect(lastLink?.compareDocumentPosition(account)).toBe(Node.DOCUMENT_POSITION_FOLLOWING);
    });

    it('still places the account control while the deployment has not said which spaces there are', () => {
        renderNavigation([], null);

        expect(screen.queryAllByRole('link')).toEqual([]);
        expect(screen.getByRole('button', { name: handedTheAccount })).toBeDefined();
    });
});
