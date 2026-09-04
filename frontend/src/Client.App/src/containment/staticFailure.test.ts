// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { afterEach, describe, expect, it } from 'vitest';
import { showStaticFailure } from './staticFailure';

// What `index.html` carries and what the client had drawn, built as elements rather than written as markup — this
// suite renders no document of its own, and building one out of a string is the parser ADR 0024 exists not to have.
const carried = 'MailFathom stopped';
const drawn = 'What was left of the client';

function documentCarryingTheSurface(): void {
    const template = document.createElement('template');
    const heading = document.createElement('h1');

    heading.textContent = carried;
    template.content.append(heading);
    template.id = 'client-failed';
    document.body.append(template);
}

function documentWithARoot(): void {
    const root = document.createElement('div');
    const left = document.createElement('p');

    left.textContent = drawn;
    root.id = 'root';
    root.append(left);
    document.body.append(root);
}

describe('showStaticFailure', () => {
    afterEach(() => {
        document.body.replaceChildren();
    });

    it('puts what the document carries where the client was drawn', () => {
        documentWithARoot();
        documentCarryingTheSurface();

        showStaticFailure();

        expect(document.querySelector('#root h1')?.textContent).toBe(carried);
        expect(document.body.textContent).not.toContain(drawn);
    });

    it('shows it in a document that carries no root to mount into at all', () => {
        documentCarryingTheSurface();

        showStaticFailure();

        expect(document.body.textContent).toContain(carried);
    });

    it('leaves a document carrying no such surface as it found it', () => {
        documentWithARoot();

        showStaticFailure();

        expect(document.body.textContent).toContain(drawn);
    });
});
