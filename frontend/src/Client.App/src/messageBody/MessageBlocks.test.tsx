// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { getDefaultNormalizer, render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import type { MailDocumentBlock, MailDocumentLink, MailInlineRun } from '@mailfathom/client-backend';
import { LocalizationProvider } from '../localization/Localization';
import { MessageBlocks } from './MessageBlocks';
import { LinkOpenerContext } from '../shellOperations/linkOpener';

// Written as attacks rather than as examples, because a message is written by a stranger: what is asserted below is
// that markup written into a message stays characters, that a heading a sender wrote cannot claim the level the
// application's own title holds, and that a block this build does not know costs the reader that block and no more.

const noEmphasis = {
    bold: false,
    italic: false,
    underline: false,
    strikethrough: false,
    monospace: false,
};

function run(text: string, overrides: Partial<MailInlineRun> = {}): MailInlineRun {
    return { text, emphasis: noEmphasis, foreground: null, link: null, ...overrides };
}

function linkTo(target: string): MailDocumentLink {
    return { target, host: 'example.invalid', asciiHost: null, deception: 'None', worthWarningAbout: false };
}

function drawing(blocks: readonly MailDocumentBlock[]) {
    return render(
        <LocalizationProvider>
            <LinkOpenerContext value={() => Promise.resolve()}>
                <MessageBlocks blocks={blocks} />
            </LinkOpenerContext>
        </LocalizationProvider>,
    );
}

