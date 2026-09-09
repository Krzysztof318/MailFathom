// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { LocalizationProvider } from '../localization/Localization';
import { LinkOpenerContext, type OpenLink } from '../shellOperations/linkOpener';
import { EmbeddedMessageMarkup, MessageMarkupFrame } from './MessageMarkupFrame';

// The `sandbox` attribute is the one thing asserted here that is not what a person sees, and it is asserted anyway:
// it is the whole of what stops a stranger's markup reaching the page, this is the only file permitted to write the
// element it sits on, and an edit that widened it would be invisible in every other test in the suite. What jsdom
// cannot answer — whether a browser honours it — is the browser suite's.
//
// The other half of this file is what a followed link does, and it is asserted as a report crossing a trust boundary
// rather than as a click: what the framed document sends is a string a stranger's markup produced, so the tests that
// matter are the ones where it is refused.

function opening(): { opened: string[]; openLink: OpenLink } {
    const opened: string[] = [];

    return { opened, openLink: (target) => (opened.push(target), Promise.resolve()) };
}

function frame(): HTMLIFrameElement {
    const drawn = screen.getByTitle("The sender's own markup, drawn in isolation");

    if (!(drawn instanceof HTMLIFrameElement)) {
        throw new Error('The markup is drawn in something other than a frame.');
    }

    return drawn;
}

function reporting(reported: unknown, source: Window | null = frame().contentWindow): void {
    fireEvent(window, new MessageEvent('message', { data: reported, source }));
}

describe('MessageMarkupFrame', () => {
    function drawing(markup: string, openLink: OpenLink = () => Promise.resolve()): void {
        render(
            <LocalizationProvider>
                <LinkOpenerContext value={openLink}>
                    <MessageMarkupFrame markup={markup} />
                </LinkOpenerContext>
            </LocalizationProvider>,
        );
    }

    it('draws the markup it was handed in a frame of its own', () => {
        drawing('<p>As the sender wrote it.</p>');

        expect(frame().getAttribute('srcdoc')).toContain('<p>As the sender wrote it.</p>');
    });

    it('permits the framed document a script and nothing else, so it reaches neither the page nor an origin', () => {
        drawing('<p>As the sender wrote it.</p>');

        const permitted = frame().getAttribute('sandbox');

        expect(permitted).toBe('allow-scripts');
        expect(permitted).not.toContain('allow-same-origin');
        expect(permitted).not.toContain('allow-popups');
        expect(permitted).not.toContain('allow-top-navigation');
    });

    it('puts the client’s own link script ahead of the markup, inside the document’s own head', () => {
        drawing('<html><head></head><body><p>As the sender wrote it.</p></body></html>');

        const framed = frame().getAttribute('srcdoc') ?? '';

        expect(framed).toContain('addEventListener("click"');
        expect(framed.indexOf('addEventListener("click"')).toBeLessThan(framed.indexOf('As the sender wrote it.'));
    });

    it('leaves the dialog scrolling inside itself, so it carries no script that would hide its overflow', () => {
        drawing('<p>As the sender wrote it.</p>');

        expect(frame().getAttribute('srcdoc')).not.toContain('ResizeObserver');
    });

    it('opens a link the framed document reports, out of the application rather than in it', () => {
        const { opened, openLink } = opening();
        drawing('<p>As the sender wrote it.</p>', openLink);

        reporting({ link: 'https://example.test/offer' });

        expect(opened).toEqual(['https://example.test/offer']);
    });

    it('says so where the head could not open the link that was followed', async () => {
        drawing('<p>As the sender wrote it.</p>', () => Promise.reject(new Error('no handler for it')));

        reporting({ link: 'https://example.test/offer' });

        // A press that quietly did nothing is the defect this surface exists to remove wearing another face, and the
        // desktop head's opener is the one that genuinely rejects.
        expect(await screen.findByText('This link could not be opened.')).toBeDefined();
    });

    it('draws nothing at all where the deployment served no markup for this message', () => {
        drawing('');

        expect(screen.queryByTitle("The sender's own markup, drawn in isolation")).toBeNull();
    });
});

// The second surface, which is the same frame drawn inline. What its own script buys is the height, and what bounds it
// is that the frame reaches nothing else: the flag is asserted exactly, and a report is taken only from the window of
// the frame this component created.
describe('EmbeddedMessageMarkup', () => {
    function embedding(markup: string, openLink: OpenLink = () => Promise.resolve()): void {
        render(
            <LocalizationProvider>
                <LinkOpenerContext value={openLink}>
                    <EmbeddedMessageMarkup markup={markup} />
                </LinkOpenerContext>
            </LocalizationProvider>,
        );
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

    it('opens a link the framed document reports', () => {
        const { opened, openLink } = opening();
        embedding('<p>As the sender wrote it.</p>', openLink);

        reporting({ link: 'mailto:someone@example.test' });

        expect(opened).toEqual(['mailto:someone@example.test']);
    });

    it('opens nothing a report from any window but the frame’s own names', () => {
        const { opened, openLink } = opening();
        embedding('<p>As the sender wrote it.</p>', openLink);

        reporting({ link: 'https://example.test/offer' }, null);

        expect(opened).toEqual([]);
    });

    it('opens nothing carrying a scheme a reader may not be handed', () => {
        const { opened, openLink } = opening();
        embedding('<p>As the sender wrote it.</p>', openLink);

        reporting({ link: 'javascript:alert(1)' });
        reporting({ link: 'data:text/html,<script>alert(1)</script>' });
        reporting({ link: 'file:///etc/passwd' });
        reporting({ link: '/relative/inside/the/frame' });
        reporting({ link: '' });
        reporting({ link: 42 });
        reporting({ link: `https://example.test/${'x'.repeat(5000)}` });
        reporting('https://example.test/offer');

        expect(opened).toEqual([]);
    });

    it('draws no failure where the opener refuses, a click being a gesture no head blocks', async () => {
        const refusing = vi.fn<OpenLink>(() => Promise.reject(new Error('refused')));
        embedding('<p>As the sender wrote it.</p>', refusing);

        reporting({ link: 'https://example.test/offer' });
        await vi.waitFor(() => {
            expect(refusing).toHaveBeenCalledWith('https://example.test/offer');
        });

        expect(screen.getByText('Fitting the height to the content…')).toBeDefined();
    });

    it('draws nothing at all where the deployment served no markup for this message', () => {
        embedding('');

        expect(screen.queryByTitle("The sender's own markup, drawn in isolation")).toBeNull();
    });
});
