// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { afterEach, describe, expect, it, vi } from 'vitest';
import { receivesNoRedirect, signInRedirectForThisApplication } from './signInRedirect';

// What is worth proving here is the same as for every other shell operation: which of the two implementations the
// composition root resolves, and that each answers in its own way without the caller ever asking which it has.

/** One command the shell was asked, so a test reads what crossed into it rather than what the operation meant to send. */
interface Asked {
    readonly command: string;
    readonly argument: Readonly<Record<string, unknown>> | undefined;
}

const global = window as unknown as Record<string, unknown>;

function shellAnswering(answers: Readonly<Record<string, unknown>>): Asked[] {
    const asked: Asked[] = [];

    global['__TAURI__'] = {
        core: {
            invoke: (command: string, argument?: Readonly<Record<string, unknown>>) => {
                asked.push({ command, argument });
                const answer = answers[command];

                return answer instanceof Error ? Promise.reject(answer) : Promise.resolve(answer);
            },
        },
    };

    return asked;
}

/** Puts the page at an address, which is how a redirect arrives at the web head. */
function arrivedAt(query: string): void {
    window.history.replaceState(null, '', `/app/${query}`);
}

afterEach(() => {
    delete global['__TAURI__'];
    window.history.replaceState(null, '', '/');
    vi.restoreAllMocks();
});

describe('the web head', () => {
    it('is redirected back to the page itself, with nothing after the path for a registration to disagree with', async () => {
        arrivedAt('?something=else#somewhere');

        const redirect = await signInRedirectForThisApplication();

        expect(redirect.offered).toBe(true);
        expect(redirect.redirectUri).toBe(`${window.location.origin}/app/`);
    });

    it('reads the code and the state the server sent back', async () => {
        arrivedAt('?code=a-code&state=a-state');

        expect((await signInRedirectForThisApplication()).answerWaiting()).toEqual({
            answered: 'code',
            code: 'a-code',
            state: 'a-state',
        });
    });

    // A code left in the address bar travels into every later referrer, into the history somebody can scroll back
    // through, and into whatever a reload would replay it with.
    it('takes the code out of the address bar as it reads it, keeping the fragment', async () => {
        arrivedAt('?code=a-code&state=a-state#mail');

        const redirect = await signInRedirectForThisApplication();

        redirect.answerWaiting();

        expect(window.location.search).toBe('');
        expect(window.location.hash).toBe('#mail');
        expect(redirect.answerWaiting()).toBeNull();
    });

    it('reads a server that refused, carrying whatever state came with it', async () => {
        arrivedAt('?error=access_denied&state=a-state');

        expect((await signInRedirectForThisApplication()).answerWaiting()).toEqual({
            answered: 'refused',
            state: 'a-state',
        });
    });

    it('reads a refusal that carried no state back, rather than reading it as nothing having answered', async () => {
        arrivedAt('?error=access_denied');

        expect((await signInRedirectForThisApplication()).answerWaiting()).toEqual({
            answered: 'refused',
            state: null,
        });
    });

    it.each([
        ['an ordinary start', ''],
        ['a code with no state to check it against', '?code=a-code'],
        ['a state with no code', '?state=a-state'],
    ])('answers nothing to %s', async (_, query) => {
        arrivedAt(query);

        expect((await signInRedirectForThisApplication()).answerWaiting()).toBeNull();
    });

    // An incomplete answer is where a code most needs taking out of the bar rather than least: the answer is refused,
    // so nothing else ever reads that query again and the code would sit there for every later referrer and reload.
    it('clears a code it refused to read, which is the case a complete answer never reaches', async () => {
        arrivedAt('?code=a-code#mail');

        const redirect = await signInRedirectForThisApplication();

        expect(redirect.answerWaiting()).toBeNull();
        expect(window.location.search).toBe('');
        expect(window.location.hash).toBe('#mail');
    });

    it('leaves an address carrying neither a code nor a refusal exactly as it was', async () => {
        arrivedAt('?something=else');

        (await signInRedirectForThisApplication()).answerWaiting();

        expect(window.location.search).toBe('?something=else');
    });
});

describe('the shell head', () => {
    it('is redirected to the loopback address the shell registered, never to the page it is serving', async () => {
        shellAnswering({ sign_in_redirect_uri: 'http://127.0.0.1:8766/' });

        const redirect = await signInRedirectForThisApplication();

        expect(redirect.offered).toBe(true);
        expect(redirect.redirectUri).toBe('http://127.0.0.1:8766/');
    });

    // The shell head is still here when the browser comes back, so its answer arrives where it handed over — which is
    // the whole of the difference from the web head, and the caller writes one sequence for both.
    it('answers where it handed over rather than at the next start', async () => {
        const asked = shellAnswering({
            sign_in_redirect_uri: 'http://127.0.0.1:8766/',
            follow_sign_in_redirect: '?code=a-code&state=a-state',
        });

        const redirect = await signInRedirectForThisApplication();
        const answer = await redirect.hand('https://id.example.invalid/authorize?client_id=x');

        expect(asked[1]).toEqual({
            command: 'follow_sign_in_redirect',
            argument: { address: 'https://id.example.invalid/authorize?client_id=x' },
        });
        expect(answer).toEqual({ answered: 'code', code: 'a-code', state: 'a-state' });
        expect(redirect.answerWaiting()).toBeNull();
    });

    it('answers nothing where the shell gave up waiting for the browser', async () => {
        shellAnswering({
            sign_in_redirect_uri: 'http://127.0.0.1:8766/',
            follow_sign_in_redirect: new Error('nothing came back'),
        });

        const redirect = await signInRedirectForThisApplication();

        expect(await redirect.hand('https://id.example.invalid/authorize')).toBeNull();
    });

    // The Android head is this case until it has a redirect arrangement of its own, and what it produces is a sign-in
    // screen drawing no provider control at all rather than one drawing a button that leads nowhere.
    it('receives no redirect where the shell offers no such command', async () => {
        shellAnswering({ sign_in_redirect_uri: new Error('no such command') });

        expect(await signInRedirectForThisApplication()).toEqual(receivesNoRedirect);
    });
});
