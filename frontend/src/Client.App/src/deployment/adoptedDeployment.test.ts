// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { afterEach, describe, expect, it, vi } from 'vitest';
import { adoptedDeployment, forgetDeployment, storeDeployment } from './adoptedDeployment';

// The document these tests run under is served from a loopback origin, which is an address this client may address —
// so the served-from case below is the one a web head is in, reached without anything being configured.
const servedFrom = window.location.origin;

describe('adoptedDeployment', () => {
    afterEach(() => {
        window.localStorage.clear();
        vi.unstubAllEnvs();
    });

    it('reads the deployment from the origin that served the client, with nothing configured and nobody asked', () => {
        expect(adoptedDeployment()).toEqual({ deployment: { baseAddress: servedFrom }, chosen: false });
    });

    it('reads the deployment an orchestration stated, rather than the server that served the page', () => {
        vi.stubEnv('VITE_MAILFATHOM_SERVICE_ADDRESS', 'https://mail.example.invalid');

        expect(adoptedDeployment()).toEqual({
            deployment: { baseAddress: 'https://mail.example.invalid' },
            chosen: false,
        });
    });

    it('reads back the deployment somebody named, so a later start opens against it', () => {
        storeDeployment({ baseAddress: 'https://mail.example.invalid' });

        expect(adoptedDeployment()).toEqual({
            deployment: { baseAddress: 'https://mail.example.invalid' },
            chosen: true,
        });
    });

    it('keeps the clear-text address somebody declared, rather than refusing what it wrote itself', () => {
        storeDeployment({ baseAddress: 'http://mail.example.invalid' });

        expect(adoptedDeployment()).toEqual({
            deployment: { baseAddress: 'http://mail.example.invalid' },
            chosen: true,
        });
    });

    it('goes back to the origin that served the client when the named deployment is forgotten', () => {
        storeDeployment({ baseAddress: 'https://mail.example.invalid' });

        forgetDeployment();

        expect(adoptedDeployment()).toEqual({ deployment: { baseAddress: servedFrom }, chosen: false });
    });

    it('ignores a stored value that is no longer an address at all', () => {
        window.localStorage.setItem('mailfathom.deployment', 'not an address');

        expect(adoptedDeployment()).toEqual({ deployment: { baseAddress: servedFrom }, chosen: false });
    });
});
