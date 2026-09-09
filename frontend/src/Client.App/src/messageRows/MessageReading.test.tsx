// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import type { MailEnrichmentAspect, MailEnrichmentMark } from '@mailfathom/client-backend';
import { LocalizationProvider } from '../localization/Localization';
import { MessageReading } from './MessageReading';

function mark(aspect: MailEnrichmentAspect, text: string): MailEnrichmentMark {
    return {
        aspect,
        text,
        reason: 'Because the message says so.',
        dueAt: null,
        source: 'Model',
        origin: 'agents/reader',
        evidence: [],
    };
}

function drawn(one: MailEnrichmentMark): HTMLElement {
    const { container } = render(
        <LocalizationProvider>
            <MessageReading mark={one} />
        </LocalizationProvider>,
    );

    return container;
}

describe('MessageReading', () => {
    it('draws the reading itself', () => {
        drawn(mark('Sense', 'An invoice for August.'));

        expect(screen.getByText('An invoice for August.')).toBeTruthy();
    });

    it('announces the reading as what it is rather than as part of the mail', () => {
        const container = drawn(mark('Commitment', 'An answer is owed by Friday.'));

        expect(container.textContent).toContain('What is committed to: An answer is owed by Friday.');
    });

    it('says once what a screen reader meets, rather than the mark and the sentence twice', () => {
        const container = drawn(mark('Significance', 'The auditor is waiting.'));

        expect(container.querySelector('.sr-only')?.textContent).toBe('Why this may matter: The auditor is waiting.');
        expect(container.querySelectorAll('[aria-hidden="true"]')).toHaveLength(2);
    });

    it('draws no control, because the row it sits on may hold none', () => {
        const container = drawn(mark('Sense', 'An invoice for August.'));

        expect(container.querySelectorAll('button, a, input, [tabindex]')).toHaveLength(0);
    });

    it('clips a reading too long for the line rather than letting it wrap the row taller', () => {
        const container = drawn(mark('Sense', 'a'.repeat(240)));

        expect(container.querySelector('.truncate')).toBeTruthy();
    });
});
