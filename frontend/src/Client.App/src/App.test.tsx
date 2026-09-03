// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { StrictMode } from 'react';
import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { ClientRequest, ClientResponse, ClientSession, DeploymentAddress } from '@mailfathom/client-backend';
import { App } from './App';
import type { ClientDeployment } from './deployment/adoptedDeployment';
import { telemetryKey } from './device/deviceStore';
import { AttachmentDeliveryContext, type AttachmentDelivery } from './deployment/attachmentDelivery';
import { AttachmentUploadContext, type AttachmentUpload } from './deployment/attachmentUpload';
import type { PortraitExchange } from './deployment/portraitExchange';
import type { DeploymentTransport } from './deployment/sendToDeployment';
import { LocalizationProvider } from './localization/Localization';
import { localeNames, locales, readStoredLocale } from './localization/locale';
import { startingListWidth, storeListWidth } from './mailSpace/listWidth';
import type { CredentialLifetime, CredentialStore } from './signIn/credentialStore';
import { mostReconnectionAttempts } from './shell/useConnection';
import { noTelemetry, TelemetryContext, type ClientEvent, type ClientTelemetry } from './telemetry/clientTelemetry';
import { ThemeProvider } from './theme/Theme';
import { LinkOpenerContext } from './shellOperations/linkOpener';
import { WorkspaceProvider } from './workspace/Workspace';

// The network boundary is the transport, and the credential this run holds is the store — both arrive as props, so a
// test supplies each and nothing patches `fetch`, starts a server, or replaces a module. What is under test stays the
// real request, the real parsing, and the real failure mapping, and only the answers they are given are the test's.

type Answer = Omit<ClientResponse, 'headers'> & { readonly headers?: Readonly<Record<string, string>> };

/** What a deployment answers a caller it accepts, which is what proves the address is MailFathom and the password works. */
const accepted = sessionAnswering(['mailfathom.mail.read', 'mailfathom.mail.ask']);

/**
 * A deployment reporting itself, what it grants the credential that just reached it, and whether it forwards the
 * client's own telemetry. The last of those is what decides whether the client records anything at all, so a test
 * about telemetry states it and every other test takes a deployment that forwards it.
 */
function sessionAnswering(permissions: readonly string[], telemetry = true): Answer {
    return {
        status: 200,
        body: JSON.stringify({ service: 'MailFathom', version: '0.8.7', permissions, telemetry }),
    };
}

// The two challenges a MailFathom surface answers a refusal with, in one header value: the bearer one every deployment
// produces, and the password one beside it where the deployment accepts passwords.
const challenged = 'Bearer realm="MailFathom", Basic realm="MailFathom", charset="UTF-8"';

const workAccount = {
    id: 'work',
    displayName: 'Work',
    synchronizationState: 'Synchronized',
    lastSynchronizedAt: '2026-08-31T09:41:00+00:00',
    behind: false,
};

/** What a run opens already holding, where something was kept for it. */
const heldCredential = 'Basic dGVzdDpzZWNyZXQ=';

/** Who `heldCredential` names, which is what the device's remembered telemetry answer is kept under. */
const heldPerson = 'test';

/** What the screen composes out of what `signIn` below types, which is what a test asserts was kept and presented. */
const typedCredential = 'Basic b3duZXI6b3BlbiBzZXNhbWU=';

function directory(synchronizationEnabled: boolean, accounts: readonly unknown[]): Answer {
    return { status: 200, body: JSON.stringify({ synchronizationEnabled, accounts }) };
}

function complete(answer: Answer): ClientResponse {
    return { status: answer.status, body: answer.body, headers: answer.headers ?? {} };
}

// Every request the doubles below were asked, in the order they were asked. `StrictMode` invokes the effect that reads
// the accounts twice on mount, as React does in development and as `main.tsx` therefore does here, so a repeat of a
// route already asked for is the mode rather than the screen — what these tests are about is which routes those are.
const asked: ClientRequest[] = [];

function routesAsked(): string[] {
    return [...new Set(asked.map((request) => request.path))];
}

// The folder the message list reads once Mail is on the screen. Empty, because what these tests are about is the frame
// rather than the list: the list has its own file, and a folder with mail in it here would be a second copy of it.
const emptyFolder: Answer = {
    status: 200,
    body: JSON.stringify({ emails: [], nextCursor: null, previousCursor: null, pageSize: 100 }),
};

/**
 * A deployment that accepts any credential and answers the session and the accounts with what a test named.
 *
 * The preferences route answers nothing readable unless a test states it, which is what every test that is not about
 * a preference wants: the client then draws the unset document rather than one this file would have to keep in step
 * with the deployment's own. A test about a preference states the answer and gets it.
 */
function deploymentAnswering(
    accounts: Answer = directory(true, [workAccount]),
    session: Answer = accepted,
    preferences: Answer | null = null,
): DeploymentTransport {
    return () => (request) => {
        asked.push(request);

        if (request.path.includes('/emails')) {
            return Promise.resolve(complete(emptyFolder));
        }

        if (preferences !== null && request.path.endsWith('/preferences')) {
            return Promise.resolve(complete(preferences));
        }

        return Promise.resolve(complete(request.path.endsWith('/session') ? session : accounts));
    };
}

/** The whole preferences document, because the route answers all of it whether or not anything was ever set. */
function preferencesAnswering(telemetryEnabled: boolean): Answer {
    return {
        status: 200,
        body: JSON.stringify({
            telemetryEnabled,
            theme: 'system',
            openMailInTabs: false,
            markReadOnOpen: true,
            expandWholeThread: false,
        }),
    };
}

/** The one message the reading pane reads, drawn from the closed tree rather than from any markup the sender wrote. */
const drawnMessage: Answer = {
    status: 200,
    body: JSON.stringify({
        storedEmailId: '00000000-0000-4000-8000-000000000000',
        availability: 'Readable',
        plainText: { text: 'As words.', originalCharacterCount: 9, truncation: 'None' },
        document: {
            schemaVersion: 1,
            blocks: [
                {
                    type: 'paragraph',
                    version: 1,
                    content: [{ text: 'A drawn message.', emphasis: 'None', foreground: null, link: null }],
                    alignment: 'Inherited',
                },
            ],
            refusal: 'None',
            removedRemoteReferenceCount: 0,
            retainedRemoteImageCount: 0,
            inlineImageCount: 0,
            undrawnInlineImageCount: 0,
            truncated: false,
        },
        remoteImagesRequested: false,
    }),
};

/** What the message route answers with, which is everything the pane draws around a body it never carries. */
const describedMessage: Answer = {
    status: 200,
    body: JSON.stringify({
        storedEmailId: '00000000-0000-4000-8000-000000000000',
        account: 'work',
        folder: 'INBOX',
        threadId: null,
        sizeOctets: 4_096,
        headers: {
            subject: 'Quarterly invoice',
            sentAt: '2026-08-31T09:41:00+00:00',
            receivedAt: '2026-08-31T09:41:10+00:00',
            participants: [{ role: 'From', address: 'billing@example.invalid', displayName: 'Billing' }],
            messageId: 'abc@example.invalid',
            inReplyTo: null,
            references: [],
        },
        body: { availability: 'Readable', plainText: true, html: true },
        sender: { authorAuthentication: 'Authenticated', deploymentTrust: 'Unknown', authenticatedDomain: null },
        attachments: [],
        carried: null,
        unread: true,
        flagged: false,
        answered: false,
    }),
};

// The folder the message the pane draws stands in, so that opening it is the act it is in the client: a reader picks a
// row out of the list, and the pane draws what that row named.
const folderWithOneMessage: Answer = {
    status: 200,
    body: JSON.stringify({
        emails: [
            {
                id: '00000000-0000-4000-8000-000000000000',
                account: 'work',
                folder: 'INBOX',
                threadId: null,
                subject: 'Quarterly invoice',
                receivedAt: '2026-08-31T09:41:10+00:00',
                sentAt: '2026-08-31T09:41:00+00:00',
                senderAddress: 'billing@example.invalid',
                senderDisplayName: 'Billing',
                toAddresses: ['owner@example.invalid'],
                unread: true,
                flagged: false,
                answered: false,
                hasAttachments: false,
                attachmentCount: 0,
                sizeOctets: 4_096,
                preview: 'The invoice for August.',
            },
        ],
        nextCursor: null,
        previousCursor: null,
        pageSize: 100,
    }),
};

