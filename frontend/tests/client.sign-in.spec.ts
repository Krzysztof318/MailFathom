// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { expect, type Page, type Request, type Route } from '@playwright/test';

import {
    grantRenewalMargin,
    openAccountMenu,
    openSignedIn,
    refreshInterval,
    renewalCheckInterval,
    sessionRenewalMargin,
    signIn,
    test,
} from './client.harness';
import * as deployment from './fixtures/deployment';

// Getting in and staying in: the credential screen, what the exchange is sent and what every request presents
// afterwards, how long a session is kept and where, the provider flow that leaves the page and comes back, and what
// becomes of all of it as the clock runs — a session or a grant renewed before it ends, and one the deployment has
// stopped accepting.

/**
 * What a deployment reached at this address states as the identifier a token has to be issued for.
 *
 * RFC 9728 has a client check that the document names the address it was read from, so the identifier is composed onto
 * the origin this run actually took rather than written in the corpus, where the port is not known until the preview
 * server has one.
 */
function resourceReadAt(address: string): string {
    return `${new URL(address).origin}/api/client`;
}

/** One value of the corpus put on the wire as the deployment behind the preview server would answer with it. */

function answering(route: Route, answered: unknown): Promise<void> {
    return route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(answered) });
}

test('asks for a credential before any mail, and opens the frame once one is accepted', async ({ page }) => {
    await page.goto('/');

    // The origin serving the bundle is the deployment, so the only thing missing is who is asking — which is why the
    // address is not on this screen and the credential is.
    await expect(page.getByRole('heading', { name: 'Connect your mailbox' })).toBeVisible();
    await expect(page.getByRole('textbox', { name: 'Server' })).toHaveCount(0);
    await expect(page.getByRole('navigation', { name: 'Spaces' })).toHaveCount(0);

    await signIn(page);

    await expect(page.getByRole('heading', { name: 'Discover', level: 1 })).toBeVisible();
});

test('sends the password once to the exchange, and the session it was given on every request after', async ({
    page,
}) => {
    const presented: [string, string | undefined][] = [];

    page.on('request', (request) => {
        const address = new URL(request.url());

        if (address.pathname.startsWith('/api/client/')) {
            presented.push([address.pathname, request.headers()['authorization']]);
        }
    });

    await openSignedIn(page);

    // Every request on the client surface rather than the set of distinct values: a read that stopped carrying a
    // credential would leave the set unchanged, because the requests before it already put each value in it. Only the
    // built bundle answers this at all — the encoding runs through the browser's own `TextEncoder` and `btoa` after
    // the bundler has been over it, and what a screen sends is not what a component was handed in jsdom.
    expect(presented.length).toBeGreaterThan(0);
    for (const [route, authorization] of presented) {
        // What a sign-in screen may offer is read before anybody holds anything, so that one route carries no
        // credential at all — and it is asserted here rather than skipped, because a client that presented a session on
        // it would be one asking a question it has already answered.
        const expected =
            route === '/api/client/sign-in-methods'
                ? undefined
                : route === '/api/client/session/token'
                  ? deployment.expectedAuthorization
                  : deployment.expectedSessionAuthorization;

        expect(authorization, `wrong credential on ${route}`).toBe(expected);
    }

    // Stated as its own assertion rather than left to the loop above, because it is the property the exchange exists
    // for: the password reaches exactly one route, and no read of anybody's mail costs the deployment a derivation.
    expect(presented.filter(([, authorization]) => authorization === deployment.expectedAuthorization)).toStrictEqual([
        ['/api/client/session/token', deployment.expectedAuthorization],
    ]);
});

