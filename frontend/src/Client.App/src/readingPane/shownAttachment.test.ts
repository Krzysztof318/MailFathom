// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { describe, expect, it } from 'vitest';
import type { MailAttachment } from '@mailfathom/client-backend';
import { charsetOf, mediaTypeOf, shownAttachment } from './shownAttachment';

// What the client will draw and what it will hand over instead. Every kind is asserted rather than the interesting ones,
// because this decision is what stands between a reader and a file a sender composed: a kind admitted by accident is a
// kind nobody weighed, and the two that are refused are refused for reasons stated in the module rather than by taste.

function file(over: Partial<MailAttachment> = {}): MailAttachment {
    return {
        position: 0,
        fileName: 'note.txt',
        wasFileNameNormalized: false,
        mediaType: 'text/plain',
        sizeOctets: 64,
        ...over,
    };
}

describe('shownAttachment', () => {
    it.each(['image/avif', 'image/bmp', 'image/gif', 'image/jpeg', 'image/png', 'image/webp'])(
        'draws %s as a picture',
        (mediaType) => {
            expect(shownAttachment(file({ mediaType }))).toEqual({ as: 'picture' });
        },
    );

    it('reads the kind under whatever case and parameters the sender wrote it in', () => {
        expect(shownAttachment(file({ mediaType: 'IMAGE/PNG; name=photo.png' }))).toEqual({ as: 'picture' });
    });

    it('draws text under the character set the message declared', () => {
        expect(shownAttachment(file({ mediaType: 'text/plain; charset=iso-8859-2' }))).toEqual({
            as: 'text',
            charset: 'iso-8859-2',
        });
    });

    it('draws text the message declared no character set for as UTF-8', () => {
        expect(shownAttachment(file())).toEqual({ as: 'text', charset: 'utf-8' });
    });

    it('draws a text kind that is not plain text as its own source rather than refusing it', () => {
        expect(shownAttachment(file({ fileName: 'page.html', mediaType: 'text/html' }))).toEqual({
            as: 'text',
            charset: 'utf-8',
        });
    });

    it('refuses a PDF, which is the kind neither head can be given one answer for', () => {
        expect(shownAttachment(file({ fileName: 'contract.pdf', mediaType: 'application/pdf' }))).toBe('kindNotShown');
    });

    it('refuses an SVG, which is markup a sender wrote however an element would draw it', () => {
        expect(shownAttachment(file({ fileName: 'logo.svg', mediaType: 'image/svg+xml' }))).toBe('kindNotShown');
    });

    it('refuses a picture larger than this surface draws', () => {
        expect(shownAttachment(file({ mediaType: 'image/png', sizeOctets: 8 * 1024 * 1024 + 1 }))).toBe(
            'largerThanShown',
        );
    });

    it('draws a picture exactly as large as this surface draws', () => {
        expect(shownAttachment(file({ mediaType: 'image/png', sizeOctets: 8 * 1024 * 1024 }))).toEqual({
            as: 'picture',
        });
    });

    it('refuses text larger than this surface lays out, which is the smaller of the two ceilings', () => {
        expect(shownAttachment(file({ sizeOctets: 1024 * 1024 + 1 }))).toBe('largerThanShown');
    });
});

describe('mediaTypeOf', () => {
    it('reads what a file declares itself to be without the parameters written after it', () => {
        expect(mediaTypeOf('Text/Plain; charset=UTF-8')).toBe('text/plain');
    });

    it('reads a type a sender wrote nothing for as nothing rather than as a kind', () => {
        expect(mediaTypeOf('')).toBe('');
    });
});

describe('charsetOf', () => {
    it('reads the character set a sender wrote', () => {
        expect(charsetOf('text/plain; charset=windows-1250')).toBe('windows-1250');
    });

    it('reads a quoted character set without its quotes', () => {
        expect(charsetOf('text/plain; charset="us-ascii"')).toBe('us-ascii');
    });

    it('reads a character set written after another parameter', () => {
        expect(charsetOf('text/plain; format=flowed; charset=koi8-r')).toBe('koi8-r');
    });

    it('answers UTF-8 where the message declared none', () => {
        expect(charsetOf('text/plain')).toBe('utf-8');
    });
});
