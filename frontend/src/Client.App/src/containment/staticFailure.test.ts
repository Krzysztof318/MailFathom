// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { afterEach, describe, expect, it, vi } from 'vitest';
import { showStaticFailure } from './staticFailure';

// What `index.html` carries and what the client had drawn, built as elements rather than written as markup — this
// suite renders no document of its own, and building one out of a string is the parser ADR 0024 exists not to have.
const carried = 'MailFathom stopped';
const drawn = 'What was left of the client';

function documentCarryingTheSurface(): void {
    const template = document.createElement('template');
    const surface = document.createElement('div');
    const heading = document.createElement('h1');

    heading.textContent = carried;
    surface.append(heading);
    surface.setAttribute('role', 'alert');
    surface.tabIndex = -1;
    template.content.append(surface);
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

    // Everything that was on the screen has just been replaced, so whatever held focus is no longer in the document.
    // Waited for rather than read straight away, because focus is taken a task later than the surface is shown — the
    // source says what React does about focus during its own handling of the failure this stands for.
    it('puts the reader at the start of what it put in front of them', async () => {
        documentWithARoot();
        documentCarryingTheSurface();

        showStaticFailure();

        await vi.waitFor(() => {
            expect(document.activeElement).toBe(document.querySelector('#root [role="alert"]'));
        });
    });

    it('leaves a document carrying no such surface as it found it', () => {
        documentWithARoot();

        showStaticFailure();

        expect(document.body.textContent).toContain(drawn);
    });
});
