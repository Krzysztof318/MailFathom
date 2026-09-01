// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { StrictMode, type ReactNode } from 'react';
import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { LocalizationProvider } from '../localization/Localization';
import type { Space as SpaceName } from '../routing/spaces';
import { Space } from './Space';

// Not catalogue entries: each stands for whatever the frame composes for Mail, which is the point of the two props.
const handedToMail = 'The mail this space was handed.';
const handedTheFolders = 'The folder tree this space was handed.';

// Every case here renders under `StrictMode`, which is what `main.tsx` mounts and what makes React invoke an effect
// twice on the first mount. A focus rule written against "has this effect run before" passes without it and moves
// focus on landing with it, so the wrapper is the point of the test rather than a detail of the harness.
function inStrictMode(space: SpaceName): ReactNode {
    return (
        <StrictMode>
            <LocalizationProvider>
                <Space space={space} folders={<p>{handedTheFolders}</p>} mail={<p>{handedToMail}</p>} />
            </LocalizationProvider>
        </StrictMode>
    );
}

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
    });

    it('shows what the frame composed for Mail in the Mail space', () => {
        render(inStrictMode('mail'));

        expect(screen.getByText(handedToMail)).toBeDefined();
    });

    it('shows the scope the Mail space is drawn against beside what it is drawn from', () => {
        render(inStrictMode('mail'));

        expect(screen.getByText(handedTheFolders)).toBeDefined();
    });

    it('shows the pending note rather than the mail in a space nothing has been built for yet', () => {
        render(inStrictMode('cases'));

        expect(screen.queryByText(handedToMail)).toBeNull();
        expect(screen.queryByText(handedTheFolders)).toBeNull();
        expect(screen.getByText(/This space is not built yet\./)).toBeDefined();
    });
});
