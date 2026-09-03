// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { LocalizationProvider } from '../localization/Localization';
import { MessageMarkupFrame } from './MessageMarkupFrame';

// The `sandbox` attribute is the one thing asserted here that is not what a person sees, and it is asserted anyway:
// it is the whole of what stops a stranger's markup running, this is the only file permitted to write the element it
// sits on, and an edit that widened it would be invisible in every other test in the suite. What jsdom cannot answer —
// whether a browser honours it — is the browser suite's.

function drawing(markup: string): void {
    render(
        <LocalizationProvider>
            <MessageMarkupFrame markup={markup} />
        </LocalizationProvider>,
    );
}

function frame(): HTMLIFrameElement {
    const drawn = screen.getByTitle("The sender's own markup, drawn in isolation");

    if (!(drawn instanceof HTMLIFrameElement)) {
        throw new Error('The markup is drawn in something other than a frame.');
    }

    return drawn;
}

describe('MessageMarkupFrame', () => {
    it('draws the markup it was handed in a frame of its own', () => {
        drawing('<p>As the sender wrote it.</p>');

        expect(frame().getAttribute('srcdoc')).toBe('<p>As the sender wrote it.</p>');
    });

    it('permits the framed document neither script nor an origin of its own', () => {
        drawing('<p>As the sender wrote it.</p>');

        const permitted = frame().getAttribute('sandbox');

        expect(permitted).toBe('');
        expect(permitted).not.toContain('allow-scripts');
        expect(permitted).not.toContain('allow-same-origin');
    });

    it('draws nothing at all where the deployment served no markup for this message', () => {
        drawing('');

        expect(screen.queryByTitle("The sender's own markup, drawn in isolation")).toBeNull();
    });
});
