// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { LocalizationProvider } from '../localization/Localization';
import { EmbeddedMessageMarkup, MessageMarkupFrame } from './MessageMarkupFrame';

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

// The second surface, which carries the one `sandbox` value in this client that permits anything at all. What that
// buys is the height, and what bounds it is that the frame reaches nothing else: the flag is asserted exactly, and a
// report is taken only from the window of the frame this component created.
describe('EmbeddedMessageMarkup', () => {
    function embedding(markup: string): void {
        render(
            <LocalizationProvider>
                <EmbeddedMessageMarkup markup={markup} />
            </LocalizationProvider>,
        );
    }

    function reporting(height: unknown, source: Window | null = frame().contentWindow): void {
        fireEvent(window, new MessageEvent('message', { data: height, source }));
    }

    it('permits the framed document a script and nothing else, so it reaches neither the page nor an origin', () => {
        embedding('<p>As the sender wrote it.</p>');

        expect(frame().getAttribute('sandbox')).toBe('allow-scripts');
    });

    it('puts the client’s own measuring script ahead of the markup, inside the document’s own head', () => {
        embedding('<html><head></head><body><p>As the sender wrote it.</p></body></html>');

        const framed = frame().getAttribute('srcdoc') ?? '';

        expect(framed).toContain('postMessage');
        expect(framed.indexOf('postMessage')).toBeLessThan(framed.indexOf('As the sender wrote it.'));
    });

    it('says it is still fitting the frame before anything inside it has reported', () => {
        embedding('<p>As the sender wrote it.</p>');

        expect(screen.getByText('Fitting the height to the content…')).toBeDefined();
        expect(frame().style.height).toBe('320px');
    });

    it('draws the frame at the height the document inside it reported', () => {
        embedding('<p>As the sender wrote it.</p>');

        reporting({ height: 900 });

        expect(frame().style.height).toBe('902px');
        expect(screen.getByText("The sender's HTML in isolation — scripts and remote resources blocked")).toBeDefined();
    });

    it('holds the frame within bounds no document may push it past', () => {
        embedding('<p>As the sender wrote it.</p>');

        reporting({ height: 4_000_000 });

        expect(frame().style.height).toBe('40000px');
    });

    it('ignores a report from any window but the frame’s own, an opaque origin being no evidence of anything', () => {
        embedding('<p>As the sender wrote it.</p>');

        reporting({ height: 900 }, null);

        expect(frame().style.height).toBe('320px');
    });

    it('ignores a report carrying no height this surface can act on', () => {
        embedding('<p>As the sender wrote it.</p>');

        reporting({ height: 'as tall as it likes' });
        reporting('900');

        expect(frame().style.height).toBe('320px');
    });

    it('draws nothing at all where the deployment served no markup for this message', () => {
        embedding('');

        expect(screen.queryByTitle("The sender's own markup, drawn in isolation")).toBeNull();
    });
});
