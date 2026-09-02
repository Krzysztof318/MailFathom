// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { render } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { useLocalization } from './useLocalization';

function Probe() {
    const { translate } = useLocalization();

    return <p>{translate('accounts.reading')}</p>;
}

describe('useLocalization', () => {
    // A silent English fallback here would be the wrong answer: a screen mounted outside the provider would read
    // correctly in English and silently refuse to change language, which is the defect that reaches a Polish user and
    // nobody else.
    it('refuses to answer a component mounted outside the provider', () => {
        expect(() => render(<Probe />)).toThrow(/outside the LocalizationProvider/);
    });
});
