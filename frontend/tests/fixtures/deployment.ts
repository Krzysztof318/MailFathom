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
 * What the exchange answers a session presented to it for renewal, which is a token of its own ending a day later.
 *
 * A different token rather than the same one again, because what a check about renewing proves is that every request
 * afterwards presents what the deployment answered — and a renewal answering the token it was handed would pass that
 * for a client that never read the answer at all.
 */
export const renewedSession = {
    token: 'mfs_browsersuiterenewed.YnJvd3Nlci1zdWl0ZS1yZW5ld2Vk',
    expiresAt: '2126-09-01T21:41:00+00:00',
};

/** @see renewedSession */
export const expectedRenewedSessionAuthorization = `Bearer ${renewedSession.token}`;

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

// A folder nested a level down its path, which is where the tree nests one and what a tree that has been expanded and
// collapsed is read against. Nothing the folders route answers sits at `Archive` itself, so that level is a heading
// the tree draws rather than a folder the deployment named — the shape a mail server produces every time somebody
// files below a folder they never mapped. The alias is upper-cased because that is the one form the service answers
// with, which is why every name a row shows is read off the path. It carries no role, as most folders carry none.
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

// The drafts folder, which a deployment supplies on the first read that finds it missing whatever the mail server held,
// and which holds nothing here: what stands in it is what somebody filed there, so a corpus draft would be a message
// nobody wrote.
const draftsFolder = {
    alias: 'DRAFTS',
    role: 'Drafts',
    path: ['Drafts'],
    storedEmailCount: 0,
    unreadEmailCount: 0,
    synchronizationState: 'Synchronized',
    lastSynchronizedAt: '2026-08-31T09:41:00+00:00',
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

// The two folders the acts on a message file into, each named by the role it plays rather than by where it sits: the
// trash is where deleting puts a message and where deleting it again destroys it, and the archive is where archiving
// and a swipe put it. The archive's own name on the mail server is not *Archive*, because a mail server marks whichever
// folder it likes with that role, and a tree draws the role's name over it. Both start empty, so a list of either holds
// exactly what an act filed into it.
const trash = {
    alias: 'TRASH',
    role: 'Trash',
    path: ['Trash'],
    storedEmailCount: 0,
    unreadEmailCount: 0,
    synchronizationState: 'Synchronized',
    lastSynchronizedAt: '2026-08-31T09:41:00+00:00',
    behind: false,
};

const filed = {
    alias: 'FILED',
    role: 'Archive',
    path: ['Filed'],
    storedEmailCount: 0,
    unreadEmailCount: 0,
    synchronizationState: 'Synchronized',
    lastSynchronizedAt: '2026-08-31T09:41:00+00:00',
    behind: false,
};

/**
 * What the folders route answers at rest: one mailbox with an inbox, its drafts, a folder nested under another, and the
 * archive and the trash its acts file into.
 */
export const mailFolders = {
    synchronizationEnabled: true,
    accounts: [{ account: workAccount, folders: [inbox, draftsFolder, archive2024, filed, trash] }],
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
        { account: workAccount, folders: [inbox, draftsFolder, archive2024, emptyFolder] },
        {
            account: failingAccount,
            folders: [
                { ...inbox, storedEmailCount: 61, unreadEmailCount: 4, synchronizationState: 'Failing', behind: true },
            ],
        },
        { account: accountBehind, folders: [{ ...inbox, storedEmailCount: 812, unreadEmailCount: 0, behind: true }] },
    ],
};

/**
 * The zone the signed-in person's day is read in, as the deployment answers before they have chosen one.
 *
 * The default rather than a chosen zone, because nobody in this corpus has chosen one: a client told a zone was the
 * person's own would place every instant against it instead of against the machine it runs on.
 */
export const ownTimeZone = { timeZone: 'UTC', isDefault: true };

/**
 * The folders of the corpus mailbox as the management route publishes them, with the acts the mailbox allows.
 *
 * The same levels the folders route answers — the inbox, the drafts, the level nothing is bound to, the folder beneath
 * it, the archive, and the trash — because the two routes are two readings of one mailbox. The acts are the mailbox's
 * rather than the credential's: the route reads under the grant to read mail, and the client narrows what it reports by
 * the grant it holds, so a check whose credential changes nothing is still offered nothing. Making a folder is the
 * mailbox's act rather than any one folder's, so it is stated once for the account, and a folder playing a role is
 * nested under and never renamed, moved, or deleted. The identities are the aliases the folders route answers, because
 * that is what a tree pairs a row with its acts by, and nothing may read meaning into one.
 */
