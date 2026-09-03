// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { describe, expect, it } from 'vitest';
import type { DeploymentSession } from '@mailfathom/client-backend';
import { telemetryForwardedBy } from './telemetryForwarding';

const address = 'https://mail.example.invalid';

function answering(telemetryForwarded: boolean): DeploymentSession {
    return { version: '0.8.7', permissions: [], telemetryForwarded };
}

describe('telemetryForwardedBy', () => {
    it('names the deployment somebody is signed in to, where it forwards telemetry', () => {
        expect(telemetryForwardedBy(answering(true), address)).toEqual({ answered: true, destination: address });
    });

    it('answers that a deployment forwarding none has nothing behind the switch', () => {
        expect(telemetryForwardedBy(answering(false), address)).toEqual({ answered: true, destination: null });
    });

    // The distinction the screen exists to draw: a deployment that has said nothing has not said no, and the frame
    // records under the person's own answer meanwhile — so a screen told these two apart says nothing untrue.
    it('answers nothing either way while no session has been read', () => {
        expect(telemetryForwardedBy(null, address)).toEqual({ answered: false });
    });

    it('answers nothing either way where there is no deployment to have answered', () => {
        expect(telemetryForwardedBy(answering(true), null)).toEqual({ answered: false });
    });
});
