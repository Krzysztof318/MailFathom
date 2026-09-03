// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { describe, expect, it } from 'vitest';
import { kindOf } from './fileKind';

describe('kindOf', () => {
    it.each([
        ['application/pdf', 'pdf'],
        ['application/msword', 'doc'],
        ['application/vnd.openxmlformats-officedocument.wordprocessingml.document', 'docx'],
        ['application/vnd.ms-excel', 'xls'],
        ['application/vnd.openxmlformats-officedocument.spreadsheetml.sheet', 'xlsx'],
        ['application/vnd.ms-powerpoint', 'ppt'],
        ['application/vnd.openxmlformats-officedocument.presentationml.presentation', 'pptx'],
        ['image/png', 'image'],
        ['image/svg+xml', 'image'],
    ])('names the family a message declared %s under', (mediaType, kind) => {
        expect(kindOf('carried', mediaType)).toBe(kind);
    });

    it('names the family the message declared rather than the one the sender named the file after', () => {
        expect(kindOf('figures.pdf', 'application/vnd.ms-excel')).toBe('xls');
    });

    it('reads a media type back through its parameters and its casing, which a sender writes as they please', () => {
        expect(kindOf(null, 'Application/PDF; name="polisa.pdf"')).toBe('pdf');
    });

    it('falls back to the extension where the declared type names no family', () => {
        expect(kindOf('archive.zip', 'application/octet-stream')).toBe('zip');
    });

    it('falls back to the declared subtype where there is no name to read an extension from', () => {
        expect(kindOf(null, 'text/calendar')).toBe('calendar');
    });

    it('cuts a kind long enough to be a description down to the glance the badge is', () => {
        expect(kindOf('no-extension', 'application/octet-stream')).toBe('octet-st');
    });
});
