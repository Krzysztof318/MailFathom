// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { Icon } from './Icon';

describe('Icon', () => {
    it('is drawn out of the accessibility tree, because the control around it carries the name', () => {
        const controlName = 'Discard the draft';

        render(
            <button type="button" aria-label={controlName}>
                <Icon name="close" />
            </button>,
        );

        expect(screen.getByRole('button', { name: controlName })).toBeDefined();
    });
});
