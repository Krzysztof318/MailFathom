// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { afterEach, describe, expect, it, vi } from 'vitest';
import { sendToDeployment } from './sendToDeployment';
import { transportForThisRun } from './transportForThisRun';

afterEach(() => {
    vi.unstubAllEnvs();
    delete window.mailfathomFixtures;
});

describe('transportForThisRun', () => {
    it('reaches the deployment somebody is running where a development run asked for nothing else', async () => {
        vi.stubEnv('DEV', true);
        vi.stubEnv('MODE', 'development');

        await expect(transportForThisRun()).resolves.toBe(sendToDeployment);
    });

    it('answers out of the fixture corpus where a development run asked for it by name', async () => {
        vi.stubEnv('DEV', true);
        vi.stubEnv('MODE', 'fixtures');

        const send = await transportForThisRun();

        expect(send).not.toBe(sendToDeployment);
        expect(window.mailfathomFixtures).toBeDefined();
    });

    it('reaches the deployment in what a build publishes, whatever mode that build was given', async () => {
        vi.stubEnv('DEV', false);
        vi.stubEnv('MODE', 'fixtures');

        await expect(transportForThisRun()).resolves.toBe(sendToDeployment);
        expect(window.mailfathomFixtures).toBeUndefined();
    });
});
