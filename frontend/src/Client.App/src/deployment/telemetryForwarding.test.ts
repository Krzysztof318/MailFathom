// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { describe, expect, it } from 'vitest';
import type { DeploymentSession, DeploymentTelemetryLevel } from '@mailfathom/client-backend';
import { telemetryForwardedBy } from './telemetryForwarding';

const address = 'https://mail.example.invalid';

function answering(telemetryLevel: DeploymentTelemetryLevel): DeploymentSession {
    return { version: '0.8.7', permissions: [], telemetryLevel };
}

describe('telemetryForwardedBy', () => {
    it('names the deployment somebody is signed in to, where it forwards telemetry', () => {
        expect(telemetryForwardedBy(answering('info'), address)).toEqual({ answered: true, destination: address });
    });

    it('answers that a deployment forwarding none has nothing behind the switch', () => {
        expect(telemetryForwardedBy(answering('off'), address)).toEqual({ answered: true, destination: null });
    });

    // How much a deployment asks for is not what this screen draws: the records go to the same place whether it wants
    // the whole stream or only what it would act on, and somebody reading the switch is being told where.
    it('names the deployment whatever level it asked for', () => {
        expect(telemetryForwardedBy(answering('trace'), address)).toEqual({ answered: true, destination: address });
        expect(telemetryForwardedBy(answering('fatal'), address)).toEqual({ answered: true, destination: address });
    });

    // The distinction the screen exists to draw: a deployment that has said nothing has not said no, and the frame
    // records under the person's own answer meanwhile — so a screen told these two apart says nothing untrue.
    it('answers nothing either way while no session has been read', () => {
        expect(telemetryForwardedBy(null, address)).toEqual({ answered: false });
    });

    it('answers nothing either way where there is no deployment to have answered', () => {
        expect(telemetryForwardedBy(answering('info'), null)).toEqual({ answered: false });
    });
});
