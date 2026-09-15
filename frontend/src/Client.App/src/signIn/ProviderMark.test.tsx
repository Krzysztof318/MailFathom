// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { render } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { ProviderMark } from './ProviderMark';

describe('ProviderMark', () => {
    it('draws the committed mark in the brand colour declared for that provider', () => {
        const { container } = render(<ProviderMark name="github" className="size-4.25" />);
        const drawn = container.querySelector('svg');

        expect(drawn?.querySelector('path')?.getAttribute('d')?.length).toBeGreaterThan(0);
        expect(drawn?.style.fill).toBe('var(--color-provider-github, currentColor)');
    });

    // An operator names their own authorization servers and nothing says one of them is a brand anybody drew, so the
    // screen falls back to the symbol the client already has for a credential rather than drawing nothing.
    it('draws the credential symbol in the text colour for a provider the bundle carries no mark for', () => {
        const { container } = render(<ProviderMark name="our-own-idp" className="size-4.25" />);
        const drawn = container.querySelector('svg');

        expect(drawn).not.toBeNull();
        expect(drawn?.style.fill).toBe('');
    });
});
