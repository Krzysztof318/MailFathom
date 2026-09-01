// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { LocalizationProvider } from '../localization/Localization';
import { spaces, type Space } from '../routing/spaces';
import { SpaceNavigation } from './SpaceNavigation';

function renderNavigation(offered: readonly Space[] = spaces): void {
    render(
        <LocalizationProvider>
            <SpaceNavigation offered={offered} current="mail" />
        </LocalizationProvider>,
    );
}

describe('SpaceNavigation', () => {
    it('offers the three spaces as links, each at an address of its own', () => {
        renderNavigation();

        const spaces = screen.getAllByRole('link');
        expect(spaces.map((space) => [space.textContent, space.getAttribute('href')])).toEqual([
            ['Discover', '#/discover'],
            ['Mail', '#/mail'],
            ['Cases', '#/cases'],
        ]);
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
});