/**
 * A deployment that answers the accounts as the one above does, and both reads the reading pane in Mail makes.
 *
 * The two are separate routes because they are separately expensive, so the double answers them separately as well —
 * a description that also served a body would prove the pane against an exchange the service does not have.
 */
function deploymentDrawingAMessage(session: Answer = accepted): DeploymentTransport {
    const otherwise = deploymentAnswering(directory(true, [workAccount]), session);

    return (signal) => (request) => {
        if (request.path.includes('/emails')) {
            asked.push(request);

            return Promise.resolve(complete(folderWithOneMessage));
        }

        // The one route here that changes a mailbox. It is answered rather than left to fall through, because a
        // submission the deployment did not write down is one the frame stops claiming — which would make an assertion
        // about the row pass for a client that never marked anything.
        if (request.path.endsWith('/mutations/flags')) {
            asked.push(request);

            return Promise.resolve(complete(flagsRecorded(request.body ?? '{}')));
        }

        if (!request.path.includes('/messages/')) {
            return otherwise(signal)(request);
        }

        asked.push(request);

        return Promise.resolve(complete(request.path.includes('/body') ? drawnMessage : describedMessage));
    };
}

/** Every change a batch named, written down, which is what a deployment holding the grant answers with. */
function flagsRecorded(stated: string): Answer {
    const changes = (JSON.parse(stated) as { changes: readonly { storedEmailId: string }[] }).changes;

    return {
        status: 200,
        body: JSON.stringify({
            results: changes.map(({ storedEmailId }) => ({ storedEmailId, outcome: 'recorded' })),
        }),
    };
}

/**
 * The same deployment, answering for somebody whose preferences say they open mail in tabs.
 *
 * It is the deployment's answer rather than a value handed to the frame because that is where the preference lives:
 * the strip is on the screen when this person's own record says so and the window is wide enough for it, and both
 * halves of that are what these tests are about.
 */
function deploymentWorkingInTabs(): DeploymentTransport {
    const otherwise = deploymentDrawingAMessage();

    return (signal) => (request) => {
        if (!request.path.endsWith('/preferences')) {
            return otherwise(signal)(request);
        }

        asked.push(request);

        return Promise.resolve(
            complete({
                status: 200,
                body: JSON.stringify({
                    telemetryEnabled: false,
                    theme: 'system',
                    openMailInTabs: true,
                    markReadOnOpen: true,
                    expandWholeThread: false,
                }),
            }),
        );
    };
}

// The same message, threaded, beside the conversation it belongs to. It is a second double rather than an option on the
// first because what it proves is the frame wiring three screens together: a row opens a message, the message opens its
// conversation, and closing the conversation returns to the message the workspace still holds.
const conversationThreadId = '9b2a1c74-4a4e-4c93-9a2e-3f6f0a1b2c3d';

function threaded(answer: Answer): Answer {
    return { ...answer, body: answer.body.replace('"threadId":null', `"threadId":"${conversationThreadId}"`) };
}

const drawnConversation: Answer = {
    status: 200,
    body: JSON.stringify({
        threadId: conversationThreadId,
        messages: [
            {
                position: 0,
                answeredId: null,
                email: {
                    id: '00000000-0000-4000-8000-000000000000',
                    account: 'work',
                    folder: 'INBOX',
                    threadId: conversationThreadId,
                    subject: 'Quarterly invoice',
                    receivedAt: '2026-08-31T09:41:10+00:00',
                    sentAt: '2026-08-31T09:41:00+00:00',
                    senderAddress: 'billing@example.invalid',
                    senderDisplayName: 'Billing',
                    toAddresses: ['owner@example.invalid'],
                    unread: true,
                    flagged: false,
                    answered: false,
                    hasAttachments: false,
                    attachmentCount: 0,
                    sizeOctets: 4_096,
                    preview: 'The invoice for August.',
                },
            },
        ],
        participants: [{ address: 'billing@example.invalid', displayName: 'Billing', messageCount: 1 }],
        messageCount: 1,
        moreMessagesNotAssembled: false,
        moreParticipantsNotNamed: false,
        nextCursor: null,
        pageSize: 100,
    }),
};

/** A deployment whose one message belongs to a conversation, and which answers that conversation as well. */
function deploymentDrawingAConversation(): DeploymentTransport {
    const otherwise = deploymentAnswering();

    return (signal) => (request) => {
        if (request.path.includes('/emails')) {
            asked.push(request);

            return Promise.resolve(complete(threaded(folderWithOneMessage)));
        }

        if (request.path.includes('/threads/')) {
            asked.push(request);

            return Promise.resolve(complete(drawnConversation));
        }

        if (!request.path.includes('/messages/')) {
            return otherwise(signal)(request);
        }

        asked.push(request);

        return Promise.resolve(complete(request.path.includes('/body') ? drawnMessage : threaded(describedMessage)));
    };
}

/** A delivery nobody in these tests asks for, supplied because a row below the frame reads one from the context. */
const deliversNothing: AttachmentDelivery = () => Promise.resolve('delivered');
const uploadsNothing: AttachmentUpload = () => Promise.resolve(null);

/** A deployment answering every route the same way, which is how a refusal to sign anybody in is stated. */
function deploymentRefusing(answer: Answer): DeploymentTransport {
    return () => (request) => {
        asked.push(request);

        return Promise.resolve(complete(answer));
    };
}

interface RecordingStore extends CredentialStore {
    /** What this store holds, by deployment, so a test asserts on what was kept rather than on what was called. */
    readonly kept: Map<string, string>;
}

function storeKeeping(lifetime: CredentialLifetime = 'untilTheTabCloses'): RecordingStore {
    const kept = new Map<string, string>();

    return {
        kept,
        lifetime,
        read: (deployment) => Promise.resolve(kept.get(deployment.baseAddress) ?? null),
        keep: (deployment, authorization) => {
            kept.set(deployment.baseAddress, authorization);

            return Promise.resolve(true);
        },
        forget: (deployment) => {
            kept.delete(deployment.baseAddress);

            return Promise.resolve(true);
        },
    };
}

/** A store that will not write, which is a keychain locked between being found and being written to. */
function storeRefusingToKeep(): RecordingStore {
    const store = storeKeeping('untilSignedOut');

    return { ...store, keep: () => Promise.resolve(false) };
}

/** A store that holds the credential and will not give it up, which is a locked keychain from the client's side. */
function storeRefusingToForget(): RecordingStore {
    const store = storeKeeping('untilSignedOut');

    return { ...store, forget: () => Promise.resolve(false) };
}

// What `main.tsx` resolves at the edge and hands down. The origin that served the client is the case a web head is in,
// and it is the default here because the screens below are about the spaces rather than about where the deployment is.
const servingAddress: DeploymentAddress = { baseAddress: 'https://mail.example.invalid' };

const servedFrom: ClientDeployment = {
    outcome: 'resolved',
    adopted: { deployment: servingAddress, origin: 'serving' },
    clearTextPermitted: null,
};

function chose(baseAddress: string): ClientDeployment {
    return {
        outcome: 'resolved',
        adopted: { deployment: { baseAddress }, origin: 'chosen' },
        clearTextPermitted: null,
    };
}

/** What a deployment configured, which is the shape a client somebody was handed opens in. */
function wasConfiguredWith(baseAddress: string, clearTextPermitted: boolean | null = null): ClientDeployment {
    return {
        outcome: 'resolved',
        adopted: { deployment: { baseAddress }, origin: 'configured' },
        clearTextPermitted,
    };
}

/** Nothing at all: no configuration, nothing stored, and nothing that served the client from a deployment. */
const nothingAdopted: ClientDeployment = { outcome: 'resolved', adopted: null, clearTextPermitted: null };

// The application is mounted the way `main.tsx` mounts it, `StrictMode` and all five providers included. Nothing below
// the frame may decide the language, the theme, what the person is carrying, or how a followed link leaves the
// application, so a test that supplied fewer would be proving a second arrangement — and the mode is half of that
// arrangement rather than a detail of it: it invokes every effect twice on mount, which is the difference between a
// screen that behaves and one that behaves the first time.
// The portrait is the one read this frame makes that does not go through a transport, octets not being text. Nothing
// here is about a picture, so the exchange answers that there is none and refuses both writes as unreachable.
const drawsNobody: PortraitExchange = {
    read: () => Promise.resolve({ outcome: 'none' }),
    replace: () => Promise.resolve({ outcome: 'refused', reason: 'unavailable' }),
    remove: () => Promise.resolve({ outcome: 'refused', reason: 'unavailable' }),
};