test('stays signed in across a reload, and asks again in a tab that was not signed in', async ({ page, context }) => {
    await openSignedIn(page);

    await page.reload();

    // A reload is a cold start for a single-page application, so surviving one is the whole of what keeping the
    // credential buys — and only a real document reloaded a second time proves it was read back rather than held.
    await expect(page.getByRole('heading', { name: 'Discover', level: 1 })).toBeVisible();

    const secondTab = await context.newPage();
    await secondTab.goto('/');

    // What the web head keeps is kept for the tab and for nothing wider, which is the bound ADR 0023 puts on it. No
    // unit test can make that claim: a second tab is a second document, and jsdom has one.
    await expect(secondTab.getByRole('textbox', { name: 'Login' })).toBeVisible();
    await secondTab.close();
});

test('asks for the credential again after signing out, including across a reload', async ({ page }) => {
    await openSignedIn(page);

    await openAccountMenu(page);
    await page.getByRole('button', { name: 'Sign out' }).click();
    await expect(page.getByRole('textbox', { name: 'Login' })).toBeVisible();

    await page.reload();

    await expect(page.getByRole('textbox', { name: 'Login' })).toBeVisible();
    await expect(page.getByRole('navigation', { name: 'Spaces' })).toHaveCount(0);
});

test('reads a refused password itself rather than letting the browser ask for one', async ({
    page,
    context,
    baseURL,
}) => {
    if (baseURL === undefined) {
        throw new Error('The suite is configured with no base address to set a cookie against.');
    }

    const prompted: string[] = [];
    const asked: Request[] = [];

    page.on('dialog', (dialog) => prompted.push(dialog.type()));
    page.on('request', (request) => {
        if (new URL(request.url()).pathname === '/api/client/session/token') {
            asked.push(request);
        }
    });

    // A cookie on the origin the bundle was served from, which is what makes the assertion below about the request's
    // credentials mode rather than about an origin that happened to have nothing to send. The Fetch Standard gates the
    // user agent's own credential prompt on the same flag that decides whether this cookie travels, so a request that
    // carried it is a request the browser would have prompted for.
    await context.addCookies([{ name: 'mailfathom-probe', value: 'set', url: baseURL }]);

    // The deployment refuses the password and challenges as MailFathom does wherever it accepts one — Basic named
    // first, which is what tells the client a password may be sent at all and what separates this refusal from a
    // deployment offering no password method.
    await page.route('**/api/client/session/token', (route) =>
        route.fulfill({
            status: 401,
            headers: { 'www-authenticate': 'Basic realm="MailFathom", charset="UTF-8"' },
        }),
    );

    await page.goto('/');
    await page.getByRole('textbox', { name: 'Login' }).fill(deployment.userName);
    await page.getByLabel('Password', { exact: true }).fill(deployment.password);
    await page.getByRole('button', { name: 'Connect' }).click();

    // The screen says what the deployment decided, which is the whole point of the client reading the challenge: a
    // dialog standing in front of this sentence is one nobody can get past to the form behind it.
    await expect(page.getByText('The login or the password is not accepted by this deployment.')).toBeVisible();

    expect(asked.length).toBeGreaterThan(0);
    for (const request of asked) {
        expect((await request.allHeaders())['cookie']).toBeUndefined();
    }

    expect(prompted).toStrictEqual([]);
});

/**
 * The deployment above with its operator's authorization server published, and that server faked beside it.
 *
 * Three things are faked rather than one, because a sign-in through a provider is three exchanges and a navigation.
 * The policy is widened here because that is the deployment's own half of it: the bundle writes `connect-src 'self'`
 * and a deployment publishing a server widens it with that issuer's origin at startup, which the preview server serving
 * the built file does not do — and without it the browser refuses the discovery request before it ever leaves the page,
 * which is the whole reason the service widens it at all.
 */