export const managedFolders = {
    allowedActs: ['Create'],
    creatableRoles: [],
    folders: [
        { id: 'INBOX', parentId: null, name: 'INBOX', role: 'Inbox', allowedActs: [] },
        { id: 'DRAFTS', parentId: null, name: 'Drafts', role: 'Drafts', allowedActs: [] },
        {
            id: 'ARCHIVE',
            parentId: null,
            name: 'Archive',
            role: null,
            allowedActs: ['Rename', 'Move', 'Delete'],
        },
        {
            id: 'ARCHIVE/2024',
            parentId: 'ARCHIVE',
            name: '2024',
            role: null,
            allowedActs: ['Rename', 'Move', 'Delete'],
        },
        { id: 'FILED', parentId: null, name: 'Filed', role: 'Archive', allowedActs: [] },
        { id: 'TRASH', parentId: null, name: 'Trash', role: 'Trash', allowedActs: [] },
    ],
};

/** What a deployment offers somebody signing in at rest: a password and nothing else, which is what every other check reads. */
export const signInMethods = {
    acceptsPassword: true,
    authorizationServers: [],
};

/** The issuer of the authorization server this corpus's operator runs, on a host reserved so nothing can reach it. */
export const authorizationServerIssuer = 'https://sso.example.test/realms/nordwind';

/**
 * The same deployment with every way in published, which is the screen a person actually meets on a configured one.
 *
 * It is four servers beside the password because that is what the design draws the screen from: the deployment's own
 * identity provider above everything, three third parties in the grid under it, and the form below that. `self` is the
 * first one's name and is a drawing instruction rather than a role — MailFathom issues no token whatever it is set to.
 * Every other name is the provider's own short one, which is what a client matches the marks it ships against, and the
 * words under each are the deployment's, which is the whole reason a display name exists beside that name.
 */
export const signInMethodsOffered = {
    acceptsPassword: true,
    authorizationServers: [
        {
            issuer: 'https://id.nordwind.example/realms/staff',
            name: 'self',
            displayName: 'Nordwind SSO',
            clientId: 'mailfathom-client',
        },
        {
            issuer: 'https://github.example.invalid',
            name: 'github',
            displayName: 'GitHub',
            clientId: 'mailfathom-client',
        },
        {
            issuer: 'https://accounts.gmail.example.invalid',
            name: 'gmail',
            displayName: 'Gmail',
            clientId: 'mailfathom-client',
        },
        {
            issuer: authorizationServerIssuer,
            name: 'keycloak',
            displayName: 'Keycloak',
            clientId: 'mailfathom-client',
        },
    ],
};

/** What the deployment's own RFC 9728 document says a token has to be issued for, and which scopes to ask for. */
export const protectedResource = {
    resource: 'https://mail.example.test/api/client',
    scopes_supported: ['mailfathom.mail.read', 'offline_access'],
};

/**
 * What {@link authorizationServerIssuer} publishes about itself, which is all a client is ever given its addresses by.
 *
 * It is the one value here that belongs to somebody other than the deployment, and it is in this corpus for the reason
 * everything else is: a screen a person signs in through cannot be reached without it, and a second copy written in a
 * spec would be a second place to be wrong about a document RFC 8414 fixes the shape of.
 */
export const authorizationServerMetadata = {
    issuer: authorizationServerIssuer,
    authorization_endpoint: `${authorizationServerIssuer}/protocol/openid-connect/auth`,
    token_endpoint: `${authorizationServerIssuer}/protocol/openid-connect/token`,
    revocation_endpoint: `${authorizationServerIssuer}/protocol/openid-connect/revoke`,
    userinfo_endpoint: `${authorizationServerIssuer}/protocol/openid-connect/userinfo`,
};

/** What that server answers a redeemed code with. It is issued by nothing and belongs to nobody. */
export const issuedToken = {
    access_token: 'browser-suite-access-token',
    token_type: 'Bearer',
    expires_in: 3600,
    refresh_token: 'browser-suite-refresh-token',
};

/** @see issuedToken */
export const expectedGrantAuthorization = `Bearer ${issuedToken.access_token}`;

/** What that server answers the refresh token above with: a new access token, and a new refresh token in its place. */
export const renewedToken = {
    access_token: 'browser-suite-renewed-access-token',
    token_type: 'Bearer',
    expires_in: 3600,
    refresh_token: 'browser-suite-renewed-refresh-token',
};

/** @see renewedToken */
export const expectedRenewedGrantAuthorization = `Bearer ${renewedToken.access_token}`;
