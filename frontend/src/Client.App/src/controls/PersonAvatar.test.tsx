// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { render } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { PersonAvatar } from './PersonAvatar';

const picture = 'data:image/png;base64,AA==';

describe('PersonAvatar', () => {
    it('draws the picture where the person has one', () => {
        const { container } = render(<PersonAvatar displayName="Ada Lovelace" picture={picture} place="menu" />);

        expect(container.querySelector('img')?.getAttribute('src')).toBe(picture);
    });

    it('falls back to the initials of the name where there is no picture', () => {
        const { container } = render(<PersonAvatar displayName="Ada Lovelace" picture={null} place="menu" />);

        expect(container.querySelector('img')).toBeNull();
        expect(container.textContent).toBe('AL');
    });

    it('draws the anonymous person while neither has answered', () => {
        const { container } = render(<PersonAvatar displayName={null} picture={null} place="menu" />);

        expect(container.textContent).toBe('');
        expect(container.querySelector('svg')).not.toBeNull();
    });

    it('says nothing of its own to a screen reader, the control around it carrying the name', () => {
        const { container } = render(<PersonAvatar displayName="Ada Lovelace" picture={picture} place="profile" />);

        expect(container.firstElementChild?.getAttribute('aria-hidden')).toBe('true');
        expect(container.querySelector('img')?.getAttribute('alt')).toBe('');
    });
});
