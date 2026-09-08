// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { describe, expect, it } from 'vitest';
import { followable, htmlOf, plainTextOf, writtenIn, writtenTextIn, type WrittenNode } from './writtenText';

// A region shaped the way a browser leaves one after somebody has typed and formatted in it. It is built element by
// element rather than from a string of markup, for the reason the module itself gives: nothing on this path turns a
// string into nodes, and a test that did would be proving the code against an arrangement it refuses.
function region(...holds: (Node | string)[]): HTMLElement {
    return element('div', ...holds);
}

function element(name: string, ...holds: (Node | string)[]): HTMLElement {
    const made = document.createElement(name);

    made.append(...holds);

    return made;
}

describe('writtenIn', () => {
    it('reads what was typed and the emphasis it was given', () => {
        const written = writtenIn(region('Read the ', element('b', 'third'), ' column.'));

        expect(written).toEqual([
            { text: 'Read the ' },
            { element: 'b', address: null, holds: [{ text: 'third' }] },
            { text: ' column.' },
        ]);
    });

    it('unwraps an element it has no name for, so what was typed inside one survives', () => {
        const written = writtenIn(region(element('font', element('b', 'Bold'))));

        expect(written).toEqual([{ element: 'b', address: null, holds: [{ text: 'Bold' }] }]);
    });

    it('keeps a link somebody could follow', () => {
        const link = element('a', 'the invoice');

        link.setAttribute('href', 'https://example.invalid/invoice');

        expect(writtenIn(region(link))).toEqual([
            { element: 'a', address: 'https://example.invalid/invoice', holds: [{ text: 'the invoice' }] },
        ]);
    });

    it.each([['javascript:alert(1)'], ['data:text/html,<script>'], ['/drafts/1'], ['']])(
        'keeps the words of a link to %s and not the link',
        (address) => {
            const link = element('a', 'press here');

            link.setAttribute('href', address);

            expect(writtenIn(region(link))).toEqual([{ text: 'press here' }]);
        },
    );

    it('carries no attribute an engine left behind, a link’s address excepted', () => {
        const emphasis = element('b', 'Bold');

        emphasis.setAttribute('onclick', 'steal()');
        emphasis.setAttribute('style', 'position:fixed');

        expect(writtenIn(region(emphasis))).toEqual([{ element: 'b', address: null, holds: [{ text: 'Bold' }] }]);
    });

    it('reads a list as the list it is', () => {
        const written = writtenIn(region(element('ul', element('li', 'One'), element('li', 'Two'))));

        expect(written).toEqual([
            {
                element: 'ul',
                address: null,
                holds: [
                    { element: 'li', address: null, holds: [{ text: 'One' }] },
                    { element: 'li', address: null, holds: [{ text: 'Two' }] },
                ],
            },
        ]);
    });

    it('reads a region nested past the bound as words, so what it composes is always kept and read back', () => {
        let deepest = element('div', 'Still here');

        for (let depth = 0; depth < 200; depth += 1) {
            deepest = element('blockquote', deepest);
        }

        const written = writtenIn(region(deepest));

        expect(plainTextOf(written)).toContain('Still here');
        expect(writtenTextIn(JSON.parse(JSON.stringify(written)) as unknown)).not.toBeNull();
    });
});

describe('htmlOf', () => {
    it('escapes what somebody typed rather than letting it become markup', () => {
        expect(htmlOf([{ text: 'a < b && c > d' }])).toBe('a &lt; b &amp;&amp; c &gt; d');
    });

    it('escapes a link’s address in the attribute it is written into', () => {
        const written: readonly WrittenNode[] = [
            { element: 'a', address: 'https://example.invalid/?a="b&c', holds: [{ text: 'there' }] },
        ];

        expect(htmlOf(written)).toBe('<a href="https://example.invalid/?a=&quot;b&amp;c">there</a>');
    });

    it('writes a line break as the element that carries no content', () => {
        expect(htmlOf([{ text: 'One' }, { element: 'br', address: null, holds: [] }, { text: 'Two' }])).toBe(
            'One<br />Two',
        );
    });
});

describe('plainTextOf', () => {
    it('reads each line an engine wrote as its own line', () => {
        const written = writtenIn(region(element('div', 'One'), element('div', 'Two')));

        expect(plainTextOf(written)).toBe('One\nTwo');
    });

    it('reads a paragraph as a break in the reading rather than the next line', () => {
        const written = writtenIn(region(element('p', 'One'), element('p', 'Two')));

        expect(plainTextOf(written)).toBe('One\n\nTwo');
    });

    it('marks a bulleted list so it still reads as one with no markup to draw it', () => {
        const written = writtenIn(region(element('ul', element('li', 'One'), element('li', 'Two'))));

        expect(plainTextOf(written)).toBe('- One\n- Two');
    });

    it('numbers a numbered list from where it starts', () => {
        const written = writtenIn(region(element('ol', element('li', 'First'), element('li', 'Second'))));

        expect(plainTextOf(written)).toBe('1. First\n2. Second');
    });

    it('quotes what was quoted, as mail has always written a quotation', () => {
        const written = writtenIn(
            region(element('blockquote', element('div', 'You wrote'), element('div', 'this')), 'Agreed.'),
        );

        expect(plainTextOf(written)).toBe('> You wrote\n> this\nAgreed.');
    });

    it('reads emphasis as the words it emphasized, there being no plain-text spelling of it', () => {
        const written = writtenIn(region('Read the ', element('b', 'third'), ' column.'));

        expect(plainTextOf(written)).toBe('Read the third column.');
    });

    it('is nothing for a message nobody has written in', () => {
        expect(plainTextOf([])).toBe('');
        expect(plainTextOf([{ element: 'br', address: null, holds: [] }])).toBe('');
    });
});

describe('followable', () => {
    it.each([
        ['https://example.invalid/invoice', true],
        ['http://example.invalid', true],
        ['mailto:ada@example.invalid', true],
        ['javascript:alert(1)', false],
        ['data:text/html,<script>', false],
        ['/drafts/1', false],
        ['example.invalid', false],
        ['', false],
    ])('reads %s as a link this client would make: %s', (address, expected) => {
        expect(followable(address)).toBe(expected);
    });
});

describe('writtenTextIn', () => {
    it('reads back a message this client wrote', () => {
        const written: readonly WrittenNode[] = [
            { element: 'div', address: null, holds: [{ text: 'Half a sentence' }] },
        ];

        expect(writtenTextIn(JSON.parse(JSON.stringify(written)))).toEqual(written);
    });

    it.each([
        ['something that is not a list of nodes at all', 'Half a sentence'],
        ['a node that is neither text nor an element', [{}]],
        ['text that is not text', [{ text: 7 }]],
        ['an element this client cannot write', [{ element: 'script', address: null, holds: [] }]],
        ['an element whose contents are not a list', [{ element: 'div', address: null, holds: 'here' }]],
        ['a link to somewhere this client would not follow', [{ element: 'a', address: 'javascript:x', holds: [] }]],
        [
            'an address on something that is not a link',
            [{ element: 'blockquote', address: 'https://example.invalid', holds: [] }],
        ],
    ])('refuses %s', (_, kept) => {
        expect(writtenTextIn(kept)).toBeNull();
    });

    it('refuses a message nested deeper than anything the composer writes', () => {
        let deepest: WrittenNode = { text: 'here' };

        for (let depth = 0; depth < 200; depth += 1) {
            deepest = { element: 'div', address: null, holds: [deepest] };
        }

        expect(writtenTextIn([deepest])).toBeNull();
    });
});