describe('MessageBlocks', () => {
    it('draws markup a sender wrote into a message as the characters they wrote', () => {
        drawing([{ type: 'paragraph', content: [run('<script>alert(1)</script>')], alignment: 'Inherited' }]);

        expect(screen.getByText('<script>alert(1)</script>')).toBeDefined();
    });

    it('draws an image tag a sender wrote as text as the characters they wrote', () => {
        drawing([{ type: 'paragraph', content: [run('<img src=x onerror=alert(1)>')], alignment: 'Inherited' }]);

        expect(screen.getByText('<img src=x onerror=alert(1)>')).toBeDefined();

        // The characters are the whole of it: nothing a sender wrote became an element, so there is no picture to have
        // an event handler on.
        expect(screen.queryByRole('img')).toBeNull();
    });

    it('never lets a heading a sender wrote claim the level the application own title holds', () => {
        drawing([{ type: 'heading', level: 1, content: [run('A masthead')], alignment: 'Start' }]);

        expect(screen.getByRole('heading', { level: 2, name: 'A masthead' })).toBeDefined();
        expect(screen.queryByRole('heading', { level: 1 })).toBeNull();
    });

    it('draws the deepest heading a message may carry without running past the levels there are', () => {
        drawing([{ type: 'heading', level: 6, content: [run('A footnote')], alignment: 'End' }]);

        expect(screen.getByRole('heading', { level: 6, name: 'A footnote' })).toBeDefined();
    });

    it('composes several emphasis flags on one run rather than letting the last one win', () => {
        const emphasis = { bold: true, italic: true, underline: true, strikethrough: true, monospace: true };

        const { container } = drawing([
            { type: 'paragraph', content: [run('All at once', { emphasis })], alignment: 'Inherited' },
        ]);

        // Emphasis is carried by the elements that mean it, and none of the five publishes a role or a name to query
        // by. Naming the elements is therefore how the composition is asserted at all — the point of the test being
        // that five flags on one run produce five wrappers rather than one utility overwriting another.
        expect(container.textContent).toBe('All at once');
        expect(['strong', 'em', 'u', 's', 'code'].map((element) => container.querySelectorAll(element).length)).toEqual(
            [1, 1, 1, 1, 1],
        );
    });

    it('draws a block written for a newer client as a placeholder, and the rest of the message around it', () => {
        drawing([
            { type: 'unimplemented', identity: 'chart', version: 3 },
            { type: 'paragraph', content: [run('The rest of it.')], alignment: 'Inherited' },
        ]);

        expect(
            screen.getByText(
                'A part of this message was written for a newer client than this one, so it is not drawn.',
            ),
        ).toBeDefined();
        expect(screen.getByText('The rest of it.')).toBeDefined();
    });

    it('draws a table so a screen reader announces which cells label the columns', () => {
        drawing([
            {
                type: 'table',
                columns: [{ widthShare: 0.5 }, { widthShare: null }],
                rows: [
                    {
                        isHeader: true,
                        cells: [
                            {
                                columnSpan: 1,
                                rowSpan: 1,
                                alignment: 'Start',
                                background: null,
                                blocks: [{ type: 'paragraph', content: [run('Item')], alignment: 'Start' }],
                            },
                        ],
                    },
                    {
                        isHeader: false,
                        cells: [
                            {
                                columnSpan: 1,
                                rowSpan: 1,
                                alignment: 'Start',
                                background: '#0028a0',
                                blocks: [{ type: 'paragraph', content: [run('A kettle')], alignment: 'Start' }],
                            },
                        ],
                    },
                ],
            },
        ]);

        expect(screen.getByRole('columnheader', { name: 'Item' })).toBeDefined();
        expect(screen.getByRole('cell', { name: 'A kettle' })).toBeDefined();
    });

    it('draws a list as a list, so it is announced and navigated as one', () => {
        drawing([
            {
                type: 'list',
                ordered: true,
                items: [
                    { blocks: [{ type: 'paragraph', content: [run('First')], alignment: 'Start' }] },
                    { blocks: [{ type: 'paragraph', content: [run('Second')], alignment: 'Start' }] },
                ],
            },
        ]);

        expect(screen.getAllByRole('listitem').map((item) => item.textContent)).toEqual(['First', 'Second']);
    });

    it('announces a picture the sender did not describe rather than leaving it silent', () => {
        drawing([
            {
                type: 'image',
                image: { source: 'data:image/gif;base64,R0lGOD==', alternativeText: null, width: null, height: null },
                link: null,
                alignment: 'Center',
            },
        ]);

        expect(screen.getByRole('img', { name: 'A picture the sender did not describe' })).toBeDefined();
    });

    it('gives a picture the words the sender wrote for it', () => {
        drawing([
            {
                type: 'image',
                image: { source: 'data:image/gif;base64,R0lGOD==', alternativeText: 'The mark', width: 32, height: 32 },
                link: null,
                alignment: 'Center',
            },
        ]);

        expect(screen.getByRole('img', { name: 'The mark' })).toBeDefined();
    });

    it('keeps the whitespace of preformatted text, which is what that block is for', () => {
        drawing([{ type: 'preformatted', text: '  two   spaces\n  and a line' }]);

        // The default query collapses whitespace, which is the one thing this block exists to keep, so the text is
        // matched as it was written rather than as a reader of prose would read it.
        const kept = screen.getByText('  two   spaces\n  and a line', {
            normalizer: getDefaultNormalizer({ collapseWhitespace: false, trim: false }),
        });

        expect(kept).toBeDefined();
    });

    it('draws a quotation as a quotation, at whatever depth the message wrote it', () => {
        drawing([
            {
                type: 'quote',
                depth: 2,
                blocks: [{ type: 'paragraph', content: [run('You wrote this.')], alignment: 'Start' }],
            },
        ]);

        expect(screen.getByRole('blockquote')).toBeDefined();
        expect(screen.getByText('You wrote this.')).toBeDefined();
    });

    it('draws one anchor as one link however many runs the emphasis split it into', () => {
        const link = linkTo('https://example.invalid/renewal');
        const emphasized = { ...noEmphasis, bold: true };

        drawing([
            {
                type: 'paragraph',
                content: [run('Read the ', { link }), run('renewal', { link, emphasis: emphasized })],
                alignment: 'Inherited',
            },
        ]);

        // One anchor rather than two: a reader meeting the target and the warning twice, tabbing through it twice, and
        // finding it twice in a screen reader's list of links is the defect this asserts against.
        const links = screen.getAllByRole('link');

        expect(links.length).toBe(1);
        expect(links[0]?.textContent).toBe('Read the renewal');
    });

    it('keeps two anchors apart even where they run into each other', () => {
        drawing([
            {
                type: 'paragraph',
                content: [
                    run('One', { link: linkTo('https://example.invalid/one') }),
                    run('Two', { link: linkTo('https://example.invalid/two') }),
                ],
                alignment: 'Inherited',
            },
        ]);

        expect(screen.getAllByRole('link').map((anchor) => anchor.textContent)).toEqual(['One', 'Two']);
    });

    it('gives a table wider than the pane a keyboard path to the columns past its edge', () => {
        drawing([
            {
                type: 'table',
                columns: [{ widthShare: null }],
                rows: [
                    {
                        isHeader: false,
                        cells: [
                            {
                                columnSpan: 1,
                                rowSpan: 1,
                                alignment: 'Start',
                                background: null,
                                blocks: [{ type: 'paragraph', content: [run('A kettle')], alignment: 'Start' }],
                            },
                        ],
                    },
                ],
            },
        ]);

        const region = screen.getByRole('group', { name: 'A table in this message, scrollable sideways' });

        expect(region.tabIndex).toBe(0);
    });

    it('gives preformatted text wider than the pane a keyboard path to the rest of the line', () => {
        drawing([{ type: 'preformatted', text: 'a line longer than the pane' }]);

        const region = screen.getByRole('group', { name: 'Preformatted text in this message, scrollable sideways' });

        expect(region.tabIndex).toBe(0);
    });

    it('draws a separator as one rather than as an empty paragraph', () => {
        drawing([{ type: 'separator' }]);

        expect(screen.getByRole('separator')).toBeDefined();
    });
});
