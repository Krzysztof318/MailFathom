// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { describe, expect, it } from 'vitest';
import { providerMark } from './providerMarks';

describe('providerMark', () => {
    it('answers with the outline committed for a provider the client carries a mark for', () => {
        const mark = providerMark('github');

        expect(mark?.box).toBe('0 0 24 24');
        expect(mark?.outline.length).toBeGreaterThan(0);
    });

    // The key is what an operator typed into their own configuration, and a configuration key is one name whichever
    // way they capitalized it.
    it.each(['GitHub', 'GITHUB', 'github'])('matches %s on the name the configuration gives the server', (name) => {
        expect(providerMark(name)).toEqual(providerMark('github'));
    });

    // An operator names their own authorization servers and nothing says one of them is a brand anybody drew, so the
    // screen falls back to the symbol the client already has for a credential.
    it.each([
        ['a server nobody drew a mark for', 'our-own-idp'],
        ['a name that is not one at all', ''],
    ])('carries nothing for %s rather than drawing the wrong mark', (_, name) => {
        expect(providerMark(name)).toBeUndefined();
    });

    // The name is a string the deployment published and the lookup is an object with a prototype behind it, so each of
    // these is an already-lowercase name that answers with something rather than with nothing — and what a screen then
    // draws is an empty square where the credential symbol belongs.
    it.each(['constructor', '__proto__', 'toString'])(
        'carries nothing for %s, which is the prototype and not a mark',
        (name) => {
            expect(providerMark(name)).toBeUndefined();
        },
    );

    // The family is exported with no colour of its own, so what keeps nine brands from being drawn as nine grey marks
    // is the colour the token layer declares for each of them.
    it('draws a mark in the brand colour declared for that provider rather than in the text colour', () => {
        expect(providerMark('GitHub')?.tint).toBe('var(--color-provider-github, currentColor)');
    });

    it('draws every mark it carries on the one square the family is exported on', () => {
        for (const name of [
            'apple',
            'auth0',
            'discord',
            'github',
            'gitlab',
            'gmail',
            'keycloak',
            'nextcloud',
            'okta',
        ]) {
            expect(providerMark(name)?.box, name).toBe('0 0 24 24');
        }
    });
});
