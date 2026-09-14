// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { act, renderHook } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { ClientRequest, ClientResponse, MailFathomTransport } from '@mailfathom/client-backend';
import { grantRenewalMargin, type OAuthGrant } from './oauthGrant';
import { grantRenewalCheckInterval, useGrantRenewal } from './useGrantRenewal';

// The clock is fake throughout, for the reason `useSessionRenewal.test.ts` gives: what this hook is is a reading of the
// clock, and nothing here waits out a margin measured in minutes.

const now = new Date('2026-09-14T09:00:00+00:00');
const issuer = 'https://id.example.invalid';

const discovered = JSON.stringify({
    issuer,
    authorization_endpoint: `${issuer}/authorize`,
    token_endpoint: `${issuer}/token`,
});

/** A grant whose access token runs out in however long a test says, which is what decides a renewal is due. */
function endingIn(milliseconds: number, refreshToken: string | null = 'a-refresh-token'): OAuthGrant {
    return {
        authorization: 'Bearer a-token',
        expiresAt: new Date(now.getTime() + milliseconds).toISOString(),
        refreshToken,
        issuer,
        clientId: 'mailfathom-client',
        resource: 'https://mail.example.invalid',
        person: 'Ada',
    };
}

function answering(token: Partial<ClientResponse>): { transport: MailFathomTransport; asked: ClientRequest[] } {
    const asked: ClientRequest[] = [];

    return {
        asked,
        transport: (request) => {
            asked.push(request);

            return Promise.resolve(
                request.path.endsWith('/token')
                    ? { status: 200, body: '', headers: {}, ...token }
                    : { status: 200, body: discovered, headers: {} },
            );
        },
    };
}

function issuedBody(refreshToken: string | null = 'rotated'): string {
    return JSON.stringify({
        access_token: 'a-fresh-token',
        token_type: 'Bearer',
        expires_in: 3600,
        ...(refreshToken === null ? {} : { refresh_token: refreshToken }),
    });
}

/** Drives the hook the way the frame does, replacing what is held as the frame would when a renewal answers. */
function driven(
    grant: OAuthGrant | null,
    transport: MailFathomTransport,
    online = true,
): {
    renewed: OAuthGrant[];
    ended: () => number;
    hold: (held: OAuthGrant | null) => void;
    settle: () => Promise<void>;
    tick: () => Promise<void>;
} {
    const renewed: OAuthGrant[] = [];
    let ended = 0;

    const rendered = renderHook(
        ({ held }: { held: OAuthGrant | null }) => {
            useGrantRenewal(
                held,
                transport,
                online,
                (answer) => {
                    renewed.push(answer);
                },
                () => {
                    ended += 1;
                },
            );
        },
        { initialProps: { held: grant } },
    );

    return {
        renewed,
        ended: () => ended,
        hold: (held) => {
            rendered.rerender({ held });
        },
        settle: async () => {
            await act(async () => {
                await vi.advanceTimersByTimeAsync(0);
            });
        },
        tick: async () => {
            await act(async () => {
                await vi.advanceTimersByTimeAsync(grantRenewalCheckInterval);
            });
        },
    };
}

