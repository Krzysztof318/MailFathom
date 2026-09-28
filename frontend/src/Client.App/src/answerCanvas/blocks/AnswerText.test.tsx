// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { AnswerText } from './AnswerText';

describe('AnswerText', () => {
    it('draws a sentence with no Markdown in it as the sentence', () => {
        render(<AnswerText text="The rate was agreed in April 2021." />);

        expect(screen.getByText('The rate was agreed in April 2021.').tagName).toBe('P');
    });

    it('draws a list the model wrote as a list rather than as dashes', () => {
        render(<AnswerText text={'Two messages ask for it:\n\n- the invoice from Anna\n- the reminder from Piotr'} />);

        expect(screen.getAllByRole('listitem').map((item) => item.textContent)).toEqual([
            'the invoice from Anna',
            'the reminder from Piotr',
        ]);
    });

    it('draws emphasis as emphasis rather than as asterisks', () => {
        render(<AnswerText text="The meeting moved to **15 September 2026**." />);

        expect(screen.getByText('15 September 2026').tagName).toBe('STRONG');
    });

    it('draws raw HTML as the text it is rather than as markup', () => {
        const { container } = render(<AnswerText text="Say <b onclick='x()'>yes</b> to it." />);

        expect(container.querySelector('b')).toBeNull();
        expect(container.textContent).toContain("<b onclick='x()'>yes</b>");
    });

    it('draws no image, so nothing the answer names is fetched', () => {
        const { container } = render(<AnswerText text="Opened ![pixel](https://tracker.example/p.gif) here." />);

        expect(container.querySelector('img')).toBeNull();
    });

    it('draws a link as its words alone, with nowhere to follow it to', () => {
        const { container } = render(<AnswerText text="Read [the agreement](https://phish.example/login) first." />);

        expect(screen.getByText(/the agreement/u)).toBeDefined();
        expect(screen.queryByRole('link')).toBeNull();
        expect(container.querySelector('[href]')).toBeNull();
        expect(container.textContent).not.toContain('phish.example');
    });

    it('draws a heading as a bold line, leaving the screen’s heading order alone', () => {
        render(<AnswerText text={'# Summary\n\nNothing is overdue.'} />);

        expect(screen.queryByRole('heading')).toBeNull();
        expect(screen.getByText('Summary').className).toContain('font-semibold');
    });

    it('draws a table the model wrote as a table', () => {
        render(<AnswerText text={'| Sender | Date |\n| --- | --- |\n| Anna | 15 September 2026 |'} />);

        expect(screen.getByRole('table')).toBeDefined();
        expect(screen.getByRole('cell', { name: 'Anna' })).toBeDefined();
    });
});