function renderApp(
    deployment: ClientDeployment = servedFrom,
    signedInWith: string | null = heldCredential,
    send: DeploymentTransport = deploymentAnswering(),
    credentials: CredentialStore = storeKeeping(),
    telemetry: ClientTelemetry = noTelemetry,
): void {
    render(
        <StrictMode>
            <LocalizationProvider>
                <ThemeProvider>
                    <WorkspaceProvider>
                        <LinkOpenerContext value={() => Promise.resolve()}>
                            <AttachmentDeliveryContext value={deliversNothing}>
                                <AttachmentUploadContext value={uploadsNothing}>
                                    <TelemetryContext value={telemetry}>
                                        <App
                                            credentials={credentials}
                                            deployment={deployment}
                                            portraits={drawsNobody}
                                            send={send}
                                            signedInWith={signedInWith}
                                        />
                                    </TelemetryContext>
                                </AttachmentUploadContext>
                            </AttachmentDeliveryContext>
                        </LinkOpenerContext>
                    </WorkspaceProvider>
                </ThemeProvider>
            </LocalizationProvider>
        </StrictMode>,
    );
}

/** A telemetry double holding what the frame asked it to record, which is what these tests read back. */
function telemetryRecording(): {
    readonly telemetry: ClientTelemetry;
    readonly exportedFor: (ClientSession | null)[];
    readonly permitted: boolean[];
    readonly stopped: number[];
    readonly events: ClientEvent[];
} {
    const exportedFor: (ClientSession | null)[] = [];
    const permitted: boolean[] = [];
    const stopped: number[] = [];
    const events: ClientEvent[] = [];

    return {
        telemetry: {
            exportFor: (session, allowed) => {
                permitted.push(allowed);

                const started = exportedFor.push(session);

                return () => stopped.push(started);
            },
            navigated: () => undefined,
            happened: (event) => {
                events.push(event);
            },
        },
        exportedFor,
        permitted,
        stopped,
        events,
    };
}

// The frame is on the screen once the summary above the space has an answer to state, which is what every test that
// acts on the frame waits for rather than for a timer.
async function framed(): Promise<void> {
    await screen.findByText('Every account is up to date.');
}

function typeAddress(entry: string): void {
    fireEvent.change(screen.getByRole('textbox', { name: 'Server' }), { target: { value: entry } });
}

function signIn(userName = 'owner', password = 'open sesame'): void {
    fireEvent.change(screen.getByRole('textbox', { name: 'Login' }), { target: { value: userName } });
    fireEvent.change(screen.getByLabelText('Password'), { target: { value: password } });
    fireEvent.click(screen.getByRole('button', { name: 'Connect' }));
}

function openingAt(address: string): void {
    window.history.replaceState(null, '', address);
}

async function goTo(space: string): Promise<void> {
    // Matched on the start of the name rather than the whole of it: a space with nothing behind it yet says so in its
    // own accessible name, and this helper is used to reach both kinds.
    fireEvent.click(screen.getByRole('link', { name: new RegExp(`^${space}`, 'u') }));

    await screen.findByRole('main', { name: space });
}

// The frame is read at the width the workspace opens out at, which is what every composition below was written
// against: jsdom lays nothing out, so the width is answered here rather than measured. A query about anything else —
// the machine's colour scheme — is answered the way the suite's own setup answers it, with nothing matching.
const declaredMatchMedia = Object.getOwnPropertyDescriptor(window, 'matchMedia');

beforeEach(() => {
    // Cleared here as well as after each test, because a read the previous test started can answer while its tree is
    // still being torn down and start one more — which lands in this record after the teardown emptied it, and reads
    // as this test having asked for a route it never reached.
    asked.length = 0;
    openingAt('/');
    Object.defineProperty(window, 'matchMedia', {
        configurable: true,
        value: (query: string) => ({
            media: query,
            matches: query.includes('min-width'),
            addEventListener: () => undefined,
            removeEventListener: () => undefined,
        }),
    });
});

afterEach(() => {
    if (declaredMatchMedia !== undefined) {
        Object.defineProperty(window, 'matchMedia', declaredMatchMedia);
    }

    vi.useRealTimers();
    vi.restoreAllMocks();
    openingAt('/');
    asked.length = 0;
    window.localStorage.clear();
    window.sessionStorage.clear();
    document.documentElement.removeAttribute('lang');
    document.documentElement.removeAttribute('data-theme');
});

