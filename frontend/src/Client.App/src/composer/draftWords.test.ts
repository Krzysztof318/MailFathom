// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { describe, expect, it } from 'vitest';
import type { MailBody, MailDocumentBlock, MailInlineRun, MailTextEmphasis } from '@mailfathom/client-backend';
import { draftWords } from './draftWords';
import { htmlOf, plainTextOf } from './writtenText';

const nothingEmphasised: MailTextEmphasis = {
    bold: false,
    italic: false,
    underline: false,
    strikethrough: false,
    monospace: false,
};

function run(text: string, emphasis: Partial<MailTextEmphasis> = {}, target: string | null = null): MailInlineRun {
    return {
        text,
        emphasis: { ...nothingEmphasised, ...emphasis },
        foreground: null,
        link:
            target === null
                ? null
                : {
                      target,
                      host: null,
                      asciiHost: null,
                      deception: 'NotApplicable',
                      worthWarningAbout: false,
                  },
    };
}

function paragraph(...content: readonly MailInlineRun[]): MailDocumentBlock {
    return { type: 'paragraph', content, alignment: 'Inherited' };
}

function bodyOf(blocks: readonly MailDocumentBlock[], plainText = ''): MailBody {
    return {
        storedEmailId: 'message-1',
        availability: 'Readable',
        plainText: { text: plainText, originalCharacterCount: plainText.length, truncation: 'None' },
        document:
            blocks.length === 0
                ? null
                : {
                      blocks,
                      refusal: 'None',
                      removedRemoteReferenceCount: 0,
                      retainedRemoteImageCount: 0,
                      inlineImageCount: 0,
                      undrawnInlineImageCount: 0,
                      truncated: false,
                  },
        selfContainedHtml: null,
        remoteImagesRequested: false,
    };
}

describe('draftWords', () => {
    it('carries each paragraph across as a paragraph, which is what the composer writes one as', () => {
        expect(htmlOf(draftWords(bodyOf([paragraph(run('Half a thought')), paragraph(run('and the rest'))])))).toBe(
            '<p>Half a thought</p><p>and the rest</p>',
        );
    });

    it('wraps a run in each emphasis it carries, one element inside another rather than a shape named for the pair', () => {
        expect(htmlOf(draftWords(bodyOf([paragraph(run('urgent', { bold: true, italic: true }))])))).toBe(
            '<p><em><strong>urgent</strong></em></p>',
        );
    });

    // The composer writes no code, so borrowing an element that means something else would say what the author did
    // not: what is lost is the face the run was set in rather than the words.
    it('drops the one emphasis the composer has no element for, keeping the words that carried it', () => {
        expect(htmlOf(draftWords(bodyOf([paragraph(run('rm -rf', { monospace: true }))])))).toBe('<p>rm -rf</p>');
    });

    it('keeps a link the client would follow, and draws one it would not as its own words', () => {
        const followed = draftWords(bodyOf([paragraph(run('the page', {}, 'https://example.test/page'))]));
        const refused = draftWords(bodyOf([paragraph(run('the page', {}, 'javascript:alert(1)'))]));

        expect(htmlOf(followed)).toBe('<p><a href="https://example.test/page">the page</a></p>');
        expect(htmlOf(refused)).toBe('<p>the page</p>');
    });

    // The composer writes no headings, so the line keeps the weight that says it was one rather than levelling into
    // the prose around it.
    it('carries a heading across as the strongest emphasis the composer does write', () => {
        const heading: MailDocumentBlock = {
            type: 'heading',
            level: 2,
            content: [run('Agenda')],
            alignment: 'Inherited',
        };

        expect(htmlOf(draftWords(bodyOf([heading])))).toBe('<p><strong>Agenda</strong></p>');
    });

    it('carries a list across as the list it was, its items in the order they were written', () => {
        const list: MailDocumentBlock = {
            type: 'list',
            ordered: true,
            items: [{ blocks: [paragraph(run('first'))] }, { blocks: [paragraph(run('second'))] }],
        };

        expect(htmlOf(draftWords(bodyOf([list])))).toBe('<ol><li><p>first</p></li><li><p>second</p></li></ol>');
    });

    it('carries a quotation across as one, which is a shape the composer draws', () => {
        const quote: MailDocumentBlock = { type: 'quote', depth: 1, blocks: [paragraph(run('you wrote'))] };

        expect(htmlOf(draftWords(bodyOf([quote])))).toBe('<blockquote><p>you wrote</p></blockquote>');
    });

    // The grid is not something the composer draws. A table dropped whole would take the message's words with it, so
    // what is kept is what the cells say, in the order they say it.
    it('keeps what a table’s cells say although the grid around them is lost', () => {
        const table: MailDocumentBlock = {
            type: 'table',
            columns: [{ widthShare: null }, { widthShare: null }],
            rows: [
                {
                    isHeader: false,
                    cells: [
                        {
                            columnSpan: 1,
                            rowSpan: 1,
                            alignment: 'Inherited',
                            background: null,
                            blocks: [paragraph(run('Monday'))],
                        },
                        {
                            columnSpan: 1,
                            rowSpan: 1,
                            alignment: 'Inherited',
                            background: null,
                            blocks: [paragraph(run('nine'))],
                        },
                    ],
                },
            ],
        };

        expect(htmlOf(draftWords(bodyOf([table])))).toBe('<p>Monday</p><p>nine</p>');
    });

    it('drops a block the composer has nothing to write it with, rather than approximating one', () => {
        const undrawn: readonly MailDocumentBlock[] = [
            { type: 'separator' },
            {
                type: 'image',
                image: { source: 'cid:one', alternativeText: null, width: null, height: null },
                link: null,
                alignment: 'Inherited',
            },
            { type: 'unimplemented', identity: 'chart', version: 3 },
        ];

        expect(draftWords(bodyOf(undrawn))).toStrictEqual([]);
    });

    it('keeps preformatted text line by line, which is the shape the composer’s own region produces', () => {
        const preformatted: MailDocumentBlock = { type: 'preformatted', text: 'one\ntwo' };

        expect(htmlOf(draftWords(bodyOf([preformatted])))).toBe('<p>one</p><p>two</p>');
    });

    // A body the deployment could not reduce is answered as plain text, which is the rendering it always carries.
    it('falls back to the plain text where the deployment reduced no document at all', () => {
        expect(plainTextOf(draftWords(bodyOf([], 'Sent from a phone\n\nsorry')))).toBe('Sent from a phone\n\nsorry');
    });
});
