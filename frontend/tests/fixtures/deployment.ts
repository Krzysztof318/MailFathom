// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

// What a deployment says about itself, about the person signed in to it, and about the mailboxes it reads for them —
// everything the client asks for before it has drawn a single message.
//
// `frontend/tests/AGENTS.md` § *The corpus* holds what the whole of it is and what may go in it. What this file adds
// is the two states an account can be looked at in beside the resting one: a mailbox whose synchronization is failing,
// and a mailbox the deployment has not caught up with. They are a page of their own rather than extra entries in the
// resting page, because the resting page is what a screen is looked at in when nothing is wrong, and an account in
// trouble changes what every summary above it says.

/** The name typed into the sign-in screen, which belongs to nobody: it reaches a preview server or a fake transport. */
export const userName = 'user';

/** @see userName */
export const password = 'open sesame';

/** The RFC 7617 value a client composes out of the two above, which is what the exchange and nothing else presents. */
export const expectedAuthorization = 'Basic dXNlcjpvcGVuIHNlc2FtZQ==';

/**
 * The session a deployment answers that exchange with, and the header value every later request presents.
 *
 * It ends far enough out that nothing in a run renews it: renewal reads the clock against this instant, and a fixture
 * inside the margin would put a second request into every check that is not about one.
 */
export const mintedSession = {
    token: 'mfs_browsersuitesession.YnJvd3Nlci1zdWl0ZS1zZXNzaW9u',
    expiresAt: '2126-08-31T21:41:00+00:00',
};

/** @see mintedSession */
export const expectedSessionAuthorization = `Bearer ${mintedSession.token}`;

/**
 * What the session route answers, which is what decides how much of the client is offered at all.
 *
 * The grant names both permissions the client acts on: an answer without them opens a frame with Discover and the
 * intent field absent, which is a different screen from the one most checks are about.
 *
 * **`version` is the one field a consumer replaces rather than reads.** Only a build knows what version it was built
 * from, and a corpus stating one would be a second copy of `Version.props` going stale in a directory nothing reads it
 * from — so a check proving anything about the version spreads the number it read over this value, and every other
 * check takes it as it stands.
 */
export const sessionAnswer = {
    service: 'MailFathom',
    version: '0.0.0',
    permissions: ['mailfathom.mail.read', 'mailfathom.mail.ask'],
    telemetry: 'info',
};

/** Who the signed-in person is, as the frame reads it for the account menu and the settings screen. */
export const ownDisplayName = { displayName: 'Iris Marlow', changeable: true };

/** What the preferences route answers before anything has been chosen, which is every default the deployment holds. */
export const clientPreferences = {
    telemetryEnabled: true,
    theme: 'system',
    openMailInTabs: false,
    markReadOnOpen: true,
    expandWholeThread: false,
    messageView: 'reduced',
    aiFiltersShown: true,
    notificationSeconds: 5,
};

/** The mailbox everything else in the corpus belongs to, up to date and with nothing to say about itself. */
export const workAccount = {
    id: 'work',
    displayName: 'Work',
    synchronizationState: 'Synchronized',
    lastSynchronizedAt: '2026-08-31T09:41:00+00:00',
    behind: false,
};

/** A mailbox whose synchronization is failing, which is what a screen draws its own failure sentence from. */
export const failingAccount = {
    id: 'club',
    displayName: 'Club',
    synchronizationState: 'Failing',
    lastSynchronizedAt: '2026-08-30T18:12:00+00:00',
    behind: true,
};

/**
 * A mailbox that is reachable and not up to date, which is the state the resting one and the failing one both miss.
 *
 * It is worth its own entry because a screen says something different about it: nothing is wrong with the account, so
 * what a reader is owed is that some of their mail has not arrived yet rather than that the deployment cannot reach it.
 */
export const accountBehind = {
    id: 'personal',
    displayName: 'Personal',
    synchronizationState: 'Synchronized',
    lastSynchronizedAt: '2026-08-31T06:02:00+00:00',
    behind: true,
};

/** What the accounts route answers at rest: one mailbox, up to date, with nothing to report. */
export const mailAccounts = {
    synchronizationEnabled: true,
    accounts: [workAccount],
};

/** What the accounts route answers when the deployment has something to say about two of the three mailboxes. */
export const troubledAccounts = {
    synchronizationEnabled: true,
    accounts: [workAccount, failingAccount, accountBehind],
};

const inbox = {
    alias: 'INBOX',
    role: 'Inbox',
    path: ['INBOX'],
    storedEmailCount: 4213,
    unreadEmailCount: 12,
    synchronizationState: 'Synchronized',
    lastSynchronizedAt: '2026-08-31T09:41:00+00:00',
    behind: false,
};

// A folder nested under a level of its own alias, which is where the tree nests one and what a tree that has been
// expanded and collapsed is read against. Nothing is bound to `ARCHIVE` itself, so that level is a heading the tree
// draws rather than a folder the deployment named — the shape a mail server produces every time somebody files below
// a folder they never mapped. The alias is upper-cased because that is the one form the service answers with, which
// is also why the level's own name has to be read off the remote path. It carries no role, as most folders carry none.
const archive2024 = {
    alias: 'ARCHIVE/2024',
    role: null,
    path: ['Archive', '2024'],
    storedEmailCount: 980,
    unreadEmailCount: 0,
    synchronizationState: 'Synchronized',
    lastSynchronizedAt: '2026-08-31T09:00:00+00:00',
    behind: false,
};

/** A folder holding nothing, which is the state a message list has to be looked at in and cannot reach by scrolling. */
export const emptyFolder = {
    alias: 'RECEIPTS',
    role: null,
    path: ['Receipts'],
    storedEmailCount: 0,
    unreadEmailCount: 0,
    synchronizationState: 'Synchronized',
    lastSynchronizedAt: '2026-08-31T09:41:00+00:00',
    behind: false,
};

/** What the folders route answers at rest: one mailbox with an inbox and a folder nested under another. */
export const mailFolders = {
    synchronizationEnabled: true,
    accounts: [{ account: workAccount, folders: [inbox, archive2024] }],
};

/**
 * What the folders route answers for {@link troubledAccounts}, so a tree can be looked at in the states above.
 *
 * The failing mailbox carries a folder that is behind as well as an account that is: a tree draws both, and a fixture
 * that only set the account's state would leave the row a reader actually points at saying nothing.
 */
export const troubledFolders = {
    synchronizationEnabled: true,
    accounts: [
        { account: workAccount, folders: [inbox, archive2024, emptyFolder] },
        {
            account: failingAccount,
            folders: [
                { ...inbox, storedEmailCount: 61, unreadEmailCount: 4, synchronizationState: 'Failing', behind: true },
            ],
        },
        { account: accountBehind, folders: [{ ...inbox, storedEmailCount: 812, unreadEmailCount: 0, behind: true }] },
    ],
};