describe('App', () => {
    it('says it is reaching the deployment while nothing has answered', () => {
        renderApp(servedFrom, heldCredential, () => () => new Promise<ClientResponse>(() => undefined));

        expect(screen.getByText('Reaching your deployment…')).toBeDefined();
    });

    it('says it is reading the accounts once the deployment has said what the credential may do', async () => {
        const send: DeploymentTransport = () => (request) =>
            request.path.endsWith('/session')
                ? Promise.resolve(complete(accepted))
                : new Promise<ClientResponse>(() => undefined);

        renderApp(servedFrom, heldCredential, send);

        expect(await screen.findByText('Reading accounts…')).toBeDefined();
    });

    it('opens in Discover when the address names no space', async () => {
        renderApp();

        expect(await screen.findByRole('heading', { name: 'Discover', level: 1 })).toBeDefined();
    });

    it('writes the space it is showing into an address that named none, so it can be reloaded', async () => {
        renderApp();

        await waitFor(() => {
            expect(window.location.hash).toBe('#/discover');
        });
    });

    it('corrects an address naming a space the client does not have, rather than showing one it does not name', async () => {
        openingAt('#/nowhere');

        renderApp();

        expect(await screen.findByRole('heading', { name: 'Discover', level: 1 })).toBeDefined();
        await waitFor(() => {
            expect(window.location.hash).toBe('#/discover');
        });
    });

    it.each(['Mail', 'Cases'])('opens in %s when that is what the address names', async (space) => {
        openingAt(`#/${space.toLowerCase()}`);

        renderApp();

        expect(await screen.findByRole('main', { name: space })).toBeDefined();
    });

    it('draws the message a row of the list opened, which is what the frame wires the two together for', async () => {
        renderApp(servedFrom, heldCredential, deploymentDrawingAMessage());
        await framed();

        await goTo('Mail');

        const list = await screen.findByRole('listbox', { name: 'Messages' });
        fireEvent.pointerDown(within(list).getByRole('option', { name: /Quarterly invoice/ }));

        expect(await screen.findByText('A drawn message.')).toBeDefined();
    });

    it('names what a person working in tabs has opened, in a strip above the mail', async () => {
        renderApp(servedFrom, heldCredential, deploymentWorkingInTabs());
        await framed();

        await goTo('Mail');

        const list = await screen.findByRole('listbox', { name: 'Messages' });
        fireEvent.pointerDown(within(list).getByRole('option', { name: /Quarterly invoice/ }));

        expect(await screen.findByRole('tab', { name: 'Quarterly invoice' })).toBeDefined();
        expect(await screen.findByText('A drawn message.')).toBeDefined();
    });

    it('says nothing is open to a person working in tabs who has opened none', async () => {
        renderApp(servedFrom, heldCredential, deploymentWorkingInTabs());
        await framed();

        await goTo('Mail');

        expect(await screen.findByText('Nothing is open')).toBeDefined();
        expect(screen.queryByRole('tablist')).toBeNull();
    });

    it('opens mail in the reading column, and draws no strip, for somebody who has not asked for tabs', async () => {
        renderApp(servedFrom, heldCredential, deploymentDrawingAMessage());
        await framed();

        await goTo('Mail');

        const list = await screen.findByRole('listbox', { name: 'Messages' });
        fireEvent.pointerDown(within(list).getByRole('option', { name: /Quarterly invoice/ }));

        expect(await screen.findByText('A drawn message.')).toBeDefined();
        expect(screen.queryByRole('tablist')).toBeNull();
    });

    it('divides the mail space where the person now signed in last left it, rather than where anybody did', async () => {
        // The width is stored against the person the held credential names, so this is the one assertion that the name
        // the frame takes the credential apart for is the name the store was written under. Everything below the frame
        // is handed that name and could not tell a wrong one from a right one.
        storeListWidth('test', 468);

        renderApp();
        await framed();

        await goTo('Mail');

        expect(
            (await screen.findByRole('separator', { name: 'Message list width' })).getAttribute('aria-valuenow'),
        ).toBe('468');
    });

    it('divides it where it starts for somebody else signing in on the same machine', async () => {
        storeListWidth('test', 468);

        renderApp(servedFrom, 'Basic YW5vdGhlcjpzZWNyZXQ=');
        await framed();

        await goTo('Mail');

        expect(
            (await screen.findByRole('separator', { name: 'Message list width' })).getAttribute('aria-valuenow'),
        ).toBe(String(startingListWidth));
    });

    it('opens the conversation a message belongs to, and returns to that message when it is closed', async () => {
        renderApp(servedFrom, heldCredential, deploymentDrawingAConversation());
        await framed();

        await goTo('Mail');

        const list = await screen.findByRole('listbox', { name: 'Messages' });
        fireEvent.pointerDown(within(list).getByRole('option', { name: /Quarterly invoice/ }));

        fireEvent.click(await screen.findByRole('button', { name: 'Show the whole conversation' }));

        const conversation = await screen.findByRole('region', { name: 'Conversation' });
        expect(within(conversation).getByText('Messages in this conversation: 1')).toBeDefined();

        fireEvent.click(within(conversation).getByRole('button', { name: 'Back to the message' }));

        expect(await screen.findByText('A drawn message.')).toBeDefined();
        expect(screen.queryByRole('region', { name: 'Conversation' })).toBeNull();

        // Returning to the message is a navigation rather than a landing, so it places the reader in what it drew.
        await waitFor(() => {
            expect(document.activeElement).toBe(screen.getByRole('article', { name: /Quarterly invoice/ }));
        });
    });

    it('returns to the message from the sender own markup, and places the reader in it', async () => {
        renderApp(servedFrom, heldCredential, deploymentDrawingAConversation());
        await framed();

        await goTo('Mail');

        const list = await screen.findByRole('listbox', { name: 'Messages' });
        fireEvent.pointerDown(within(list).getByRole('option', { name: /Quarterly invoice/ }));

        fireEvent.click(await screen.findByRole('button', { name: 'Show the full HTML version' }));
        fireEvent.click(screen.getByRole('button', { name: 'Show the HTML' }));

        const surface = await screen.findByRole('region', { name: "The sender's own version of this message" });

        fireEvent.click(within(surface).getByRole('button', { name: 'Close this view' }));

        expect(await screen.findByText('A drawn message.')).toBeDefined();

        // The same rule the conversation above is held to, and the one the two surfaces would otherwise disagree on:
        // what decides it is that *something* was in front of the message rather than which of the two it was, so a
        // reader leaving this one is placed exactly as a reader leaving that one is.
        await waitFor(() => {
            expect(document.activeElement).toBe(screen.getByRole('article', { name: /Quarterly invoice/ }));
        });
    });

    // Three things decide whether opening a message marks it read, and all three are the frame's: the reader's own
    // setting, the grant the credential signed in under, and there being a session to submit over. Nothing below the
    // frame asks the question, so this is where each of them is proven.
    it('marks read a message a row opened, where the credential may write a flag', async () => {
        renderApp(
            servedFrom,
            heldCredential,
            deploymentDrawingAMessage(
                sessionAnswering(['mailfathom.mail.read', 'mailfathom.mail.ask', 'mailfathom.mail.flags.write']),
            ),
        );
        await framed();

        await goTo('Mail');

        const list = await screen.findByRole('listbox', { name: 'Messages' });
        fireEvent.pointerDown(within(list).getByRole('option', { name: /Quarterly invoice/ }));
        await screen.findByText('A drawn message.');

        await waitFor(() => {
            expect(
                asked.some(
                    (request) =>
                        request.path === 'https://mail.example.invalid/api/client/mutations/flags' &&
                        request.method === 'POST',
                ),
            ).toBe(true);
        });
    });

    it('marks nothing where the credential was never granted a flag write, and says so rather than failing', async () => {
        renderApp(servedFrom, heldCredential, deploymentDrawingAMessage());
        await framed();

        await goTo('Mail');

        const list = await screen.findByRole('listbox', { name: 'Messages' });
        fireEvent.pointerDown(within(list).getByRole('option', { name: /Quarterly invoice/ }));
        await screen.findByText('A drawn message.');

        expect(routesAsked().some((path) => path.includes('/mutations/'))).toBe(false);

        // An absence nobody explained is a client that looks broken, which is what the notice strip is for — so the
        // sentence is asserted on the screen rather than the withholding being asserted on its own.
        expect(
            screen.getByText(
                'This credential may not change a flag on your mail server, so opening a message leaves it unread there and this client shows what the server last reported. Whoever runs the deployment can grant that.',
            ),
        ).toBeDefined();
    });

    it('draws no message until a row of the list opens one', async () => {
        renderApp(servedFrom, heldCredential, deploymentDrawingAMessage());
        await framed();

        await goTo('Mail');
        await screen.findByRole('listbox', { name: 'Messages' });

        expect(screen.getByText('Open a message to read it here.')).toBeDefined();
        expect(routesAsked().some((path) => path.includes('/messages/'))).toBe(false);
    });

    it('reads no message while the space on the screen is not Mail', async () => {
        renderApp(servedFrom, heldCredential, deploymentDrawingAMessage());
        await framed();

        expect(routesAsked().some((path) => path.includes('/messages/'))).toBe(false);
    });

    it('shows the space whose link was activated, and marks it as the current one', async () => {
        renderApp();
        await screen.findByRole('heading', { name: 'Discover', level: 1 });

        await goTo('Cases');

        expect(screen.getByRole('link', { current: 'page' }).textContent).toBe('Cases');
    });

    it('keeps the question and the mailbox in scope while the person moves between spaces', async () => {
        renderApp();
        await framed();

        fireEvent.change(screen.getByRole('searchbox', { name: 'Ask your mail' }), {
            target: { value: 'the renewal Nordwind sent' },
        });
        fireEvent.change(screen.getByRole('combobox', { name: 'Mailbox in scope' }), { target: { value: 'work' } });

        await goTo('Mail');

        expect(screen.getByRole('searchbox', { name: 'Ask your mail' })).toHaveProperty(
            'value',
            'the renewal Nordwind sent',
        );
        expect(screen.getByRole('combobox', { name: 'Mailbox in scope' })).toHaveProperty('value', 'work');
    });

    it('offers every mailbox the owner holds as a scope, beside all of them at once', async () => {
        const twoMailboxes = directory(true, [workAccount, { ...workAccount, id: 'archive', displayName: 'Archive' }]);

        renderApp(servedFrom, heldCredential, deploymentAnswering(twoMailboxes));

        const scope = await screen.findByRole('combobox', { name: 'Mailbox in scope' });
        await waitFor(() => {
            expect([...scope.querySelectorAll('option')].map((option) => option.textContent)).toEqual([
                'All mailboxes',
                'Work',
                'Archive',
            ]);
        });
    });

    it('says the deployment is not refreshing these accounts when it is not, as its setting rather than a grant', async () => {
        renderApp(servedFrom, heldCredential, deploymentAnswering(directory(false, [workAccount])));

        expect(
            await screen.findByText(
                'This deployment is not refreshing the local copy of these accounts, so what you see is as current as its last run left it. That is a setting on the deployment rather than a permission you are missing.',
            ),
        ).toBeDefined();
    });

    it('reports why the accounts could not be read instead of saying nothing about them', async () => {
        renderApp(servedFrom, heldCredential, deploymentAnswering({ status: 403, body: '' }));

        expect(await screen.findByText('The accounts could not be read: unauthorized.')).toBeDefined();
    });

    it('reads the accounts again when the person asks it to, rather than only on a reload', async () => {
        let accounts: Answer = { status: 503, body: '' };
        const deployment: DeploymentTransport = () => (request) =>
            Promise.resolve(complete(request.path.endsWith('/session') ? accepted : accounts));

        renderApp(servedFrom, heldCredential, deployment);
        const retry = await screen.findByRole('button', { name: 'Try again' });

        accounts = directory(true, [workAccount]);
        fireEvent.click(retry);

        expect(await screen.findByText('Every account is up to date.')).toBeDefined();
    });

    it('reads its mail with the credential it holds rather than with one written into the client', async () => {
        renderApp(servedFrom, typedCredential);
        await framed();

        expect([...new Set(asked.map((request) => request.headers['Authorization']))]).toEqual([typedCredential]);
    });
});

