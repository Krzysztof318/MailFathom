// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { StrictMode, type ReactNode } from 'react';
import { render, screen, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { ComposingContext } from '../composer/useComposing';
import { LocalizationProvider } from '../localization/Localization';
import type { Space as SpaceName } from '../routing/spaces';
import { WorkspaceProvider } from '../workspace/Workspace';
import { Space } from './Space';

// Not catalogue entries: each stands for whatever the frame composes for Mail, which is the point of the three props.
const handedToMail = 'The mail this space was handed.';
const handedTheFolders = 'The folder tree this space was handed.';
const handedTheTabs = 'The tab strip this space was handed.';
const handedTheList = 'The message list this space was handed.';
const handedTheIntent = 'The question this space was handed.';
const handedTheStatus = 'The connection this space was handed.';

// Every case here renders under `StrictMode`, which is what `main.tsx` mounts and what makes React invoke an effect
// twice on the first mount. A focus rule written against "has this effect run before" passes without it and moves
// focus on landing with it, so the wrapper is the point of the test rather than a detail of the harness.
// The toolbar and the corner control both ask whether writing a message is offered. Nothing here is about writing one,
// so nothing offers it and both stand as the planned controls they were.
const nothingBeingWritten = {
    offered: false,
    opening: null,
    compose: () => undefined,
    close: () => undefined,
};

function inStrictMode(space: SpaceName): ReactNode {
    return (
        <StrictMode>
            <LocalizationProvider>
                <WorkspaceProvider>
                    <ComposingContext value={nothingBeingWritten}>
                        <Space
                            space={space}
                            intent={<p>{handedTheIntent}</p>}
                            status={<p>{handedTheStatus}</p>}
                            folders={<p>{handedTheFolders}</p>}
                            list={<p>{handedTheList}</p>}
                            mail={<p>{handedToMail}</p>}
                            tabs={<p>{handedTheTabs}</p>}
                            person="reader"
                        />
                    </ComposingContext>
                </WorkspaceProvider>
            </LocalizationProvider>
        </StrictMode>
    );
}

// The region is read at the width the workspace opens out at, so that Mail draws all three of its regions at once:
// jsdom lays nothing out, so the width is answered here rather than measured.
const declaredMatchMedia = Object.getOwnPropertyDescriptor(window, 'matchMedia');

beforeEach(() => {
    Object.defineProperty(window, 'matchMedia', {
        configurable: true,
        value: (query: string) => ({
            media: query,
            matches: query.includes('min-width'),
            addEventListener: () => undefined,
            removeEventListener: () => undefined,
        }),
    });
});

afterEach(() => {
    if (declaredMatchMedia !== undefined) {
        Object.defineProperty(window, 'matchMedia', declaredMatchMedia);
    }
});

describe('Space', () => {
    it('leaves focus where it was on landing, rather than pulling it into the content nobody navigated to', () => {
        render(inStrictMode('discover'));

        expect(document.activeElement).toBe(document.body);
    });

    it('puts focus at the start of the new content when the address changes', () => {
        const { rerender } = render(inStrictMode('discover'));

        rerender(inStrictMode('mail'));

        expect(document.activeElement).toBe(screen.getByRole('main'));
    });

    it('names the space it is showing', () => {
        render(inStrictMode('cases'));

        expect(screen.getByRole('heading', { name: 'Cases' })).toBeDefined();
        expect(screen.getByRole('main', { name: 'Cases' })).toBeDefined();
    });

    it('draws Mail without a heading, as the design project does, and names the landmark instead', () => {
        render(inStrictMode('mail'));

        expect(screen.queryByRole('heading', { level: 1 })).toBeNull();
        expect(screen.getByRole('main', { name: 'Mail' })).toBeDefined();
    });

    it('carries the question and the connection into every space', () => {
        for (const space of ['mail', 'cases'] as const) {
            const { unmount } = render(inStrictMode(space));

            expect(screen.getByText(handedTheIntent)).toBeDefined();
            expect(screen.getByText(handedTheStatus)).toBeDefined();
            unmount();
        }
    });

    it('shows what the frame composed for Mail in the Mail space', () => {
        render(inStrictMode('mail'));

        expect(screen.getByText(handedToMail)).toBeDefined();
    });

    it('shows the scope the Mail space is drawn against beside what it is drawn from', () => {
        render(inStrictMode('mail'));

        expect(screen.getByText(handedTheFolders)).toBeDefined();
    });

    it('does not call the Mail space unbuilt, which is what it stopped being when it started reading mail', () => {
        render(inStrictMode('mail'));

        expect(screen.queryByText(/This space is not built yet\./)).toBeNull();
    });

    it('shows the pending note in a space nothing has been built for yet, with Mail standing behind it', () => {
        render(inStrictMode('cases'));

        expect(screen.getByText(/This space is not built yet\./)).toBeDefined();
        expect(screen.queryByRole('main', { name: 'Mail' })).toBeNull();
        expect(screen.getByRole('main', { name: 'Cases' })).toBeDefined();
    });

    // What Mail holds is the folder it was reading, the pages of it that answered, and the place in them the reader had
    // scrolled to, and all three are state of the components it renders — so it is stood aside rather than taken down,
    // and coming back is a return rather than a rebuild. What says it is aside is what a reader would find: no second
    // landmark naming Mail, and nothing in it to tab into.
    it('keeps Mail on the screen while another space is in front of it, out of reach and out of the reading order', () => {
        render(inStrictMode('cases'));

        const mail = screen.getByLabelText('Mail');

        expect(mail.getAttribute('aria-hidden')).toBe('true');
        expect(mail.hasAttribute('inert')).toBe(true);
        expect(within(mail).getByText(handedTheList)).toBeDefined();
        expect(within(mail).getByText(handedTheFolders)).toBeDefined();
        expect(within(mail).getByText(handedToMail)).toBeDefined();
    });

    // The assertion that says *not rebuilt from zero*: the same element, not an element drawing the same thing. A Mail
    // space taken down and put back reads the folder again from its leading end and puts the reader at the top of it,
    // and every node in it is a new node — so node identity is what tells the two apart, and it holds under the extra
    // mount `StrictMode` performs, which a count of mounts would not.
    it('gives back the Mail space that was left rather than a rebuilt one', () => {
        const { rerender } = render(inStrictMode('mail'));
        const before = screen.getByText(handedTheList);

        rerender(inStrictMode('cases'));
        rerender(inStrictMode('mail'));

        expect(screen.getByText(handedTheList)).toBe(before);
        expect(screen.getByRole('main', { name: 'Mail' })).toBeDefined();
    });

    // The two the frame composes for whichever space is in front. Handed to one of them and to nothing else: a second
    // live copy of the field would be a second place somebody's question could be typed into.
    it('hands the question and the connection to the space in front and to nothing behind it', () => {
        render(inStrictMode('cases'));

        expect(screen.getAllByText(handedTheIntent)).toHaveLength(1);
        expect(screen.getAllByText(handedTheStatus)).toHaveLength(1);
        expect(within(screen.getByRole('main', { name: 'Cases' })).getByText(handedTheIntent)).toBeDefined();
    });
});
