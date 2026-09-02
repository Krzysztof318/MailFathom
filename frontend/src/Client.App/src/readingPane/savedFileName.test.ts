// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { describe, expect, it } from 'vitest';
import { savedAs } from './savedFileName';

describe('savedAs', () => {
    it('keeps the name the message gave the file, extension and all', () => {
        expect(savedAs('invoice.pdf', 0)).toBe('invoice.pdf');
    });

    it.each([
        ['a path a sender wrote into a name', '../../etc/passwd', 'etcpasswd'],
        ['a separator either operating system acts on', 'reports\\2026\\q3.csv', 'reports2026q3.csv'],
        ['a drive letter', 'C:notes.txt', 'Cnotes.txt'],
        ['a name that would be hidden by leading dots', '...secret.key', 'secret.key'],
        ['a name a file system would not keep the end of', 'summary.doc.', 'summary.doc'],
        ['an override reversing what a listing draws after it', 'invoice\u202Efdp.exe', 'invoicefdp.exe'],
        ['an isolate a sender wrapped an extension in', 'photo\u2066.jpg\u2069', 'photo.jpg'],
        ['a mark that would reorder what is drawn', 'report\u200F.pdf', 'report.pdf'],
        ['a control character nothing draws', 'notes\u0085.txt', 'notes.txt'],
    ])('reduces %s to something this client is willing to write', (_case, written, offered) => {
        expect(savedAs(written, 0)).toBe(offered);
    });

    it('answers a part with no usable name at all by the position the message gave it', () => {
        expect(savedAs('///', 3)).toBe('attachment-3');
    });

    it('answers a part the message named nothing by the position as well', () => {
        expect(savedAs(null, 0)).toBe('attachment-0');
    });

    it('keeps a sender from naming a file longer than this client will write', () => {
        expect(savedAs('a'.repeat(500), 0).length).toBe(128);
    });

    // Cutting to length is what can put back the character the trim removed, so the order of the two is the behaviour.
    it('keeps a name cut to length from ending in what a file system would drop', () => {
        expect(savedAs(`${'a'.repeat(127)}..pdf`, 0)).toBe('a'.repeat(127));
    });
});