describe('App session', () => {
    /** A deployment that grants the credential exactly these names, and answers the accounts normally. */
    function granting(...permissions: readonly string[]): DeploymentTransport {
        return deploymentAnswering(directory(true, [workAccount]), sessionAnswering(permissions));
    }

    it('offers only the spaces the grant permits, rather than ones the deployment would refuse', async () => {
        renderApp(servedFrom, heldCredential, granting('mailfathom.mail.read'));
        await framed();

        expect(screen.getAllByRole('link').map((space) => space.textContent)).toEqual([
            'Mail',
            'Cases',
            'Tasks',
            'Calendar',
            'People',
        ]);
    });

    it('says what the credential may not do, so an absence is not read as a client that is broken', async () => {
        renderApp(servedFrom, heldCredential, granting('mailfathom.mail.read'));
        await framed();

        expect(
            screen.getByText(
                'This credential may not ask questions of your mail on this deployment, so asking is not offered. Whoever runs the deployment can grant that.',
            ),
        ).toBeDefined();
    });

    it('offers nothing to ask with where the credential may not ask', async () => {
        renderApp(servedFrom, heldCredential, granting('mailfathom.mail.read'));
        await framed();

        expect(screen.queryByRole('searchbox', { name: 'Ask your mail' })).toBeNull();
    });

    it('never asks for the mail a credential may not read, rather than letting the read be refused', async () => {
        renderApp(servedFrom, heldCredential, granting('mailfathom.mail.ask'));
        await screen.findByRole('heading', { name: 'Discover', level: 1 });

        await waitFor(() => {
            expect(routesAsked()).toEqual(['https://mail.example.invalid/api/client/session']);
        });
        expect(screen.queryByText(/The accounts could not be read/)).toBeNull();
    });

    it('answers an address naming a space this credential may not open with one it may', async () => {
        openingAt('#/discover');

        renderApp(servedFrom, heldCredential, granting('mailfathom.mail.read'));

        expect(await screen.findByRole('main', { name: 'Mail' })).toBeDefined();
        await waitFor(() => {
            expect(window.location.hash).toBe('#/mail');
        });
    });

    it('tells a credential granted nothing everything it may not do, rather than leaving it to guess', async () => {
        renderApp(servedFrom, heldCredential, granting());

        expect(await screen.findByText(/This credential may not read mail on this deployment/)).toBeDefined();
        expect(screen.getByText(/This credential may not ask questions of your mail/)).toBeDefined();
        expect(screen.queryByRole('link', { name: 'Mail' })).toBeNull();
    });

    it('reaches for a deployment that did not answer on its own, and says which attempt it is on', async () => {
        vi.useFakeTimers({ shouldAdvanceTime: true });

        renderApp(servedFrom, heldCredential, deploymentRefusing({ status: 503, body: '' }));
        await screen.findByText(/Trying again — attempt 1 of/);

        await act(async () => {
            await vi.advanceTimersByTimeAsync(10_000);
        });

        expect(screen.getByText(/Trying again — attempt 2 of/)).toBeDefined();
    });

    it('stops reaching once the budget is spent, and hands the way out to the person', async () => {
        vi.useFakeTimers({ shouldAdvanceTime: true });

        renderApp(servedFrom, heldCredential, deploymentRefusing({ status: 503, body: '' }));
        await screen.findByText(/Trying again — attempt 1 of/);

        // One pass per wait in the budget, because each attempt is only scheduled once the one before it has answered:
        // a single long advance would find no timer to fire past the first.
        for (let attempt = 0; attempt < mostReconnectionAttempts; attempt += 1) {
            await act(async () => {
                await vi.advanceTimersByTimeAsync(60_000);
            });
        }

        expect(await screen.findByText(/Your deployment has not answered after/)).toBeDefined();
        expect(screen.getByRole('button', { name: 'Try again' })).toBeDefined();
    });

    it('asks a deployment for nothing while this machine has no network, and says that rather than blaming it', () => {
        vi.spyOn(window.navigator, 'onLine', 'get').mockReturnValue(false);

        renderApp();

        expect(
            screen.getByText('This machine is offline. The client reconnects on its own when the network comes back.'),
        ).toBeDefined();
        expect(asked).toEqual([]);
    });

    it('reads again on its own when the network comes back, without anybody restarting the client', async () => {
        const connected = vi.spyOn(window.navigator, 'onLine', 'get').mockReturnValue(false);

        renderApp();
        await screen.findByText(/This machine is offline\./);

        connected.mockReturnValue(true);
        fireEvent(window, new Event('online'));

        await framed();
    });

    it('offers the next person nothing of the last one until their own grant has been read', async () => {
        renderApp(servedFrom, null, deploymentAnswering(), storeKeeping());
        signIn();
        await framed();
        expect(screen.getByRole('navigation', { name: 'Spaces' })).toBeDefined();

        // No network from here on, so nothing can arrive to replace what is on the screen: what has to clear it is the
        // credential changing rather than an answer about the new one.
        vi.spyOn(window.navigator, 'onLine', 'get').mockReturnValue(false);
        fireEvent(window, new Event('offline'));

        fireEvent.click(screen.getByRole('button', { name: 'Sign out', hidden: true }));
        signIn('somebody', 'else');
        await screen.findByText(/This machine is offline\./);

        expect(within(screen.getByRole('navigation', { name: 'Spaces' })).queryAllByRole('link')).toEqual([]);
    });

    it('reports the deployment it is reading from beside the client it is running', async () => {
        renderApp();
        await framed();

        expect(screen.getByText(`Client ${__MAILFATHOM_VERSION__}, deployment 0.8.7`)).toBeDefined();
    });

    it('names each account and what its last attempt did, behind the line that summarizes them', async () => {
        const failing = { ...workAccount, id: 'news', displayName: 'Newsletters', synchronizationState: 'Unreachable' };

        renderApp(servedFrom, heldCredential, deploymentAnswering(directory(true, [workAccount, failing])));

        // The one gesture the design asks for: the reading is closed when the frame is drawn, and this is what a
        // person does to it.
        fireEvent.click(await screen.findByText('Some accounts stopped synchronizing.'));

        // Scoped to the panel, because the mailbox in scope offers the same names and this is about the freshness
        // reading rather than about the field beside it.
        const panel = within(screen.getByRole('group'));
        expect(screen.getByRole('group')).toHaveProperty('open', true);
        expect(panel.getByText('Work')).toBeDefined();
        expect(panel.getByText('Up to date')).toBeDefined();
        expect(panel.getByText('Newsletters')).toBeDefined();
        expect(panel.getByText('The mail server did not answer')).toBeDefined();
    });

    it('tells an owner holding no account what would fill it, rather than showing a failure', async () => {
        renderApp(servedFrom, heldCredential, deploymentAnswering(directory(true, [])));

        expect(await screen.findByText(/No mail account is configured for this owner yet\./)).toBeDefined();
        expect(
            screen.getByText(/Whoever runs this deployment declares which mailboxes it reads for you/),
        ).toBeDefined();
    });
});