async function offeringAProvider(page: Page, authorizations: string[], redemptions: string[]): Promise<void> {
    const issuer = deployment.authorizationServerIssuer;
    const issuerOrigin = new URL(issuer).origin;

    await page.route(
        (url) => url.pathname === '/' && url.origin !== issuerOrigin,
        async (route) => {
            const served = await route.fetch();
            const headers = { ...served.headers() };
            const policy = headers['content-security-policy'] ?? '';

            headers['content-security-policy'] = policy.replace(
                "connect-src 'self'",
                `connect-src 'self' ${issuerOrigin}`,
            );

            await route.fulfill({ response: served, headers });
        },
    );

    await page.route('**/api/client/sign-in-methods', (route) => answering(route, deployment.signInMethodsOffered));

    // The first of the three addresses RFC 8414 puts a document for a path-carrying issuer at, which is the one this
    // client asks for first and the only one a server has to answer for the rest of the flow to happen.
    await page.route('**/.well-known/oauth-authorization-server/**', (route) =>
        answering(route, deployment.authorizationServerMetadata),
    );

    // The authorization server's own screen, which a person would meet here and which this answers for: it reads the
    // request, hands back the code against the state it was given, and sends the browser to the address the client
    // registered. A page of the server's own rather than a `302`, because a redirect the browser follows by itself is
    // one hop of a single request — the answer would arrive at the client under this suite's own routing and under the
    // policy the redirect started from, which is neither of the two things being proven here.
    await page.route(`${issuer}/protocol/openid-connect/auth*`, (route) => {
        const asked = new URL(route.request().url()).searchParams;
        const back = new URL(asked.get('redirect_uri') ?? '');

        authorizations.push(route.request().url());
        back.searchParams.set('code', 'browser-suite-authorization-code');
        back.searchParams.set('state', asked.get('state') ?? '');

        return route.fulfill({
            status: 200,
            contentType: 'text/html',
            body: `<!doctype html><title>Nordwind staff directory</title><script>location.replace(${JSON.stringify(
                back.toString(),
            )})</script>`,
        });
    });

    // A redemption is answered with the token the sign-in was issued and a renewal with the one that replaces it, told
    // apart the way the server tells them apart: by the grant the request names.
    await page.route(`${issuer}/protocol/openid-connect/token`, (route) => {
        const redeemed = route.request().postData() ?? '';

        redemptions.push(redeemed);

        return answering(
            route,
            redeemed.includes('grant_type=refresh_token') ? deployment.renewedToken : deployment.issuedToken,
        );
    });
}

// The one flow no unit test reaches end to end: it leaves the page for a second origin and comes back to a fresh
// document, so what carries the attempt across is the browser's own session storage and what carries the answer back
// is a real address bar. Everything below the navigation is covered beside its source; the navigation is not.
test('signs a person in through a published provider, and presents the token it was issued afterwards', async ({
    page,
}) => {
    const authorizations: string[] = [];
    const redemptions: string[] = [];
    const presented: string[] = [];
    const violations: string[] = [];

    await offeringAProvider(page, authorizations, redemptions);

    page.on('console', (message) => {
        if (message.text().includes('Content Security Policy')) {
            violations.push(message.text());
        }
    });

    page.on('request', (request) => {
        if (new URL(request.url()).pathname.startsWith('/api/client/')) {
            presented.push(request.headers()['authorization'] ?? '');
        }
    });

    await page.goto('/');

    const control = page.getByRole('button', { name: 'Continue to Keycloak' });

    // Every way in the deployment published is drawn at once: its own provider above, the grid under it, the form below.
    await expect(page.getByRole('button', { name: 'Sign in' })).toBeVisible();
    await expect(control).toBeVisible();
    await expect(page.getByRole('textbox', { name: 'Login' })).toBeVisible();

    await control.click();

    await expect(page.getByRole('navigation', { name: 'Spaces' })).toBeVisible();

    const asked = new URL(authorizations[0] ?? '').searchParams;

    expect(asked.get('response_type')).toBe('code');
    expect(asked.get('client_id')).toBe('mailfathom-client');
    expect(asked.get('code_challenge_method')).toBe('S256');
    expect(asked.get('resource')).toBe(resourceReadAt(page.url()));
    expect(asked.get('scope')).toBe(deployment.protectedResource.scopes_supported.join(' '));
    expect(asked.get('nonce')).not.toBeNull();

    // The secret the redemption proves the request with is stated nowhere the address bar, a referrer, or a server log
    // could carry it, which is the whole of what PKCE is.
    expect(asked.get('code_verifier')).toBeNull();
    expect(redemptions[0]).toContain('grant_type=authorization_code');
    expect(redemptions[0]).toContain('code_verifier=');

    // The code is taken out of the address before anything else runs, so a reload replays nothing and no referrer
    // carries it onward.
    expect(new URL(page.url()).search).toBe('');

    // What every request afterwards presents is the token that server issued, and never a session this deployment
    // minted — nothing exchanged one for the other.
    expect(presented).toContain(deployment.expectedGrantAuthorization);
    expect(presented).not.toContain(deployment.expectedSessionAuthorization);

    // The whole flow ran inside the policy the page is served under, which is what the widened `connect-src` is for.
    expect(violations).toStrictEqual([]);
});

