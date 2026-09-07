// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { sendToDeployment, type DeploymentTransport } from './sendToDeployment';

/**
 * How this run reaches its deployment: over the wire, or out of the fixture corpus where a development run asked for it.
 *
 * `pnpm dev:fixtures` is the whole of the ask — it runs Vite in a mode of its own, and nothing else in the workspace
 * uses that mode — so `pnpm dev` against a service somebody is running is unchanged, and so is every other way this
 * client is served. Both halves of the condition are constants a production build substitutes, so the branch folds
 * away and the dynamic import behind it is never emitted: example mail has no business in a bundle a deployment
 * publishes, and the browser suite asserts that rather than assuming it.
 */
export async function transportForThisRun(): Promise<DeploymentTransport> {
    if (!import.meta.env.DEV || import.meta.env.MODE !== 'fixtures') {
        return sendToDeployment;
    }

    const { fixtureDeployment } = await import('../development/fixtureDeployment');

    return fixtureDeployment();
}