describe('App sign-in', () => {
    it('asks for a login and a password when nothing has been signed in with', () => {
        renderApp(servedFrom, null);

        expect(screen.getByRole('textbox', { name: 'Login' })).toBeDefined();
        expect(screen.getByLabelText('Password')).toBeDefined();
        expect(screen.queryByRole('navigation', { name: 'Spaces' })).toBeNull();
    });

    it('asks for nothing but the credential where the origin that served the client is the deployment', () => {
        renderApp(servedFrom, null);

        expect(screen.queryByRole('textbox', { name: 'Server' })).toBeNull();
    });

    it('opens the frame once the credential somebody typed has been accepted', async () => {
        renderApp(servedFrom, null);

        signIn();

        await framed();
        expect(screen.getByRole('navigation', { name: 'Spaces' })).toBeDefined();
    });

    it('presents the credential it composed rather than anything it was handed', async () => {
        renderApp(servedFrom, null);

        signIn();
        await framed();

        expect([...new Set(asked.map((request) => request.headers['Authorization']))]).toEqual([typedCredential]);
    });

    it('keeps the credential it signed in with, so a later start opens already signed in', async () => {
        const credentials = storeKeeping();

        renderApp(servedFrom, null, deploymentAnswering(), credentials);
        signIn();
        await framed();

        expect([...credentials.kept]).toEqual([[servingAddress.baseAddress, typedCredential]]);
    });

    it('says how long the password will be kept before anybody has typed one', () => {
        renderApp(servedFrom, null, deploymentAnswering(), storeKeeping('untilSignedOut'));

        expect(
            screen.getByText(
                'Your password is kept in this machine’s keychain until you sign out. Signing out is what removes it.',
            ),
        ).toBeDefined();
    });

    it('says why it will ask again where nothing may be kept beyond the run', () => {
        renderApp(servedFrom, null, deploymentAnswering(), storeKeeping('untilTheClientCloses'));

        expect(
            screen.getByText(
                'Your password is kept until you close MailFathom, and you will be asked for it again — this machine offers no keychain to keep it in safely.',
            ),
        ).toBeDefined();
    });

    it('reports a refused credential as one thing rather than as a guess about which half was wrong', async () => {
        const refusing = deploymentRefusing({ status: 401, body: '', headers: { 'www-authenticate': challenged } });

        renderApp(servedFrom, null, refusing);
        signIn();

        expect(await screen.findByText('The login or the password is not accepted by this deployment.')).toBeDefined();
    });

    it('tells somebody whose deployment offers no passwords that, rather than refusing their credential', async () => {
        const bearerOnly = { status: 401, body: '', headers: { 'www-authenticate': 'Bearer realm="MailFathom"' } };

        renderApp(servedFrom, null, deploymentRefusing(bearerOnly));
        signIn();

        expect(
            await screen.findByText(
                'This deployment does not accept a login and a password. Whoever runs it has to enable that before you can sign in here.',
            ),
        ).toBeDefined();
    });

    it('names neither the password nor the value composed from it when the deployment refuses it', async () => {
        const refusing = deploymentRefusing({ status: 401, body: '', headers: { 'www-authenticate': challenged } });

        renderApp(servedFrom, null, refusing);
        signIn('owner', 'open sesame');
        await screen.findByRole('alert');

        expect(document.body.textContent).not.toContain('open sesame');
        expect(document.body.textContent).not.toContain('b3duZXI6b3BlbiBzZXNhbWU=');
    });

    it('puts somebody in front of the sign-in when the deployment stops accepting what was kept', async () => {
        const credentials = storeKeeping();
        await credentials.keep(servingAddress, typedCredential);

        renderApp(servedFrom, typedCredential, deploymentAnswering({ status: 401, body: '' }), credentials);

        expect(
            await screen.findByText('This deployment has stopped accepting the password that was kept. Sign in again.'),
        ).toBeDefined();
        expect([...credentials.kept]).toEqual([]);
    });

    it('clears what the person carried between the spaces when the deployment stops accepting what was kept', async () => {
        // One deployment whose answer to the accounts changes under the client, which is what a service restarted with
        // a different password looks like from here: the read that was working starts refusing.
        let accounts: Answer = { status: 503, body: '' };
        const send: DeploymentTransport = () => (request) => {
            asked.push(request);

            return Promise.resolve(complete(request.path.endsWith('/session') ? accepted : accounts));
        };

        renderApp(servedFrom, null, send, storeKeeping());
        signIn();
        await screen.findByRole('button', { name: 'Try again' });

        fireEvent.change(screen.getByRole('searchbox', { name: 'Ask your mail' }), {
            target: { value: 'the renewal Nordwind sent' },
        });

        accounts = { status: 401, body: '' };
        fireEvent.click(screen.getByRole('button', { name: 'Try again' }));
        await screen.findByText('This deployment has stopped accepting the password that was kept. Sign in again.');

        // Being turned away mid-session returns this person to the sign-in exactly as signing out does, so what they
        // were carrying goes with the credential there too rather than waiting for the next person to read.
        accounts = directory(true, [workAccount]);
        signIn();
        await framed();

        expect(screen.getByRole('searchbox', { name: 'Ask your mail' })).toHaveProperty('value', '');
    });

    it('says the password is still on the machine when the deployment stops accepting it and the store will not', async () => {
        renderApp(servedFrom, typedCredential, deploymentAnswering({ status: 401, body: '' }), storeRefusingToForget());

        // Two things went wrong at once and both are the person's to act on: the deployment no longer accepts what was
        // kept, and the store would not give it up — so it is read back on every later start until they remove it.
        expect(
            await screen.findByText('This deployment has stopped accepting the password that was kept. Sign in again.'),
        ).toBeDefined();
        expect(
            await screen.findByText(
                'Signing out did not remove the password from this machine’s credential store, so it is still kept there. Remove it in the store itself, or sign in and out again.',
            ),
        ).toBeDefined();
    });

    it('clears the credential and everything read with it when somebody signs out', async () => {
        const credentials = storeKeeping();

        renderApp(servedFrom, null, deploymentAnswering(), credentials);
        signIn();
        await framed();

        fireEvent.click(screen.getByRole('button', { name: 'Sign out', hidden: true }));

        expect(screen.getByRole('textbox', { name: 'Login' })).toBeDefined();
        expect(screen.queryByRole('navigation', { name: 'Spaces' })).toBeNull();
        expect([...credentials.kept]).toEqual([]);
    });

    it('clears what the person carried between the spaces when somebody signs out', async () => {
        renderApp(servedFrom, null, deploymentAnswering(), storeKeeping());
        signIn();
        await framed();

        fireEvent.change(screen.getByRole('searchbox', { name: 'Ask your mail' }), {
            target: { value: 'the renewal Nordwind sent' },
        });
        fireEvent.change(screen.getByRole('combobox', { name: 'Mailbox in scope' }), { target: { value: 'work' } });

        fireEvent.click(screen.getByRole('button', { name: 'Sign out', hidden: true }));
        signIn();
        await framed();

        // The next person to sign in on this machine reads their own empty screen rather than the last one's question
        // and the mailbox it was scoped to.
        expect(screen.getByRole('searchbox', { name: 'Ask your mail' })).toHaveProperty('value', '');
        expect(screen.getByRole('combobox', { name: 'Mailbox in scope' })).toHaveProperty('value', '');
    });

    it('says the password was not kept when the store would not write it, without refusing the sign-in', async () => {
        renderApp(servedFrom, null, deploymentAnswering(), storeRefusingToKeep());
        signIn();
        await framed();

        // The screen promised how long the password would last before anybody typed, so a store that refused the write
        // says so — inside the frame, because signing in worked and only the keeping did not.
        expect(
            screen.getByText(
                'Your password could not be stored on this machine, so you will be asked for it again the next time you open MailFathom. You are signed in either way.',
            ),
        ).toBeDefined();
    });

    it('says the password is still on the machine when the store would not remove it', async () => {
        renderApp(servedFrom, null, deploymentAnswering(), storeRefusingToForget());
        signIn();
        await framed();

        fireEvent.click(screen.getByRole('button', { name: 'Sign out', hidden: true }));

        // Signing out told them the password would be removed, so a store that refused has to say so rather than let
        // the next start read it back while they believe it is gone.
        expect(
            await screen.findByText(
                'Signing out did not remove the password from this machine’s credential store, so it is still kept there. Remove it in the store itself, or sign in and out again.',
            ),
        ).toBeDefined();
    });

    it('places focus on what it has to say about the credential, rather than on the field below it', async () => {
        renderApp(servedFrom, typedCredential, deploymentAnswering({ status: 401, body: '' }), storeKeeping());

        const notice = await screen.findByText(
            'This deployment has stopped accepting the password that was kept. Sign in again.',
        );

        // Each of these sentences is inserted in the same commit as its own text, which a live region does not
        // announce — so somebody signed out mid-session would otherwise land in the form with nothing read to them.
        //
        // Waited for rather than read once the sentence is on the screen: placing focus is an effect, and an effect
        // runs after the commit that inserted the text this awaited. The two are the same commit and not the same
        // moment, and asserting on the earlier one is how this passes on an idle machine and fails on a busy one.
        await waitFor(() => {
            expect(document.activeElement).toBe(notice.parentElement);
        });
    });

    it('places focus on the credential where the deployment is already known', () => {
        renderApp(servedFrom, null);

        expect(document.activeElement).toBe(screen.getByRole('textbox', { name: 'Login' }));
    });

    it('puts focus at the start of the workspace once somebody has signed in, rather than leaving it behind', async () => {
        renderApp(servedFrom, null);

        signIn();
        await framed();

        expect(document.activeElement?.contains(screen.getByRole('main'))).toBe(true);
    });
});

