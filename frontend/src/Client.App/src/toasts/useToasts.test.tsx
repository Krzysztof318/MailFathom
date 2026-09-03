// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { render } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { useToasts } from './useToasts';

function Probe() {
    const toasts = useToasts();

    toasts.raise({ kind: 'neutral', title: 'Three threads archived' });

    return null;
}

describe('useToasts', () => {
    // Answering with a surface that drops what it is handed would be the worse failure: every screen would go on
    // reporting outcomes into nothing, and the defect would show up as mail that quietly seems not to have been sent
    // rather than as anything a test or a person could see.
    it('refuses to answer a component mounted outside the provider', () => {
        expect(() => render(<Probe />)).toThrow(/outside the ToastsProvider/);
    });
});
