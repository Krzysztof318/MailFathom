// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { describe, expect, it } from 'vitest';
import type { MailDocumentBlock } from '@mailfathom/client-backend';
import { splitQuotedHistory } from './quotedHistory';

function paragraph(text: string): MailDocumentBlock {
    return {
        type: 'paragraph',
        alignment: 'Inherited',
        content: [
            {
                text,
                emphasis: { bold: false, italic: false, underline: false, strikethrough: false, monospace: false },
                foreground: null,
                link: null,
            },
        ],
    };
}

function quote(text: string): MailDocumentBlock {
    return { type: 'quote', depth: 1, blocks: [paragraph(text)] };
}

const separator: MailDocumentBlock = { type: 'separator' };

describe('splitQuotedHistory', () => {
    it('keeps what a reply added and folds the conversation it quoted underneath it', () => {
        const written = paragraph('Yes, Thursday works.');

        expect(splitQuotedHistory([written, quote('Does Thursday work?')])).toStrictEqual({
            contribution: [written],
            quotedHistory: [quote('Does Thursday work?')],
        });
    });

    it('folds the rule a mail client draws between a reply and what it answers along with the quotation', () => {
        const written = paragraph('Yes, Thursday works.');

        expect(splitQuotedHistory([written, separator, quote('Does Thursday work?')])).toStrictEqual({
            contribution: [written],
            quotedHistory: [separator, quote('Does Thursday work?')],
        });
    });

    it('folds every quotation a reply ended on rather than only the last of them', () => {
        const written = paragraph('Both of those are fine.');
        const older = quote('And the week after?');
        const oldest = quote('Does Thursday work?');

        expect(splitQuotedHistory([written, older, oldest])).toStrictEqual({
            contribution: [written],
            quotedHistory: [older, oldest],
        });
    });

    it('leaves a quotation somebody replied underneath where it is, because there it is what the message said', () => {
        const blocks = [quote('Does Thursday work?'), paragraph('Yes, Thursday works.')];

        expect(splitQuotedHistory(blocks)).toStrictEqual({ contribution: blocks, quotedHistory: [] });
    });

    it('leaves a message that ends on a rule rather than on a quotation whole', () => {
        const blocks = [paragraph('Yes, Thursday works.'), separator];

        expect(splitQuotedHistory(blocks)).toStrictEqual({ contribution: blocks, quotedHistory: [] });
    });

    it('draws a message that is nothing but quotation whole, so a forward is not a screen with nothing on it', () => {
        const blocks = [quote('The figures you asked for are attached.')];

        expect(splitQuotedHistory(blocks)).toStrictEqual({ contribution: blocks, quotedHistory: [] });
    });

    it('leaves a message that quoted nothing whole', () => {
        const blocks = [paragraph('The figures you asked for are attached.')];

        expect(splitQuotedHistory(blocks)).toStrictEqual({ contribution: blocks, quotedHistory: [] });
    });

    it('answers an empty document as one that added nothing and quoted nothing', () => {
        expect(splitQuotedHistory([])).toStrictEqual({ contribution: [], quotedHistory: [] });
    });
});
