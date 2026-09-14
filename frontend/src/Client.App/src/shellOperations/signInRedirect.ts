// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

// Where an OAuth sign-in comes back to, which is the third shell operation and the one whose two implementations differ
// most. A page is served from an origin an authorization server can redirect to, so it hands the browser over and is
// itself replaced; a WebView has no such origin, so the shell listens on a loopback address and hands the person to
// their own browser — which is also the arrangement every recommendation on native applications asks for, since a
// sign-in screen painted inside the application's own window is one nobody can tell from a counterfeit.
//
// The caller never learns which of the two it has. It keeps what the attempt was started with, hands over an address,
// and reads an answer — from the promise where handing over came back, and from what was waiting where it did not. One
// of the two is always the one that answers, and no screen, hook, or module asks which.
//
// A head that offers neither is an answer of its own rather than a failure: the client draws no provider control there
// at all, which is what the Android head is until it has a redirect arrangement. `AGENTS.md` § *the other exception is
// a shell operation* is the rule this follows, and `systemNotifier.ts` is the module it follows.
//
// It travels as a value rather than through a context, which is where it differs from the two modules beside it: one
// screen asks this, and a context exists to spare a value the hops between where it is resolved and where it is read.
// `credentialStore.ts` is the other operation of that shape and reaches the same screen the same way.

/** What an authorization server's redirect carried back. */
export type SignInRedirectAnswer =
    | { readonly answered: 'code'; readonly code: string; readonly state: string }

    /** The server, or the person at it, stopped the authorization. The state is carried so a stale answer is still discarded. */
    | { readonly answered: 'refused'; readonly state: string | null };

/** How the application hands somebody to an authorization server and reads what came back. */
export interface SignInRedirect {
    /** Whether this head receives a redirect at all, which is what decides a provider control is drawn. */
    readonly offered: boolean;

    /** The address the authorization server redirects to, which an operator registers exactly as written. */
    readonly redirectUri: string;

    /**
     * Hands the person to the authorization server, answering what came back where this head is still here to read it.
     *
     * The web head is replaced by the address it opens, so nothing it answers is ever read: what the browser brings
     * back arrives at the next start, through {@link answerWaiting}. The shell head stays, so its answer comes back
     * here. A caller writes one sequence for both.
     */
    hand(address: string): Promise<SignInRedirectAnswer | null>;

    /**
     * Gives up on a hand-over nobody is waiting for any more, where waiting holds something a later one needs.
     *
     * The shell head binds its loopback port before it opens the browser and holds it for the whole deadline, so a
     * sign-in somebody walked away from leaves every attempt inside that window unable to bind — which reaches the
     * screen as a deployment that did not answer. Where nothing is held, this does nothing: the web head has already
     * been replaced by the address it opened, and a head that receives no redirect never started one.
     */
    abandon(): void;

    /** The answer that was already waiting when this run started, which is the web head's whole way of answering. */
    answerWaiting(): SignInRedirectAnswer | null;
}

/** What a head that receives no redirect answers, and what every caller reads before it offers anything. */
export const receivesNoRedirect: SignInRedirect = {
    offered: false,
    redirectUri: '',
    hand: () => Promise.resolve(null),
    abandon: () => undefined,
    answerWaiting: () => null,
};

/**
 * Resolves the operation for the head this bundle is running in, which is the whole of the composition.
 *
 * The shell is asked first, for the reason `systemNotifier.ts` gives about the order: a desktop head is a browser page
 * as far as the DOM is concerned, so a question about the page would answer for a head the shell is already answering
 * for — and its answer, an origin no authorization server could redirect to, would be the wrong one.
 */
export async function signInRedirectForThisApplication(): Promise<SignInRedirect> {
    const shell = window.__TAURI__;

    if (shell === undefined) {
        return redirectedBackToThisPage();
    }

    const registered = await shell.core.invoke('sign_in_redirect_uri').catch(() => null);

    return typeof registered === 'string' && registered.length > 0
        ? redirectedThroughTheShell(registered)
        : receivesNoRedirect;
}

/**
 * The web head, which is served from an origin and is redirected back to it.
 *
 * The redirect address is this document's own, with nothing after the path: a query and a fragment are what the answer
 * arrives in, and a registered redirect URI is compared exactly, so neither may be part of what is registered. What an
 * operator registers is therefore the address of the page itself — `https://mail.example.test/app/` for a deployment
 * serving the client where every published image serves it.
 */
function redirectedBackToThisPage(): SignInRedirect {
    return {
        offered: true,
        redirectUri: `${window.location.origin}${window.location.pathname}`,

        hand: (address) => {
            // The document is replaced by what this opens, so the promise is never settled and nothing waits on it: the
            // answer arrives at the next start, which is where `answerWaiting` reads it.
            window.location.assign(address);

            return new Promise(() => undefined);
        },

        abandon: () => undefined,

        answerWaiting: () => {
            const query = window.location.search;
            const carried = new URLSearchParams(query);

            if (carried.get('code') === null && carried.get('error') === null) {
                return null;
            }

            // The code and the state are taken out of the address bar before anything else runs, which is the whole
            // reason this clears rather than only reads: an authorization code left there travels into every later
            // referrer, into the session history the person can scroll back through, and into whatever a bookmark or a
            // reload would replay it with.
            //
            // Cleared on the presence of either parameter rather than on a complete answer, because an incomplete one
            // is where a code most needs taking out of the bar: a redirect carrying a code and no state — a server
            // that omitted it, or a link somebody was sent — is refused by the reader below and would otherwise leave
            // the code sitting there with nothing having read it.
            window.history.replaceState(null, '', `${window.location.pathname}${window.location.hash}`);

            return answerIn(query);
        },
    };
}

/**
 * The shell head, which has no origin to be redirected to and listens on a loopback address instead.
 *
 * Handing over is one command rather than a listener and a link opened separately, because the listener has to be bound
 * before the browser is started; `src-tauri/src/redirects.rs` holds that and the rest of the arrangement. What comes
 * back is the query exactly as the browser sent it, read here by the same reader the page's own address bar is read by.
 */
function redirectedThroughTheShell(redirectUri: string): SignInRedirect {
    return {
        offered: true,
        redirectUri,

        hand: async (address) => {
            const query = await window.__TAURI__?.core.invoke('follow_sign_in_redirect', { address }).catch(() => null);

            return typeof query === 'string' ? answerIn(query) : null;
        },

        abandon: () => {
            void window.__TAURI__?.core.invoke('abandon_sign_in_redirect').catch(() => null);
        },

        answerWaiting: () => null,
    };
}

/**
 * What a redirect's query says, or `null` where it says nothing this client started a flow for.
 *
 * Read with the platform's own parser rather than by hand, because this is the application and not `Client.Backend`:
 * what a query is, and how a `+` and a percent escape in one decode, is the platform's answer rather than a second one
 * written here. The query is read on its own, with no address involved at all.
 */
function answerIn(query: string): SignInRedirectAnswer | null {
    const stated = new URLSearchParams(query.startsWith('?') ? query.slice(1) : query);
    const code = stated.get('code');
    const state = stated.get('state');

    if (code !== null && code.length > 0 && state !== null && state.length > 0) {
        return { answered: 'code', code, state };
    }

    // A server that refused says so in `error`, and RFC 6749 requires the state back with it — but a server that sent
    // neither has still refused as far as anybody waiting is concerned, which is why the state is carried as possibly
    // absent rather than being what decides there is an answer at all.
    return stated.get('error') === null ? null : { answered: 'refused', state };
}