describe('App deployment', () => {
    it('reads from the deployment it was pointed at, rather than from one written into the client', async () => {
        renderApp(chose('https://elsewhere.example.invalid'));
        await framed();

        await waitFor(() => {
            expect(routesAsked()).toEqual([
                'https://elsewhere.example.invalid/api/client/session',
                'https://elsewhere.example.invalid/api/client/accounts',
                'https://elsewhere.example.invalid/api/client/preferences',
                'https://elsewhere.example.invalid/api/client/display-name',
            ]);
        });
    });

    // A deployment that configured this client wrongly is said out loud rather than worked around: every control on
    // the sign-in screen is about a connection this run has already been refused, so offering the form would invite a
    // password against an address the client will not use.
    it('says what a deployment configured wrongly, in place of the form, rather than asking for a password', () => {
        renderApp({ outcome: 'refused', refusal: 'clearTextContradictsAddress' }, null);

        expect(screen.getByRole('heading', { name: 'This client is configured wrongly' })).toBeDefined();
        expect(
            screen.getByText(
                'It was told to permit an unsecured connection and given an https address, which are two different answers to one question. Remove whichever of the two is wrong.',
            ),
        ).toBeDefined();
        expect(screen.queryByRole('textbox', { name: 'Server' })).toBeNull();
        expect(screen.queryByRole('button', { name: 'Connect' })).toBeNull();
    });

    // The form somebody was on their way to filling is gone, which is a view change like any other: focus left on the
    // document would tab past the one sentence saying why there is nothing to fill.
    it('puts focus at the start of the refusal, rather than leaving it where the form would have been', () => {
        renderApp({ outcome: 'refused', refusal: 'addressMalformed' }, null);

        expect(document.activeElement).toBe(
            screen.getByRole('heading', { name: 'This client is configured wrongly' }).parentElement,
        );
    });

    it('says where the two settings are read from, there being no way out of that screen from inside the client', () => {
        renderApp({ outcome: 'refused', refusal: 'addressMalformed' }, null);

        expect(
            screen.getByText(
                'Both settings are read from the arguments MailFathom was started with, from its environment, and from client.conf beside its own configuration, in that order.',
            ),
        ).toBeDefined();
    });

    // An address a deployment configured is not one changing an address could move, so the client is not offered as
    // something to point elsewhere — which is the same reason an origin that served the client is not.
    it('offers no way out of a configured address, that being nobody on this machine’s to change', () => {
        renderApp(wasConfiguredWith('https://configured.example.invalid'), null);

        expect(screen.getByRole<HTMLInputElement>('textbox', { name: 'Server' }).value).toBe(
            'https://configured.example.invalid',
        );
        expect(screen.queryByRole('button', { name: 'Point somewhere else' })).toBeNull();
    });

    it('asks for an address when nothing has said where the deployment is', () => {
        renderApp(nothingAdopted, null);

        expect(screen.getByRole('textbox', { name: 'Server' })).toBeDefined();
        expect(screen.queryByRole('navigation', { name: 'Spaces' })).toBeNull();
    });

    it('places focus on the address where that is the first thing it is asking for', () => {
        renderApp(nothingAdopted, null);

        expect(document.activeElement).toBe(screen.getByRole('textbox', { name: 'Server' }));
    });

    it('signs in against the address somebody named, in the same act as naming it', async () => {
        renderApp(nothingAdopted, null);

        typeAddress('mail.example.test');
        signIn();
        await framed();

        await waitFor(() => {
            expect(routesAsked()).toEqual([
                'https://mail.example.test/api/client/session',
                'https://mail.example.test/api/client/accounts',
                'https://mail.example.test/api/client/preferences',
                'https://mail.example.test/api/client/display-name',
            ]);
        });
    });

    it('offers to be pointed elsewhere once somebody named the deployment themselves', async () => {
        renderApp(chose('https://mail.example.invalid'));
        await framed();

        expect(screen.getByRole('button', { name: 'Point somewhere else', hidden: true })).toBeDefined();
    });

    it('offers a way out of the sign-in screen a chosen deployment left behind', () => {
        renderApp(chose('https://mail.example.invalid'), null);

        // A chosen address renders no address field, and it is read back out of storage on every later start — so
        // without this, somebody whose password no longer works has no way to point the client anywhere else.
        fireEvent.click(screen.getByRole('button', { name: 'Point somewhere else', hidden: true }));

        expect(screen.getByRole('textbox', { name: 'Server' })).toBeDefined();
    });

    it('offers nothing to change on the sign-in screen the origin that served the client left', () => {
        renderApp(servedFrom, null);

        expect(screen.queryByRole('button', { name: 'Point somewhere else', hidden: true })).toBeNull();
    });

    it('offers nothing to change where the origin that served the client is the deployment', async () => {
        renderApp();
        await framed();

        expect(screen.queryByRole('button', { name: 'Point somewhere else', hidden: true })).toBeNull();
    });

    it('asks for an address again, and shows no space, once it is pointed somewhere else', async () => {
        renderApp(chose('https://mail.example.invalid'));
        await framed();

        fireEvent.click(screen.getByRole('button', { name: 'Point somewhere else', hidden: true }));

        expect(screen.getByRole('textbox', { name: 'Server' })).toBeDefined();
        expect(screen.queryByRole('navigation', { name: 'Spaces' })).toBeNull();
    });

    it('calls off an attempt whose deployment was abandoned while it ran', async () => {
        const credentials = storeKeeping();
        const held: (() => void)[] = [];
        let answering = false;

        // A deployment that answers nothing until this test says so, and everything from then on: what the attempt is
        // holding on is the first request, and what would carry it through to signing somebody in is the rest of them
        // answering normally once it resumes.
        const send: DeploymentTransport = () => (request) => {
            asked.push(request);
            const answer = complete(request.path.endsWith('/session') ? accepted : directory(true, [workAccount]));

            if (answering) {
                return Promise.resolve(answer);
            }

            return new Promise((resolved) => {
                held.push(() => {
                    resolved(answer);
                });
            });
        };

        renderApp(chose('https://mail.example.invalid'), null, send, credentials);
        signIn();

        // The way out of a chosen address sits above the form and stays live while an attempt runs. An answer for the
        // address somebody has just pointed away from would sign them back in to it and write the credential into the
        // store that was asked to clear it.
        fireEvent.click(screen.getByRole('button', { name: 'Point somewhere else', hidden: true }));
        answering = true;
        for (const answer of held) {
            answer();
        }

        expect(await screen.findByRole('textbox', { name: 'Server' })).toBeDefined();
        await waitFor(() => {
            expect(screen.queryByRole('navigation', { name: 'Spaces' })).toBeNull();
        });
        expect([...credentials.kept]).toEqual([]);
    });

    it('forgets the credential of the deployment it is pointed away from', async () => {
        const credentials = storeKeeping();
        const chosen = chose('https://mail.example.invalid');
        const pointedAt = (chosen.outcome === 'resolved' ? chosen.adopted?.deployment : null) ?? servingAddress;

        // Seeded against the address this test itself chose rather than against the serving fixture that happens to
        // spell the same one: the two are different origins, and a test that passed on the coincidence would stop
        // proving what its name says the moment either literal moved.
        await credentials.keep(pointedAt, heldCredential);

        renderApp(chosen, heldCredential, deploymentAnswering(), credentials);
        await framed();

        fireEvent.click(screen.getByRole('button', { name: 'Point somewhere else', hidden: true }));

        expect([...credentials.kept]).toEqual([]);
    });

    it('leaves focus where the document opened it when the credential was already held', async () => {
        renderApp();
        await framed();

        // A cold start is not a view change. `main.tsx` mounts under `StrictMode`, so every effect runs twice here as
        // it does under `pnpm dev`, and a guard that only survives one invocation would have pulled focus by now.
        expect(document.activeElement).toBe(document.body);
    });

    it('puts focus back in the address when it is pointed somewhere else', async () => {
        renderApp(chose('https://mail.example.invalid'));
        await framed();

        fireEvent.click(screen.getByRole('button', { name: 'Point somewhere else', hidden: true }));

        expect(document.activeElement).toBe(screen.getByRole('textbox', { name: 'Server' }));
    });

    it('starts an empty workspace when somebody signs in, rather than handing them the last person’s', async () => {
        // Written before the provider mounts, which is how a tab that was not signed out keeps what was on the screen:
        // a second person signing into the same tab must not find the first one's question, mailbox, or reading
        // position waiting for them.
        window.sessionStorage.setItem(
            'mailfathom.workspace',
            JSON.stringify({
                scope: { kind: 'account', accountId: 'work' },
                collapsed: [],
                selection: 'AAMkAD-42',
                selected: ['AAMkAD-42'],
                question: 'what did the last person ask',
            }),
        );

        renderApp(servedFrom, null);
        signIn();
        await framed();

        expect(screen.getByRole('searchbox', { name: 'Ask your mail' })).toHaveProperty('value', '');
        expect(screen.getByRole('combobox', { name: 'Mailbox in scope' })).toHaveProperty('value', '');
    });

    it('signs in against the next deployment of its own, and never reads the one before it again', async () => {
        renderApp(chose('https://first.example.invalid'));
        await framed();

        fireEvent.click(screen.getByRole('button', { name: 'Point somewhere else', hidden: true }));
        typeAddress('second.example.invalid');
        signIn();
        await framed();

        await waitFor(() => {
            expect(routesAsked()).toEqual([
                'https://first.example.invalid/api/client/session',
                'https://first.example.invalid/api/client/accounts',
                'https://first.example.invalid/api/client/preferences',
                'https://first.example.invalid/api/client/display-name',
                'https://second.example.invalid/api/client/session',
                'https://second.example.invalid/api/client/accounts',
                'https://second.example.invalid/api/client/preferences',
                'https://second.example.invalid/api/client/display-name',
            ]);
        });
    });
});

