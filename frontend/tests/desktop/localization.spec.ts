// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { expect, test } from '@playwright/test';

import * as deployment from '../fixtures/deployment';
import * as messages from '../fixtures/messages';
import { freshProfile, openDesktopHead, type DesktopHead } from './head';

// The two rules that could diverge on the desktop head without anybody noticing, asked of the real WebView. Both are
// answered by the platform rather than by the client: the zone every instant is placed against is whatever the runtime
// reports, and the language a first run opens in is whatever the platform states as a preference — and a WebView reads
// each from the process it was started in rather than from a browser profile a test configured.
// `frontend/tests/AGENTS.md` § *The desktop suite* holds what this suite owns and what it leaves to the other three.

// A shell is started per case, and the reading-pane cases sign in on top of that, so a case is minutes rather than
// seconds. The number is the shell's start, not the client's: the client is the same bundle the browser suite drives.
test.describe.configure({ timeout: 180_000 });

/** The instant the corpus message carries, which is the one this suite reads in two zones. */
const sentAt = messages.newsletterMessage.headers.sentAt;

// The literal spellings, which is the whole point: an expectation written against a formatter built the same way passes
// for a head that named a zone of its own as happily as for one that did not. They are the same two the unit suite
// asserts in `localization/instants.test.ts`, because the rule is one rule and a second reading of it here would be a
// second chance to get it wrong. 09:41 UTC is the evening of the same day in Tokyo and the small hours of it in Los
// Angeles.
const spellings: Readonly<Record<string, string>> = {
    'Asia/Tokyo': '8/31/26, 6:41 PM',
    'America/Los_Angeles': '8/31/26, 2:41 AM',
};

/**
 * What language the head opened in, read off the one control that states it during a render.
 *
 * The document's `lang` is written by an effect, and `index.html` declares `en` before any of this client has run — so
 * reading that attribute alone cannot tell a head that resolved English from one that has not resolved anything yet.
 * The checked radio in the language strip is the same decision observed in the commit that drew the screen, so it is
 * what is waited for; the attribute is then held against it, which is how the declaration the whole token layer and
 * every spelling dictionary read is proven rather than assumed.
 */
async function languageTheHeadOpenedIn(head: DesktopHead): Promise<string> {
    const resolved = await head.settled<string>(
        'document.querySelector(\'input[name="language-choice"]:checked\')?.value ?? null',
    );

    await head.settled(`document.documentElement.lang === ${JSON.stringify(resolved)} ? true : null`);

    return resolved;
}

/**
 * Signs in against the fixture corpus, opens the first message, and answers the instant the reading pane drew.
 *
 * The reading pane is where an instant is said in full rather than relative to now: a row words today, yesterday, and
 * the day and the month, none of which a literal expectation could survive the passing of a day. Every `<time>` outside
 * the list carrying that instant is collected, so what is read is the pane's own rather than whichever matched first.
 */
async function instantTheReadingPaneDrew(head: DesktopHead): Promise<readonly string[]> {
    // The ids the labels point at, which are the same elements the browser suite reaches by accessible name. WebDriver
    // has no locator for a role and a name, and that the two are associated at all is what the unit suite asserts.
    await head.fill('#sign-in-user-name', deployment.userName);
    await head.fill('#sign-in-password', deployment.password);
    await head.click('button[type="submit"]');

    // Signing in lands on the client's default space, so the space holding mail is asked for afterwards — the same
    // ordering `frontend/design-parity/capture.ts` follows, and for the same reason.
    await head.evaluate('(document.location.hash = "#/mail", true)');
    await head.click('[role="listbox"] [role="option"]');

    return head.settled<readonly string[]>(`(() => {
        const drawn = [...document.querySelectorAll('time[datetime="${sentAt}"]')]
            .filter((said) => said.closest('[role="listbox"]') === null)
            .map((said) => said.textContent);

        return drawn.length === 0 ? null : drawn;
    })()`);
}

for (const [zone, spelling] of Object.entries(spellings)) {
    test(`places an instant against the zone the host reports, started in ${zone}`, async () => {
        const head = await openDesktopHead({ TZ: zone });

        try {
            expect(await instantTheReadingPaneDrew(head)).toStrictEqual([spelling]);
        } finally {
            await head.close();
        }
    });
}

// What a WebView reports as the machine's language preference comes from the platform rather than from anything a test
// can set on a context, which is the whole reason these cases are here. Two things about the Linux head were measured
// rather than assumed, and both decide how this is written.
//
// **It reads the C locale, not `LANGUAGE`.** WebKitGTK answers `navigator.languages` out of `LC_ALL` and `LANG`;
// `LANGUAGE`, which is what GLib's own message lookup reads first, is ignored. A case stating its preference there and
// leaving `LC_ALL` at `C.UTF-8` reports `C` and opens in English — a green-looking case proving the opposite of what it
// says. So all three are stated together, as a machine consistently set to one language.
//
// **It reports exactly one language.** A browser answers a list and `narrowToOfferedLocale` walks it; this head hands
// over a single tag whatever is in the environment, so the walk is a web-head property and only its first step can be
// asked here. That is a fact about the platform rather than a gap to close: the narrowing itself is covered by the unit
// suite, and what is left for this suite is that a real WebView's one tag reaches the right catalogue.
function preferring(locale: string): Readonly<Record<string, string>> {
    return { LANG: locale, LC_ALL: locale, LANGUAGE: locale };
}

test('opens in Polish on a machine that prefers Polish, with nothing chosen', async () => {
    const head = await openDesktopHead(preferring('pl_PL.UTF-8'));

    try {
        expect(await languageTheHeadOpenedIn(head)).toBe('pl');
    } finally {
        await head.close();
    }
});

test('opens in English on a machine preferring a language the client does not carry', async () => {
    const head = await openDesktopHead(preferring('de_DE.UTF-8'));

    try {
        expect(await languageTheHeadOpenedIn(head)).toBe('en');
    } finally {
        await head.close();
    }
});

test('opens in English on a head whose language preference names nothing this client could read', async () => {
    // The C locale, which is what a shell started by a service manager with a bare environment is in. WebKitGTK words
    // `C.UTF-8` as the tag `C` and bare `C` as `en-US`, so this is the stricter of the two: what the client is handed is
    // not a language it carries and not a language at all, and what it owes is English regardless.
    const head = await openDesktopHead(preferring('C.UTF-8'));

    try {
        expect(await languageTheHeadOpenedIn(head)).toBe('en');
    } finally {
        await head.close();
    }
});

test('lets a choice outrank the machine preference, and keeps it across a restart of the shell', async () => {
    const preference = preferring('pl_PL.UTF-8');
    const profile = freshProfile();
    const chosen = await openDesktopHead(preference, profile);

    try {
        expect(await languageTheHeadOpenedIn(chosen)).toBe('pl');

        // The label rather than the radio inside it, which is hidden from sight and not from the accessibility tree —
        // the same element the browser suite clicks, reached by the one thing WebDriver offers.
        await chosen.click('label:has(input[name="language-choice"][value="en"])');
        expect(await languageTheHeadOpenedIn(chosen)).toBe('en');
    } finally {
        await chosen.close();
    }

    // A second shell, started afresh under the same preference and over the same profile. This is the assertion only
    // the desktop head can make: the choice was written into the WebView's own storage and read back by a new process,
    // where a browser proves only that a reloaded document read back what the same browser wrote.
    const restarted = await openDesktopHead(preference, profile);

    try {
        expect(await languageTheHeadOpenedIn(restarted)).toBe('en');
    } finally {
        await restarted.close();
    }
});
