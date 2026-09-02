// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { StrictMode, type ReactNode } from 'react';
import { render, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { LocalizationProvider } from '../localization/Localization';
import type { Space as SpaceName } from '../routing/spaces';
import { WorkspaceProvider } from '../workspace/Workspace';
import { Space } from './Space';

// Not catalogue entries: each stands for whatever the frame composes for Mail, which is the point of the three props.
const handedToMail = 'The mail this space was handed.';
const handedTheFolders = 'The folder tree this space was handed.';
const handedTheList = 'The message list this space was handed.';
const handedTheIntent = 'The question this space was handed.';
const handedTheStatus = 'The connection this space was handed.';

// Every case here renders under `StrictMode`, which is what `main.tsx` mounts and what makes React invoke an effect
// twice on the first mount. A focus rule written against "has this effect run before" passes without it and moves
// focus on landing with it, so the wrapper is the point of the test rather than a detail of the harness.
function inStrictMode(space: SpaceName): ReactNode {
    return (
        <StrictMode>
            <LocalizationProvider>
                <WorkspaceProvider>
                    <Space
                        space={space}
                        intent={<p>{handedTheIntent}</p>}
                        status={<p>{handedTheStatus}</p>}
                        folders={<p>{handedTheFolders}</p>}
                        list={<p>{handedTheList}</p>}
                        mail={<p>{handedToMail}</p>}
                    />
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

    it('shows the pending note rather than the mail in a space nothing has been built for yet', () => {
        render(inStrictMode('cases'));

        expect(screen.queryByText(handedToMail)).toBeNull();
        expect(screen.queryByText(handedTheFolders)).toBeNull();
        expect(screen.queryByText(handedTheList)).toBeNull();
        expect(screen.getByText(/This space is not built yet\./)).toBeDefined();
    });
});