describe('useGrantRenewal', () => {
    beforeEach(() => {
        vi.useFakeTimers();
        vi.setSystemTime(now);
    });

    afterEach(() => {
        vi.useRealTimers();
    });

    it('leaves a grant alone while its access token has longer left than the margin', async () => {
        const { transport, asked } = answering({ body: issuedBody() });
        const running = driven(endingIn(grantRenewalMargin * 4), transport);

        await running.tick();

        expect(asked).toEqual([]);
    });

    // Due on the first reading rather than on the first tick, which is what a machine that slept across the expiry
    // comes back to.
    it('renews a grant already inside the margin without waiting for a tick', async () => {
        const { transport, asked } = answering({ body: issuedBody() });
        const running = driven(endingIn(grantRenewalMargin / 2), transport);

        await running.settle();

        expect(asked.some((request) => request.path === `${issuer}/token`)).toBe(true);
        expect(running.renewed[0]?.authorization).toBe('Bearer a-fresh-token');
        expect(running.renewed[0]?.refreshToken).toBe('rotated');
        expect(running.renewed[0]?.person).toBe('Ada');
    });

    // Presenting a rotated refresh token twice is what a server rotating them reads as a stolen one, so a renewal in
    // flight is the only one there is until it answers.
    it('presents a refresh token once, however many ticks pass while the answer is on the wire', async () => {
        const pending: { settle?: (response: ClientResponse) => void } = {};
        const asked: ClientRequest[] = [];

        const running = driven(endingIn(0), (request) => {
            asked.push(request);

            return request.path.endsWith('/token')
                ? new Promise<ClientResponse>((settle) => {
                      pending.settle = settle;
                  })
                : Promise.resolve({ status: 200, body: discovered, headers: {} });
        });

        await running.settle();
        await running.tick();
        await running.tick();

        expect(asked.filter((request) => request.path === `${issuer}/token`)).toHaveLength(1);

        pending.settle?.({ status: 200, body: issuedBody(), headers: {} });
    });

    // A server that refuses the refresh token has ended the sign-in in a way no retry undoes.
    it('reports a refused refresh token as the grant having ended', async () => {
        const { transport } = answering({ status: 400, body: '{"error":"invalid_grant"}' });
        const running = driven(endingIn(0), transport);

        await running.settle();

        expect(running.ended()).toBe(1);
        expect(running.renewed).toEqual([]);
    });

    it('holds on to what it has where nothing answered, and tries again on the next tick', async () => {
        const asked: ClientRequest[] = [];
        let reachable = false;

        const running = driven(endingIn(0), (request) => {
            asked.push(request);

            return reachable
                ? Promise.resolve({
                      status: 200,
                      body: request.path.endsWith('/token') ? issuedBody() : discovered,
                      headers: {},
                  })
                : Promise.reject(new TypeError('no route'));
        });

        await running.settle();

        expect(running.ended()).toBe(0);
        expect(running.renewed).toEqual([]);

        reachable = true;
        await running.tick();

        expect(running.renewed[0]?.authorization).toBe('Bearer a-fresh-token');
    });

    it('attempts nothing for a grant with no refresh token to present, rather than retrying forever', async () => {
        const { transport, asked } = answering({ body: issuedBody() });
        const running = driven(endingIn(0, null), transport);

        await running.settle();
        await running.tick();

        expect(asked).toEqual([]);
        expect(running.ended()).toBe(0);
    });

    it('attempts nothing while this machine has no network to deliver a renewal over', async () => {
        const { transport, asked } = answering({ body: issuedBody() });
        const running = driven(endingIn(0), transport, false);

        await running.settle();
        await running.tick();

        expect(asked).toEqual([]);
    });

    it('attempts nothing where nobody signed in with an authorization server', async () => {
        const { transport, asked } = answering({ body: issuedBody() });
        const running = driven(null, transport);

        await running.settle();
        await running.tick();

        expect(asked).toEqual([]);
    });

    // Somebody who signed out, or signed in as somebody else, while a renewal was on the wire must not have the answer
    // to the old one applied on top of what they hold now.
    it('applies nothing where what is held changed while the renewal was on the wire', async () => {
        const pending: { settle?: (response: ClientResponse) => void } = {};

        const running = driven(endingIn(0), (request) =>
            request.path.endsWith('/token')
                ? new Promise<ClientResponse>((settle) => {
                      pending.settle = settle;
                  })
                : Promise.resolve({ status: 200, body: discovered, headers: {} }),
        );

        await running.settle();
        running.hold(null);

        await act(async () => {
            pending.settle?.({ status: 200, body: issuedBody(), headers: {} });
            await vi.advanceTimersByTimeAsync(0);
        });

        expect(running.renewed).toEqual([]);
        expect(running.ended()).toBe(0);
    });
});
