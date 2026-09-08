// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { afterEach, describe, expect, it, vi } from 'vitest';
import type { ClientRequest, ClientResponse } from '@mailfathom/client-backend';
import * as changes from '../../../../tests/fixtures/changes';
import * as deployment from '../../../../tests/fixtures/deployment';
import * as drafts from '../../../../tests/fixtures/drafts';
import * as mail from '../../../../tests/fixtures/mail';
import * as messages from '../../../../tests/fixtures/messages';
import * as notifications from '../../../../tests/fixtures/notifications';
import {
    fixtureAnswer,
    fixtureDeployment,
    fixtureDeploymentDefaults,
    fixtureDeploymentState,
    type FixtureDeploymentOptions,
} from './fixtureDeployment';

const deploymentAddress = 'https://mailfathom.invalid';

function asking(route: string, method: ClientRequest['method'] = 'GET', body?: string): ClientRequest {
    return {
        method,
        path: `${deploymentAddress}/api/client${route}`,
        headers: { Accept: 'application/json', Authorization: 'Basic dXNlcjpvcGVuIHNlc2FtZQ==' },
        ...(body === undefined ? {} : { body }),
    };
}

function answered(
    route: string,
    options: Partial<FixtureDeploymentOptions> = {},
    draw = 1,
    method: ClientRequest['method'] = 'GET',
    body?: string,
): ClientResponse {
    const answer = fixtureAnswer(
        asking(route, method, body),
        { ...fixtureDeploymentDefaults, ...options },
        draw,
        fixtureDeploymentState(),
    );

    if (answer === null) {
        throw new Error(`The fixture deployment answered nothing for ${route}.`);
    }

    return answer;
}

function posted(route: string): ClientResponse {
    return answered(route, {}, 1, 'POST', '{}');
}

function stated(answer: ClientResponse): Record<string, unknown> {
    return JSON.parse(answer.body) as Record<string, unknown>;
}

const readRoutes: readonly (readonly [string, unknown])[] = [
    ['/folders', deployment.troubledFolders],
    [`/threads/${mail.conversationId}`, mail.conversation],
    ['/emails/search?text=renewal', mail.searchResults],
    ['/notifications?pageSize=20', notifications.notificationPage],
    ['/notifications/unread-count', notifications.unreadNotificationCount],
    ['/mutations', changes.mutationRecords],
];

const writtenRoutes: readonly (readonly [string, unknown])[] = [
    ['/mutations/flags', changes.flagsRecorded],
    ['/mutations/moves', changes.movesPartlyRecorded],
    ['/signals/ticket', changes.signalTicket],
    ['/notifications/read', notifications.everyNotificationMarkedRead],
    [`/notifications/${notifications.notificationId}/read-state`, notifications.notificationMarkedRead],
    ['/drafts', drafts.savedDraft],
    [`/drafts/${drafts.draftId}/send`, drafts.queuedSend],
];

afterEach(() => {
    vi.useRealTimers();
    delete window.mailfathomFixtures;
});

