// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { LocalizationProvider } from '../../localization/Localization';
import { LinkOpenerContext, type OpenLink } from '../../shellOperations/linkOpener';
import { AnswerText } from './AnswerText';

function drawing(text: string, openLink: OpenLink = () => Promise.resolve()) {
    return render(
        <LocalizationProvider>
            <LinkOpenerContext value={openLink}>
                <AnswerText text={text} />
            </LinkOpenerContext>
        </LocalizationProvider>,
    );
}

describe('AnswerText', () => {
    it('draws a sentence with no Markdown in it as the sentence', () => {
        drawing('The rate was agreed in April 2021.');

        expect(screen.getByText('The rate was agreed in April 2021.').tagName).toBe('P');
    });

    it('draws a list the model wrote as a list rather than as dashes', () => {
        drawing('Two messages ask for it:\n\n- the invoice from Anna\n- the reminder from Piotr');

        expect(screen.getAllByRole('listitem').map((item) => item.textContent)).toEqual([
            'the invoice from Anna',
            'the reminder from Piotr',
        ]);
    });

    it('draws emphasis as emphasis rather than as asterisks', () => {
        drawing('The meeting moved to **15 September 2026**, ~~14 September~~ no longer.');

        expect(screen.getByText('15 September 2026').tagName).toBe('STRONG');
        expect(screen.getByText('14 September').tagName).toBe('DEL');
    });

    it('draws raw HTML as the text it is rather than as markup', () => {
        const { container } = drawing("Say <b onclick='x()'>yes</b> to it.");

        expect(container.querySelector('b')).toBeNull();
        expect(container.textContent).toContain("<b onclick='x()'>yes</b>");
    });

    it('draws no image, so nothing the answer names is fetched', () => {
        const { container } = drawing('Opened ![pixel](https://tracker.example/p.gif) here.');

        expect(container.querySelector('img')).toBeNull();
    });

    it('draws a link the way a link in mail is drawn: saying where it goes, and warned about', () => {
        drawing('Read [the agreement](https://phish.example/login) first.');

        expect(screen.getByRole('link', { name: 'the agreement' })).toBeDefined();
        expect(screen.getByText('goes to phish.example')).toBeDefined();
        expect(
            screen.getByText('This link is worth checking before you follow it. It goes to phish.example.', {
                exact: false,
            }),
        ).toBeDefined();
    });

    it('opens a link out of the application rather than navigating the answer to it', () => {
        const opened: string[] = [];
        drawing('Read [the agreement](https://example.invalid/agreement).', (target) => {
            opened.push(target);
            return Promise.resolve();
        });

        fireEvent.click(screen.getByRole('link', { name: 'the agreement' }));

        expect(opened).toEqual(['https://example.invalid/agreement']);
    });

    it('draws a link naming no web or mail address as its words alone', () => {
        const { container } = drawing('Read [the agreement](file:///etc/passwd) first.');

        expect(screen.queryByRole('link')).toBeNull();
        expect(container.querySelector('[href]')).toBeNull();
        expect(screen.getByText(/the agreement/u)).toBeDefined();
    });

    it('draws a heading without claiming a place in the screen’s heading order', () => {
        drawing('## Summary\n\nNothing is overdue.');

        expect(screen.queryByRole('heading')).toBeNull();
        expect(screen.getByText('Summary')).toBeDefined();
    });

    it('draws a table the model wrote as a table a keyboard can scroll', () => {
        drawing('| Sender | Date |\n| --- | --- |\n| Anna | 15 September 2026 |');

        expect(screen.getByRole('group', { name: /table/iu }).getAttribute('tabindex')).toBe('0');
        expect(screen.getByRole('cell', { name: 'Anna' })).toBeDefined();
    });
});
