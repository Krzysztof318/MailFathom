// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { renderHook, waitFor } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import {
    defaultTelemetryLevel,
    type ClientResponse,
    type ClientSession,
    type DeploymentSession,
    type MailFathomTransport,
} from '@mailfathom/client-backend';
import { useDraftsReplies } from './useDraftsReplies';

const session: ClientSession = { baseAddress: 'https://mail.example.invalid', authorization: 'Basic YWRh' };

function offering(...permissions: DeploymentSession['permissions']): DeploymentSession {
    return { version: '0.8.7', permissions, telemetryLevel: defaultTelemetryLevel };
}

const drafts: ClientResponse = { status: 200, body: JSON.stringify({ draftsReplies: true }), headers: {} };
const notAnswering: ClientResponse = { status: 503, body: '', headers: {} };

/** A deployment answering the drafting route with each answer in turn, and counting how often it was asked. */
function answeringInTurn(...answers: readonly ClientResponse[]): {
    transport: MailFathomTransport;
    asked: () => number;
} {
    let asked = 0;

    return {
        transport: () => {
            const answer = answers[Math.min(asked, answers.length - 1)] ?? notAnswering;

            asked += 1;

            return Promise.resolve(answer);
        },
        asked: () => asked,
    };
}

describe('useDraftsReplies', () => {
    it('reads the drafting route again once the session is, where the first read failed', async () => {
        const deployment = answeringInTurn(notAnswering, drafts);
        const view = renderHook(
            ({ offered }: { offered: DeploymentSession }) => useDraftsReplies(session, offered, deployment.transport),
            { initialProps: { offered: offering('mailfathom.mail.ask') } },
        );

        await waitFor(() => {
            expect(deployment.asked()).toBe(1);
        });
        expect(view.result.current).toBe(false);

        view.rerender({ offered: offering('mailfathom.mail.ask') });

        await waitFor(() => {
            expect(view.result.current).toBe(true);
        });
    });
});
