// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { afterEach, describe, expect, it, vi } from 'vitest';
import { configuredNothing, type ConfiguredConnection } from '../shellOperations/configuredConnection';
import { adoptedDeployment, forgetDeployment, storeDeployment } from './adoptedDeployment';

// The document these tests run under is served from a loopback origin, which is an address this client may address —
// so the served-from case below is the one a web head is in, reached without anything being configured.
const servedFrom = window.location.origin;

function configured(stated: Partial<ConfiguredConnection>): ConfiguredConnection {
    return { ...configuredNothing, ...stated };
}

describe('adoptedDeployment', () => {
    afterEach(() => {
        window.localStorage.clear();
        vi.unstubAllEnvs();
    });

    it('reads the deployment from the origin that served the client, with nothing configured and nobody asked', () => {
        expect(adoptedDeployment(configuredNothing)).toEqual({
            outcome: 'resolved',
            adopted: { deployment: { baseAddress: servedFrom }, origin: 'serving' },
            clearTextPermitted: null,
        });
    });

    it('reads the deployment an orchestration stated, rather than the server that served the page', () => {
        vi.stubEnv('VITE_MAILFATHOM_SERVICE_ADDRESS', 'https://mail.example.invalid');

        expect(adoptedDeployment(configuredNothing)).toEqual({
            outcome: 'resolved',
            adopted: { deployment: { baseAddress: 'https://mail.example.invalid' }, origin: 'serving' },
            clearTextPermitted: null,
        });
    });

    it('reads back the deployment somebody named, so a later start opens against it', () => {
        storeDeployment({ baseAddress: 'https://mail.example.invalid' });

        expect(adoptedDeployment(configuredNothing)).toEqual({
            outcome: 'resolved',
            adopted: { deployment: { baseAddress: 'https://mail.example.invalid' }, origin: 'chosen' },
            clearTextPermitted: null,
        });
    });

    it('keeps the clear-text address somebody declared, rather than refusing what it wrote itself', () => {
        storeDeployment({ baseAddress: 'http://mail.example.invalid' });

        expect(adoptedDeployment(configuredNothing)).toEqual({
            outcome: 'resolved',
            adopted: { deployment: { baseAddress: 'http://mail.example.invalid' }, origin: 'chosen' },
            clearTextPermitted: null,
        });
    });

    it('goes back to the origin that served the client when the named deployment is forgotten', () => {
        storeDeployment({ baseAddress: 'https://mail.example.invalid' });

        forgetDeployment();

        expect(adoptedDeployment(configuredNothing)).toEqual({
            outcome: 'resolved',
            adopted: { deployment: { baseAddress: servedFrom }, origin: 'serving' },
            clearTextPermitted: null,
        });
    });

    it('ignores a stored value that is no longer an address at all', () => {
        window.localStorage.setItem('mailfathom.deployment', 'not an address');

        expect(adoptedDeployment(configuredNothing)).toEqual({
            outcome: 'resolved',
            adopted: { deployment: { baseAddress: servedFrom }, origin: 'serving' },
            clearTextPermitted: null,
        });
    });

    it('takes the address a deployment configured, and says that is where it came from', () => {
        expect(adoptedDeployment(configured({ serviceAddress: 'mail.example.invalid:8443' }))).toEqual({
            outcome: 'resolved',
            adopted: { deployment: { baseAddress: 'https://mail.example.invalid:8443' }, origin: 'configured' },
            clearTextPermitted: null,
        });
    });

    // The point of configuring a client is that whoever installed it decides where it points, so a stored address from
    // an earlier run is not allowed to shadow one — and removing the setting hands the machine back to what it had.
    it('takes what a deployment configured over the address somebody named on this machine', () => {
        storeDeployment({ baseAddress: 'https://named.example.invalid' });

        expect(adoptedDeployment(configured({ serviceAddress: 'configured.example.invalid' }))).toEqual({
            outcome: 'resolved',
            adopted: { deployment: { baseAddress: 'https://configured.example.invalid' }, origin: 'configured' },
            clearTextPermitted: null,
        });
    });

    it('takes an unsecured configured address where the same configuration permitted one', () => {
        expect(
            adoptedDeployment(configured({ serviceAddress: 'mail.example.invalid', permitClearText: 'true' })),
        ).toEqual({
            outcome: 'resolved',
            adopted: { deployment: { baseAddress: 'http://mail.example.invalid' }, origin: 'configured' },
            clearTextPermitted: true,
        });
    });

    it('carries a configured permission through where configuration named no address at all', () => {
        expect(adoptedDeployment(configured({ permitClearText: 'false' }))).toEqual({
            outcome: 'resolved',
            adopted: { deployment: { baseAddress: servedFrom }, origin: 'serving' },
            clearTextPermitted: false,
        });
    });

    it('refuses a configured address that is not an address, rather than asking for one as if none was given', () => {
        expect(adoptedDeployment(configured({ serviceAddress: 'https://mail.example.invalid/inbox?a=1' }))).toEqual({
            outcome: 'refused',
            refusal: 'addressMalformed',
        });
    });

    it('refuses a configured clear-text address that nothing permitted', () => {
        expect(adoptedDeployment(configured({ serviceAddress: 'http://mail.example.invalid' }))).toEqual({
            outcome: 'refused',
            refusal: 'addressNeedsClearTextPermission',
        });
    });

    // Correcting either half would be this client deciding on somebody's behalf whether a password crosses a network
    // in the clear, which is the one thing it may not decide quietly.
    it('refuses a permission granted beside an address that names TLS, rather than silently correcting either', () => {
        expect(
            adoptedDeployment(configured({ serviceAddress: 'https://mail.example.invalid', permitClearText: 'true' })),
        ).toEqual({ outcome: 'refused', refusal: 'clearTextContradictsAddress' });
    });

    it.each(['yes', '1', 'on', 'TRUE!'])(
        'refuses a permission written as %s, which is not true or false',
        (written) => {
            expect(adoptedDeployment(configured({ permitClearText: written }))).toEqual({
                outcome: 'refused',
                refusal: 'permissionNotABoolean',
            });
        },
    );

    it.each(['TRUE', 'False'])(
        'reads a permission written as %s, case being nothing an operator should think about',
        (written) => {
            const resolved = adoptedDeployment(configured({ permitClearText: written }));

            expect(resolved).toEqual({
                outcome: 'resolved',
                adopted: { deployment: { baseAddress: servedFrom }, origin: 'serving' },
                clearTextPermitted: written.toLowerCase() === 'true',
            });
        },
    );
});
