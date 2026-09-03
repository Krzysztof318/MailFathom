// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { afterEach, describe, expect, it } from 'vitest';
import { nothingWrittenYet, type Composition } from './composition';
import { forgetComposition, rememberComposition, rememberedComposition } from './keptComposition';

const storageKey = 'mailfathom.composition';

const written: Composition = {
    ...nothingWrittenYet('work'),
    subject: 'Invoice',
    to: ['ada@example.invalid'],
    cc: ['bo@example.invalid'],
    words: 'Here it is.',
};

afterEach(() => {
    window.sessionStorage.clear();
});

describe('rememberedComposition', () => {
    it('reads back what was being written, so a reload returns to it', () => {
        rememberComposition(written);

        expect(rememberedComposition()).toEqual(written);
    });

    it('reads back an answer with the message it answers', () => {
        const answering: Composition = { ...written, answering: { storedEmailId: 'e1', answers: 'everyone' } };

        rememberComposition(answering);

        expect(rememberedComposition()).toEqual(answering);
    });

    it('answers nothing where nothing was kept', () => {
        expect(rememberedComposition()).toBeNull();
    });

    it('drops what was being written, which is what signing out does', () => {
        rememberComposition(written);
        forgetComposition();

        expect(rememberedComposition()).toBeNull();
    });

    it.each([
        ['text that is not a composition at all', 'not json'],
        ['a list rather than a message', '[]'],
        ['a message with no words', JSON.stringify({ ...written, words: undefined })],
        ['a subject that is not text', JSON.stringify({ ...written, subject: 7 })],
        ['an address that is not text', JSON.stringify({ ...written, to: [7] })],
        ['an empty address', JSON.stringify({ ...written, to: [''] })],
        ['a header that is not a list', JSON.stringify({ ...written, cc: 'bo@example.invalid' })],
        ['more addresses than one header takes', JSON.stringify({ ...written, to: Array.from({ length: 257 }, () => 'a@b') })],
        ['a subject past a header line', JSON.stringify({ ...written, subject: 'x'.repeat(999) })],
        ['an address past the longest one', JSON.stringify({ ...written, to: ['x'.repeat(321)] })],
        ['an account past an identifier', JSON.stringify({ ...written, account: 'x'.repeat(257) })],
        ['an answer to nothing named', JSON.stringify({ ...written, answering: { answers: 'everyone' } })],
        ['an answer of a kind there is none of', JSON.stringify({ ...written, answering: { storedEmailId: 'e1', answers: 'shout' } })],
        ['an answer that is a list', JSON.stringify({ ...written, answering: [] })],
    ])('answers nothing for %s, rather than a message with a hole in it', (_, stored) => {
        window.sessionStorage.setItem(storageKey, stored);

        expect(rememberedComposition()).toBeNull();
    });
});