test('signs in at the narrowest width a supported head presents', async ({ page }) => {
    await page.setViewportSize({ width: 320, height: 640 });
    await page.goto('/');

    // The screen in front of the frame meets the same bar the frame does, and it is the one screen nobody can go
    // around: a form that overflowed at this width would be a client somebody could not sign in to at all.
    await expect(page.getByRole('textbox', { name: 'Login' })).toBeVisible();
    await expect(page.getByLabel('Password', { exact: true })).toBeVisible();
    await expect(page.getByRole('button', { name: 'Connect' })).toBeVisible();

    // The heading is asked for here rather than in jsdom because what would take it away is a breakpoint: the brand
    // half drops its claim below the split, and a top-level heading that went with it would leave a screen reader
    // starting at the form's own `h2` at exactly the widths a phone presents. Only a browser lays that out.
    await expect(page.getByRole('heading', { level: 1 })).toBeVisible();

    // The document's own overflow rather than the body's box: `body` is a block element with no width rule, so its
    // used width is the viewport's whatever a child inside it does, and an assertion on it could not fail.
    // Asked as an expression rather than as a function, because this suite is compiled without a DOM declaration on
    // purpose — `tsconfig.json` says why — and a closure naming `document` would be the one thing that changes.
    const overflowing = await page.evaluate<boolean>(
        'document.documentElement.scrollWidth > document.documentElement.clientWidth',
    );
    expect(overflowing).toBe(false);
});

/**
 * Every credential the page presented on the client surface to read or to act, in the order it issued the requests
 * carrying one.
 *
 * What the client recorded about itself is left out: records made under one session are flushed under that session when
 * it is replaced, which `telemetry/clientTelemetry.ts` does on purpose so no record is attributed to a credential it was
 * not made under — and that flush can reach the wire after the first request the renewed credential carried.
 */
function presentedBy(page: Page): string[] {
    const presented: string[] = [];

    page.on('request', (request) => {
        const { pathname } = new URL(request.url());

        if (pathname.startsWith('/api/client/') && !pathname.startsWith('/api/client/telemetry/')) {
            presented.push(request.headers()['authorization'] ?? '');
        }
    });

    return presented;
}

/**
 * What the page presented from the first request carrying the renewed credential onwards, once the frame has read
 * again everything it draws — which is every request the client makes on its own, asked after the renewal rather than
 * inferred from one of them.
 */
async function presentedSinceRenewing(page: Page, presented: readonly string[], renewed: string): Promise<string[]> {
    await expect.poll(() => presented.includes(renewed)).toBe(true);

    const from = presented.indexOf(renewed);

    await page.clock.runFor(refreshInterval);

    return presented.slice(from);
}

// The instant a check about renewing starts its clock at: two hours before the corpus session ends, so the renewal it
// sees is one the running clock brought due rather than one signing in made.
const beforeTheSessionEnds = new Date(Date.parse(deployment.mintedSession.expiresAt) - 2 * sessionRenewalMargin);