// Inside the frame the language and the telemetry decision are made on the settings screen rather than in the menu
// that leads to it, which is where the design project puts them — so a test about either opens that screen the way a
// person does, on the tab that holds both, which is the second of its two.
function openSettings(): void {
    fireEvent.click(screen.getByRole('button', { name: 'Settings', hidden: true }));
    fireEvent.click(screen.getByRole('tab', { name: 'Application', hidden: true }));
}

describe('App language', () => {
    it('offers each language under its own name, and no other', () => {
        renderApp();
        openSettings();

        const offered = within(screen.getByRole('group', { name: 'Language' })).getAllByRole('radio');

        expect(offered.map((choice) => choice.closest('label')?.textContent)).toEqual(
            locales.map((locale) => localeNames[locale]),
        );
    });

    it('rewrites the screen when another language is chosen, without anything being restarted', async () => {
        renderApp();
        await screen.findByRole('heading', { name: 'Discover', level: 1 });
        openSettings();

        fireEvent.click(screen.getByRole('radio', { name: localeNames.pl }));

        expect(screen.getByRole('heading', { name: 'Odkrywaj', level: 1 })).toBeDefined();
        expect(document.documentElement.lang).toBe('pl');
    });

    it('remembers the choice, so a later run of either head opens in it', () => {
        renderApp();
        openSettings();

        fireEvent.click(screen.getByRole('radio', { name: localeNames.pl }));

        expect(readStoredLocale()).toBe('pl');
    });
});

describe('App telemetry', () => {
    it('exports under the session that is signed in, and records that it began', async () => {
        const recording = telemetryRecording();

        renderApp(servedFrom, heldCredential, deploymentAnswering(), storeKeeping(), recording.telemetry);
        await framed();

        expect(recording.exportedFor.map((session) => session?.baseAddress)).toContain(servingAddress.baseAddress);
        expect(recording.events).toContain('session_started');
    });

    it('stops exporting when the person signs out', async () => {
        const recording = telemetryRecording();

        renderApp(servedFrom, heldCredential, deploymentAnswering(), storeKeeping(), recording.telemetry);
        await framed();
        fireEvent.click(screen.getByRole('button', { name: 'Sign out', hidden: true }));

        // Nothing is exported for somebody who is not signed in, so the pipeline the session held is asked to end and
        // what replaces it is asked to export for nobody.
        await waitFor(() => {
            expect(recording.stopped.length).toBeGreaterThan(0);
        });
        expect(recording.exportedFor.at(-1)).toBeNull();
    });

    it('records a credential the deployment has stopped accepting', async () => {
        const recording = telemetryRecording();
        const credentials = storeKeeping();
        await credentials.keep(servingAddress, typedCredential);

        renderApp(
            servedFrom,
            typedCredential,
            deploymentAnswering({ status: 401, body: '' }),
            credentials,
            recording.telemetry,
        );
        await screen.findByText('This deployment has stopped accepting the password that was kept. Sign in again.');

        expect(recording.events).toContain('credential_no_longer_accepted');
    });

    // A deployment that forwards nothing is not known to forward nothing until it says so, and until then the client
    // records into the buffer #1230 holds — which is why what is asserted is where this ends rather than that it never
    // permitted anything. The pipeline throws away what it held when that answer arrives, which `exporting.test.ts`
    // and `holding.test.ts` are what prove.
    it('stops recording against a deployment that says it forwards no telemetry', async () => {
        const recording = telemetryRecording();

        renderApp(
            servedFrom,
            heldCredential,
            deploymentAnswering(undefined, sessionAnswering(['mailfathom.mail.read', 'mailfathom.mail.ask'], false)),
            storeKeeping(),
            recording.telemetry,
        );
        await framed();

        expect(recording.permitted.at(-1)).toBe(false);
    });

    // What a restart owes somebody who turned it off on this machine: the decision is honoured from the first effect
    // rather than for the second it takes an answer to come back, and it stands for as long as no answer does.
    it('honours a decision this device remembers while the deployment has answered nothing', async () => {
        window.localStorage.setItem(telemetryKey(heldPerson), 'false');

        const recording = telemetryRecording();

        renderApp(servedFrom, heldCredential, deploymentAnswering(), storeKeeping(), recording.telemetry);
        await framed();

        expect(recording.permitted).not.toContain(true);
        expect(recording.events).not.toContain('session_started');
    });

    // The other half of that: the device's copy is a cache and not a second opinion, so the deployment's own answer
    // replaces it — which is what makes a decision taken on one machine reach this one.
    it('lets the deployment replace what this device remembers once it answers', async () => {
        window.localStorage.setItem(telemetryKey(heldPerson), 'false');

        const recording = telemetryRecording();

        renderApp(
            servedFrom,
            heldCredential,
            deploymentAnswering(undefined, accepted, preferencesAnswering(true)),
            storeKeeping(),
            recording.telemetry,
        );
        await framed();

        expect(recording.permitted[0]).toBe(false);
        await waitFor(() => {
            expect(recording.permitted.at(-1)).toBe(true);
        });
    });

    // What began is the session rather than the recording, so moving the switch off and on again reports nothing a
    // second time. The guard is a ref rather than a derived value because the record is an event and not a state.
    it('reports a session beginning once across a switch moved twice', async () => {
        const recording = telemetryRecording();

        renderApp(
            servedFrom,
            heldCredential,
            deploymentAnswering(undefined, accepted, preferencesAnswering(true)),
            storeKeeping(),
            recording.telemetry,
        );
        await framed();
        openSettings();

        const withhold = screen.getByRole('switch', { name: /Do not send telemetry/ });

        fireEvent.click(withhold);
        await waitFor(() => {
            expect(recording.permitted.at(-1)).toBe(false);
        });

        fireEvent.click(withhold);
        await waitFor(() => {
            expect(recording.permitted.at(-1)).toBe(true);
        });

        expect(recording.events.filter((event) => event === 'session_started')).toHaveLength(1);
    });
});
