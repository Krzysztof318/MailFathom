// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { LocalizationProvider } from '../localization/Localization';
import { VersionLine } from './VersionLine';

function renderLine(deploymentVersion: string | null): void {
    render(
        <LocalizationProvider>
            <VersionLine deploymentVersion={deploymentVersion} />
        </LocalizationProvider>,
    );
}

describe('VersionLine', () => {
    it('names the product in front of the versions, which is what makes a number readable', () => {
        renderLine(null);

        expect(screen.getByText(/^MailFathom /u)).toBeDefined();
    });

    it('says what the client and the deployment are running once the deployment has answered', () => {
        renderLine('0.9.0');

        expect(screen.getByText(`MailFathom Client ${__MAILFATHOM_VERSION__}, deployment 0.9.0`)).toBeDefined();
    });

    it('says what the client alone is running while nothing has answered, that being all this machine knows', () => {
        renderLine(null);

        expect(screen.getByText(`MailFathom Client ${__MAILFATHOM_VERSION__}`)).toBeDefined();
    });
});