// Renewing is a timer the client runs against an instant the deployment stated, and what it changes is every request
// afterwards — which is the seam `useSessionRenewal.test.ts` stops short of: it proves the hook hands the renewed
// session on, and nothing about what the frame then presents.
test('renews the session before it ends, and presents the renewed one on everything after without asking again', async ({
    page,
    deployment: served,
}) => {
    const presented = presentedBy(page);

    await page.clock.install({ time: beforeTheSessionEnds });
    await openSignedIn(page, '/#/mail');
    await page.clock.runFor(sessionRenewalMargin + renewalCheckInterval);

    const since = await presentedSinceRenewing(page, presented, deployment.expectedRenewedSessionAuthorization);

    expect(since.length).toBeGreaterThan(1);
    expect(since.filter((credential) => credential !== deployment.expectedRenewedSessionAuthorization)).toStrictEqual(
        [],
    );
    expect(served.requests('POST', '/session/token')).toHaveLength(2);
    await expect(page.getByRole('navigation', { name: 'Spaces' })).toBeVisible();
    await expect(page.getByRole('textbox', { name: 'Login' })).toHaveCount(0);
});

test('renews a provider grant with its refresh token before the access token ends, and presents the renewed one after', async ({
    page,
}) => {
    const redemptions: string[] = [];
    const presented = presentedBy(page);

    await page.clock.install();
    await offeringAProvider(page, [], redemptions);
    await page.goto('/');
    await page.getByRole('button', { name: 'Continue to Keycloak' }).click();
    await expect(page.getByRole('navigation', { name: 'Spaces' })).toBeVisible();

    await page.clock.runFor(deployment.issuedToken.expires_in * 1_000 - grantRenewalMargin + renewalCheckInterval);

    const since = await presentedSinceRenewing(page, presented, deployment.expectedRenewedGrantAuthorization);

    expect(redemptions.at(-1)).toContain(`refresh_token=${deployment.issuedToken.refresh_token}`);
    expect(since.filter((credential) => credential !== deployment.expectedRenewedGrantAuthorization)).toStrictEqual([]);
    await expect(page.getByRole('textbox', { name: 'Login' })).toHaveCount(0);
});

// Two ways a deployment says it has stopped accepting the sign-in, and one answer to both: the sign-in screen, saying
// why, and a reload that draws nothing of the mailbox that was open. The first is the renewal the running clock brings
// due; the second is a read of a folder somebody opened, which is not the connection's own read and was, before this
// check, answered in the list as a folder that could not be read while the frame stayed signed in.
for (const refusal of [
    {
        met: 'renewing it',
        refused: '**/api/client/session/token',
        async meet(page: Page): Promise<void> {
            await page.clock.runFor(sessionRenewalMargin + renewalCheckInterval);
        },
    },
    {
        met: 'reading a folder',
        refused: '**/api/client/emails?*',
        async meet(page: Page): Promise<void> {
            await page
                .getByRole('tree', { name: 'Mailboxes and folders' })
                .getByRole('treeitem', { name: /^Drafts/u })
                .click();
        },
    },
]) {
    test(`returns to the sign-in saying why when the deployment stops accepting the sign-in on ${refusal.met}, and draws nothing of the mailbox after a reload`, async ({
        page,
    }) => {
        await page.clock.install({ time: beforeTheSessionEnds });
        await openSignedIn(page, '/#/mail');
        await expect(page.getByRole('listbox', { name: 'Messages' }).getByRole('option').first()).toBeVisible();

        await page.route(refusal.refused, (route) => route.fulfill({ status: 401 }));
        await refusal.meet(page);

        await expect(
            page.getByText('This deployment has stopped accepting the sign-in that was kept. Sign in again.'),
        ).toBeVisible();
        await expect(page.getByRole('textbox', { name: 'Login' })).toBeVisible();

        await page.reload();

        await expect(page.getByRole('textbox', { name: 'Login' })).toBeVisible();
        await expect(page.getByRole('navigation', { name: 'Spaces' })).toHaveCount(0);
        await expect(page.getByRole('listbox', { name: 'Messages' })).toHaveCount(0);
    });
}