describe('fixtureAnswer', () => {
    it('answers the folder a reader is looking at with the page the cursor names', () => {
        const page = stated(answered('/emails?direction=forward&cursor=200&pageSize=100'));

        expect(page['emails']).toHaveLength(100);
        expect(page['previousCursor']).toBe('200');
    });

    it('answers a message with the identity that was asked for rather than the corpus own', () => {
        const message = stated(answered('/messages/00000000-0000-4000-8000-0000000000c3'));

        expect(message['storedEmailId']).toBe('00000000-0000-4000-8000-0000000000c3');
    });

    it('challenges as MailFathom where nothing carried a credential, so a password may be typed', () => {
        const request: ClientRequest = { method: 'GET', path: `${deploymentAddress}/api/client/session`, headers: {} };
        const answer = fixtureAnswer(request, fixtureDeploymentDefaults, 1, fixtureDeploymentState());

        expect(answer?.status).toBe(401);
        expect(answer?.headers['www-authenticate']).toContain('realm="MailFathom"');
    });

    it('reports what it holds rather than what the corpus states once a preference has been written', () => {
        const state = fixtureDeploymentState();
        const written = JSON.stringify({ theme: 'dark' });

        fixtureAnswer(asking('/preferences', 'POST', written), fixtureDeploymentDefaults, 1, state);

        const held = fixtureAnswer(asking('/preferences'), fixtureDeploymentDefaults, 1, state);

        expect(held === null ? null : stated(held)['theme']).toBe('dark');
    });

    it('answers a route the corpus states nothing for as nothing being there', () => {
        expect(answered('/cases').status).toBe(404);
    });

    it.each(readRoutes)('answers %s with what the corpus says a deployment holds', (route, held) => {
        expect(stated(answered(route))).toStrictEqual(held);
    });

    it.each(writtenRoutes)('answers a write to %s with what the corpus says it becomes', (route, became) => {
        expect(stated(posted(route))).toStrictEqual(became);
    });

    it('answers a withdrawn message as the deployment having accepted the withdrawal', () => {
        expect(stated(posted('/outbox/cancellation'))['outcome']).toBe('Accepted');
    });

    it('answers a discarded draft with nothing to read', () => {
        expect(answered(`/drafts/${drafts.draftId}`, {}, 1, 'DELETE').status).toBe(204);
    });

    it('answers the body of a sender who wrote no text part with what MailFathom derived from the markup', () => {
        expect(stated(answered(`/messages/${messages.markupOnlyId}/body`))).toStrictEqual(messages.markupOnlyBody);
    });

    it("carries the sender's own markup into a body only where the reader asked for it", () => {
        const withheld = stated(answered(`/messages/${messages.newsletterId}/body`));
        const asked = stated(answered(`/messages/${messages.newsletterId}/body?fullHtml=true`));

        expect(withheld['selfContainedHtml']).toBeNull();
        expect(asked['selfContainedHtml']).not.toBeNull();
    });

    it('answers every collection empty where the options ask for it', () => {
        const empty = { emptyCollections: true };

        expect(stated(answered('/emails?direction=forward', empty))['emails']).toStrictEqual([]);
        expect(stated(answered('/emails/search?text=renewal', empty))['results']).toStrictEqual([]);
        expect(stated(answered('/notifications?pageSize=20', empty))['notifications']).toStrictEqual([]);
    });

    it('leaves the mailboxes populated where the collections are asked to be empty', () => {
        expect(stated(answered('/accounts', { emptyCollections: true }))['accounts']).not.toStrictEqual([]);
    });

    it('refuses every read while the session is expired, so the client asks for the password again', () => {
        expect(answered('/accounts', { expiredSession: true }).status).toBe(401);
    });

    it('refuses signing in while the session is expired, which is what makes it a state rather than one refusal', () => {
        expect(answered('/session', { expiredSession: true }).status).toBe(401);
    });

    it('exchanges a credential for a session, which is what a run against the corpus signs in with', () => {
        const minted = stated(answered('/session/token', {}, 1, 'POST'));

        expect(minted['token']).toMatch(/^mfs_/);
        expect(Number.isNaN(Date.parse(String(minted['expiresAt'])))).toBe(false);
    });

    it('challenges the exchange where nothing carried a credential, so a password may be typed', () => {
        const request: ClientRequest = {
            method: 'POST',
            path: `${deploymentAddress}/api/client/session/token`,
            headers: {},
        };
        const answer = fixtureAnswer(request, fixtureDeploymentDefaults, 1, fixtureDeploymentState());

        expect(answer?.status).toBe(401);
        expect(answer?.headers['www-authenticate']).toContain('realm="MailFathom"');
    });

    it('ends a session it is asked to end, so signing out reaches something that answers', () => {
        expect(answered('/session/token/revocation', {}, 1, 'POST').status).toBe(204);
    });

    it('fails a request whose drawn value falls under the failure rate', () => {
        expect(answered('/accounts', { failureRate: 0.5 }, 0.49).status).toBe(503);
    });

    it('answers a request whose drawn value does not fall under the failure rate', () => {
        expect(answered('/accounts', { failureRate: 0.5 }, 0.5).status).toBe(200);
    });

    it('answers nothing at all while the deployment is unreachable', () => {
        const answer = fixtureAnswer(
            asking('/accounts'),
            { ...fixtureDeploymentDefaults, unreachable: true },
            1,
            fixtureDeploymentState(),
        );

        expect(answer).toBeNull();
    });
});

describe('fixtureDeployment', () => {
    it('publishes the options where they can be changed while the client is running', () => {
        fixtureDeployment();

        expect(window.mailfathomFixtures).toStrictEqual(fixtureDeploymentDefaults);
    });

    it('reads the options afresh on every request, so a change takes effect without a reload', async () => {
        const transport = fixtureDeployment(() => 1)(new AbortController().signal);

        window.mailfathomFixtures = { ...fixtureDeploymentDefaults, expiredSession: true };

        await expect(transport(asking('/accounts'))).resolves.toMatchObject({ status: 401 });
    });

    it('holds an answer back for as long as the latency asks it to', async () => {
        vi.useFakeTimers();

        const transport = fixtureDeployment(() => 1)(new AbortController().signal);

        window.mailfathomFixtures = { ...fixtureDeploymentDefaults, latency: 500 };

        const answering = transport(asking('/accounts'));
        let answeredYet = false;

        void answering.then(() => {
            answeredYet = true;
        });

        await vi.advanceTimersByTimeAsync(499);
        expect(answeredYet).toBe(false);

        await vi.advanceTimersByTimeAsync(1);
        await expect(answering).resolves.toMatchObject({ status: 200 });
    });

    it('gives up on a request the screen that started it abandoned', async () => {
        vi.useFakeTimers();

        const abandoning = new AbortController();
        const transport = fixtureDeployment(() => 1)(abandoning.signal);

        window.mailfathomFixtures = { ...fixtureDeploymentDefaults, latency: 500 };

        const answering = transport(asking('/accounts'));

        abandoning.abort();

        await expect(answering).rejects.toThrow('abandoned');
    });
});
