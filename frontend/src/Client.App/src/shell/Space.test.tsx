// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { StrictMode, type ReactNode } from 'react';
import { render, screen, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { ComposingContext } from '../composer/useComposing';
import { LocalizationProvider } from '../localization/Localization';
import { spaces, type Space as SpaceName } from '../routing/spaces';
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
    drafts: false,
    opening: null,
    compose: () => undefined,
    close: () => undefined,
};

function inStrictMode(space: SpaceName, offered: readonly SpaceName[] = spaces): ReactNode {
    return (
        <StrictMode>
            <LocalizationProvider>
                <WorkspaceProvider>
                    <ComposingContext value={nothingBeingWritten}>
                        <Space
                            offered={offered}
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

    // Both directions, because the region focused is the one the single ref is handed to and that ref moves between
    // regions that are all already mounted: a rule that only ever reached the space it was written for would pass on
    // the first move and leave focus behind on the second.
    it('puts focus at the start of the new content when the address changes', () => {
        const { rerender } = render(inStrictMode('discover'));

        rerender(inStrictMode('mail'));

        expect(document.activeElement).toBe(screen.getByRole('main', { name: 'Mail' }));

        rerender(inStrictMode('cases'));

        expect(document.activeElement).toBe(screen.getByRole('main', { name: 'Cases' }));
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

        expect(
            within(screen.getByRole('main', { name: 'Mail' })).queryByText(/This space is not built yet\./),
        ).toBeNull();
    });

    it('shows the pending note in a space nothing has been built for yet', () => {
        render(inStrictMode('cases'));

        expect(
            within(screen.getByRole('main', { name: 'Cases' })).getByText(/This space is not built yet\./),
        ).toBeDefined();
    });

    // The mechanism, stated as a count: a space the deployment offers is a space that is on the screen from the first
    // render, whichever one the address names.
    it('mounts every space the deployment offers, not only the one in front', () => {
        render(inStrictMode('cases', ['discover', 'mail', 'cases']));

        expect(document.querySelectorAll('main')).toHaveLength(3);
        expect(screen.getByLabelText('Discover')).toBeDefined();
        expect(screen.getByLabelText('Mail')).toBeDefined();
    });

    it('mounts no space the deployment does not offer', () => {
        render(inStrictMode('mail', ['mail']));

        expect(document.querySelectorAll('main')).toHaveLength(1);
        expect(screen.queryByLabelText('Cases')).toBeNull();
    });

    // What says a space is aside is what a reader would find: one landmark rather than seven, nothing in the others to
    // tab into, and no live region in one reaching somebody standing on another.
    it('leaves exactly the space in front reachable by the keyboard and by an accessible name', () => {
        render(inStrictMode('cases', ['discover', 'mail', 'cases']));

        expect(screen.getAllByRole('main')).toHaveLength(1);
        expect(screen.getByRole('main', { name: 'Cases' })).toBeDefined();

        for (const aside of ['Discover', 'Mail'] as const) {
            const region = screen.getByLabelText(aside);

            expect(region.getAttribute('aria-hidden')).toBe('true');
            expect(region.hasAttribute('inert')).toBe(true);
        }
    });

    it('keeps a space on the screen while another is in front of it, with everything it was handed still in it', () => {
        render(inStrictMode('cases'));

        const mail = screen.getByLabelText('Mail');

        expect(within(mail).getByText(handedTheList)).toBeDefined();
        expect(within(mail).getByText(handedTheFolders)).toBeDefined();
        expect(within(mail).getByText(handedToMail)).toBeDefined();
    });

    // The assertion that says *not rebuilt from zero*: the same element, not an element drawing the same thing. A space
    // taken down and put back reads again from its leading end and puts the reader at the top of it, and every node in
    // it is a new node — so node identity is what tells the two apart, and it holds under the extra mount `StrictMode`
    // performs, which a count of mounts would not.
    it('gives back the Mail space that was left rather than a rebuilt one', () => {
        const { rerender } = render(inStrictMode('mail'));
        const before = screen.getByText(handedTheList);

        rerender(inStrictMode('cases'));
        rerender(inStrictMode('mail'));

        expect(screen.getByText(handedTheList)).toBe(before);
        expect(screen.getByRole('main', { name: 'Mail' })).toBeDefined();
    });

    it('gives back the space that was left whichever space it is, not only Mail', () => {
        const { rerender } = render(inStrictMode('cases'));
        const before = screen.getByRole('heading', { name: 'Cases' });

        rerender(inStrictMode('mail'));
        rerender(inStrictMode('cases'));

        expect(screen.getByRole('heading', { name: 'Cases' })).toBe(before);
    });

    // The scroll offset is the platform's rather than this client's: it belongs to the scroller's box, so it survives
    // exactly as long as that box does. jsdom keeps what is written to `scrollTop` without laying anything out, so the
    // number here says the element was never rebuilt — a space put back would be a new element reading zero. That the
    // aside space also keeps its *box*, by standing aside with `visibility` rather than leaving the layout, is a
    // rendered-layout claim and is the browser suite's rather than this one's.
    it('keeps a space its scroll offset across a visit to another space and back', () => {
        const { rerender } = render(inStrictMode('cases'));
        const region = screen.getByLabelText('Cases');

        region.scrollTop = 120;
        rerender(inStrictMode('mail'));
        rerender(inStrictMode('cases'));

        expect(screen.getByLabelText('Cases')).toBe(region);
        expect(region.scrollTop).toBe(120);
    });

    // The two the frame composes for whichever space is in front. Handed to one of them and to nothing else: a second
    // live copy of the field would be a second place somebody's question could be typed into.
    it('hands the question and the connection to the space in front and to nothing behind it', () => {
        render(inStrictMode('cases'));

        expect(screen.getAllByText(handedTheIntent)).toHaveLength(1);
        expect(screen.getAllByText(handedTheStatus)).toHaveLength(1);
        expect(within(screen.getByRole('main', { name: 'Cases' })).getByText(handedTheIntent)).toBeDefined();
    });

    it('moves the question and the connection to the space arrived at rather than leaving them behind', () => {
        const { rerender } = render(inStrictMode('cases'));

        rerender(inStrictMode('mail'));

        expect(screen.getAllByText(handedTheIntent)).toHaveLength(1);
        expect(within(screen.getByRole('main', { name: 'Mail' })).getByText(handedTheIntent)).toBeDefined();
    });
});
