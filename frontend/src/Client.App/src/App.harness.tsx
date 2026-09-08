// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { StrictMode } from 'react';
import { fireEvent, render, screen } from '@testing-library/react';
import { afterEach, beforeEach, vi } from 'vitest';
import {
    sessionExchangeRoute,
    sessionRevocationRoute,
    type ClientRequest,
    type ClientResponse,
    type ClientSession,
    type DeploymentAddress,
    type MailFathomSignalChannel,
    type SignalStreamSchedule,
} from '@mailfathom/client-backend';
import { App } from './App';
import { Containment } from './containment/Containment';
import type { ClientDeployment } from './deployment/adoptedDeployment';
import { AttachmentExchangeContext, type AttachmentExchange } from './deployment/attachmentExchange';
import { AttachmentUploadContext, type AttachmentUpload } from './deployment/attachmentUpload';
import type { PortraitExchange } from './deployment/portraitExchange';
import type { DeploymentTransport } from './deployment/sendToDeployment';
import { LocalizationProvider } from './localization/Localization';
import type { CredentialLifetime, CredentialStore } from './signIn/credentialStore';
import type { KeptSession } from './signIn/keptSession';
import { noTelemetry, TelemetryContext, type ClientEvent, type ClientTelemetry } from './telemetry/clientTelemetry';
import { ThemeProvider } from './theme/Theme';
import { ToastsProvider } from './toasts/Toasts';
import { LinkOpenerContext } from './shellOperations/linkOpener';
import { SystemNotifierContext, type SystemNotifier } from './shellOperations/systemNotifier';
import { WorkspaceProvider } from './workspace/Workspace';

// The frame every `App.*.test.tsx` file mounts, and the doubles it is mounted over. It is a module rather than a
// second test file because each of those files proves one group of behaviours against the same arrangement, and a
// copy of this per group is what would let two of them quietly drift into proving different frames.
//
// The network boundary is the transport, and the credential a run holds is the store — both arrive as props, so a
// test supplies each and nothing patches `fetch`, starts a server, or replaces a module. What is under test stays the
// real request, the real parsing, and the real failure mapping, and only the answers they are given are the test's.

export type Answer = Omit<ClientResponse, 'headers'> & { readonly headers?: Readonly<Record<string, string>> };

/** What a deployment answers a caller it accepts, which is what proves the address is MailFathom and the password works. */
export const accepted = sessionAnswering(['mailfathom.mail.read', 'mailfathom.mail.ask']);

/**
 * A deployment reporting itself, what it grants the credential that just reached it, and whether it forwards the
 * client's own telemetry. The last of those is what decides whether the client records anything at all, so a test
 * about telemetry states it and every other test takes a deployment that forwards it.
 */
export function sessionAnswering(permissions: readonly string[], telemetry = true): Answer {
    return {
        status: 200,
        body: JSON.stringify({ service: 'MailFathom', version: '0.8.7', permissions, telemetry }),
    };
}

// The two challenges a MailFathom surface answers a refusal with, in one header value: the bearer one every deployment
// produces, and the password one beside it where the deployment accepts passwords.
export const challenged = 'Bearer realm="MailFathom", Basic realm="MailFathom", charset="UTF-8"';

export const workAccount = {
    id: 'work',
    displayName: 'Work',
    synchronizationState: 'Synchronized',
    lastSynchronizedAt: '2026-08-31T09:41:00+00:00',
    behind: false,
};

// Far enough out that nothing in a test renews a session on its own: renewal is its own file, and a fixture that
// expired mid-run would put a second request in every test that is not about one.
const sessionExpiry = '2126-08-31T21:41:00+00:00';

/** Who a run opens already signed in as, which is what the device's remembered telemetry answer is kept under. */
export const heldPerson = 'test';

/** What a run opens already holding, where something was kept for it: a session rather than a password. */
export const heldSession: KeptSession = {
    authorization: 'Bearer mfs_heldsession.aGVsZC1zZXNzaW9uLXByb29m',
    expiresAt: sessionExpiry,
    person: heldPerson,
};

/** What the screen composes out of what `signIn` below types, which is the one request that presents a password. */
export const typedCredential = 'Basic dXNlcjpvcGVuIHNlc2FtZQ==';

/** Who `typedCredential` names. */
export const typedPerson = 'user';

/** The session kept for the person `typedCredential` names, where a test seeds one rather than typing the password. */
export const typedSession: KeptSession = {
    authorization: 'Bearer mfs_typedsession.dHlwZWQtc2Vzc2lvbi1wcm9vZg',
    expiresAt: sessionExpiry,
    person: typedPerson,
};

/** A session belonging to somebody else on this machine, which is what a per-person preference is read under. */
export const anotherPersonsSession: KeptSession = {
    authorization: 'Bearer mfs_anothersession.YW5vdGhlci1zZXNzaW9uLXByb29m',
    expiresAt: sessionExpiry,
    person: 'another',
};

/** Which token each credential a double has seen was minted for, so one credential is answered the same token twice. */
const mintedFor = new Map<string, string>();

/**
 * What a deployment double mints for a credential it accepts.
 *
 * A token per credential rather than one token, because that is the property the client depends on: a second person
 * signing in on the same machine gets a different session, and a fixture that minted one value for everybody would let
 * the frame carry the last person's grant into the next one without a test noticing.
 *
 * Numbered rather than derived from the credential, because a real deployment answers a value with nothing of the
 * credential in it: a double that spelled the password into the token would make a client that leaked one indetectable
 * here, which is the one thing these tests are for.
 */
export function mintedTokenFor(credential: string): string {
    const minted = mintedFor.get(credential) ?? `mfs_session${String(mintedFor.size + 1)}.bWludGVkLXNlc3Npb24`;

    mintedFor.set(credential, minted);

    return minted;
}

/** The header value the session minted for `typedCredential` is presented under. */
export const mintedCredential = `Bearer ${mintedTokenFor(typedCredential)}`;

/** What the client keeps once a deployment double took `typedCredential`: the session it was given, under who typed it. */
export const mintedKeptSession: KeptSession = {
    authorization: mintedCredential,
    expiresAt: sessionExpiry,
    person: typedPerson,
};

/** The exchange answer a deployment double gives the credential that reached it. */
export function mintedSession(request: ClientRequest): Answer {
    return {
        status: 200,
        body: JSON.stringify({
            token: mintedTokenFor(request.headers['Authorization'] ?? ''),
            expiresAt: sessionExpiry,
        }),
    };
}

export function directory(synchronizationEnabled: boolean, accounts: readonly unknown[]): Answer {
    return { status: 200, body: JSON.stringify({ synchronizationEnabled, accounts }) };
}

export function complete(answer: Answer): ClientResponse {
    return { status: answer.status, body: answer.body, headers: answer.headers ?? {} };
}

// Every request the doubles below were asked, in the order they were asked. `StrictMode` invokes the effect that reads
// the accounts twice on mount, as React does in development and as `main.tsx` therefore does here, so a repeat of a
// route already asked for is the mode rather than the screen — what these tests are about is which routes those are.
export const asked: ClientRequest[] = [];

export function routesAsked(): string[] {
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
export function deploymentAnswering(
    accounts: Answer = directory(true, [workAccount]),
    session: Answer = accepted,
    preferences: Answer | null = null,
): DeploymentTransport {
    return () => (request) => {
        asked.push(request);

        if (request.path.includes('/emails')) {
            return Promise.resolve(complete(emptyFolder));
        }

        if (request.path.endsWith(sessionExchangeRoute)) {
            return Promise.resolve(complete(mintedSession(request)));
        }

        if (request.path.endsWith(sessionRevocationRoute)) {
            return Promise.resolve(complete({ status: 204, body: '' }));
        }

        if (preferences !== null && request.path.endsWith('/preferences')) {
            return Promise.resolve(complete(preferences));
        }

        return Promise.resolve(complete(request.path.endsWith('/session') ? session : accounts));
    };
}

/** The whole preferences document, because the route answers all of it whether or not anything was ever set. */
export function preferencesAnswering(telemetryEnabled: boolean): Answer {
    return {
        status: 200,
        body: JSON.stringify({
            telemetryEnabled,
            theme: 'system',
            openMailInTabs: false,
            markReadOnOpen: true,
            expandWholeThread: false,
            embeddedHtmlMessages: false,
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
                toAddresses: ['user@example.invalid'],
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
export function deploymentDrawingAMessage(session: Answer = accepted): DeploymentTransport {
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
export function deploymentWorkingInTabs(): DeploymentTransport {
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
                    embeddedHtmlMessages: false,
                }),
            }),
        );
    };
}

// The same message, threaded, beside the conversation it belongs to. It is a second double rather than an option on the
// first because what it proves is the frame wiring three screens together: a row opens a message, the message opens its
// conversation, and closing the conversation returns to the message the workspace still holds.
const conversationThreadId = '9b2a1c74-4a4e-4c93-9a2e-3f6f0a1b2c3d';

/** One answer's body as the value it serializes to, so a conversation can carry what a route serves on its own. */
function parsed(answer: Answer): Readonly<Record<string, unknown>> {
    return JSON.parse(answer.body) as Readonly<Record<string, unknown>>;
}

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
                    toAddresses: ['user@example.invalid'],
                    unread: true,
                    flagged: false,
                    answered: false,
                    hasAttachments: false,
                    attachmentCount: 0,
                    sizeOctets: 4_096,
                    preview: 'The invoice for August.',
                },

                // The conversation route carries every message's own description and words, which is what makes
                // opening a correspondence one request rather than one per message drawn.
                message: parsed(threaded(describedMessage)),
                body: parsed(drawnMessage),
            },
        ],
        participants: [{ address: 'billing@example.invalid', displayName: 'Billing', messageCount: 1 }],
        messageCount: 1,
        moreMessagesNotAssembled: false,
        moreParticipantsNotNamed: false,
        nextCursor: null,
        pageSize: 10,
    }),
};

/** A deployment whose one message belongs to a conversation, and which answers that conversation as well. */
export function deploymentDrawingAConversation(): DeploymentTransport {
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

/** An exchange nobody in these tests asks for, supplied because a row below the frame reads one from the context. */
const deliversNothing: AttachmentExchange = {
    deliver: () => Promise.resolve('delivered'),
    read: () => Promise.resolve({ outcome: 'shown', content: '' }),
};

const uploadsNothing: AttachmentUpload = () => Promise.resolve(null);

/** The head a test runs in: one that offered no system notification, which is what jsdom is with no binding on it. */
const raisesNothing: SystemNotifier = {
    offered: false,
    standing: 'unasked',
    permit: () => Promise.resolve('unasked'),
    raise: () => Promise.resolve('unavailable'),
    whenActedOn: () => () => undefined,
};

/** A deployment answering every route the same way, which is how a refusal to sign anybody in is stated. */
export function deploymentRefusing(answer: Answer): DeploymentTransport {
    return () => (request) => {
        asked.push(request);

        return Promise.resolve(complete(answer));
    };
}

interface RecordingStore extends CredentialStore {
    /** What this store holds, by deployment, so a test asserts on what was kept rather than on what was called. */
    readonly kept: Map<string, string>;
}

export function storeKeeping(lifetime: CredentialLifetime = 'untilTheTabCloses'): RecordingStore {
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
export function storeRefusingToKeep(): RecordingStore {
    const store = storeKeeping('untilSignedOut');

    return { ...store, keep: () => Promise.resolve(false) };
}

/** A store that holds the credential and will not give it up, which is a locked keychain from the client's side. */
export function storeRefusingToForget(): RecordingStore {
    const store = storeKeeping('untilSignedOut');

    return { ...store, forget: () => Promise.resolve(false) };
}

// What `main.tsx` resolves at the edge and hands down. The origin that served the client is the case a web head is in,
// and it is the default here because most of what this family proves is about the spaces rather than about where the
// deployment is.
export const servingAddress: DeploymentAddress = { baseAddress: 'https://mail.example.invalid' };

export const servedFrom: ClientDeployment = {
    outcome: 'resolved',
    adopted: { deployment: servingAddress, origin: 'serving' },
    clearTextPermitted: null,
};

export function chose(baseAddress: string): ClientDeployment {
    return {
        outcome: 'resolved',
        adopted: { deployment: { baseAddress }, origin: 'chosen' },
        clearTextPermitted: null,
    };
}

/** What a deployment configured, which is the shape a client somebody was handed opens in. */
export function wasConfiguredWith(baseAddress: string, clearTextPermitted: boolean | null = null): ClientDeployment {
    return {
        outcome: 'resolved',
        adopted: { deployment: { baseAddress }, origin: 'configured' },
        clearTextPermitted,
    };
}

/** Nothing at all: no configuration, nothing stored, and nothing that served the client from a deployment. */
export const nothingAdopted: ClientDeployment = { outcome: 'resolved', adopted: null, clearTextPermitted: null };

// The application is mounted the way `main.tsx` mounts it: the same nesting in the same order, `StrictMode` and all
// five providers included, and the application's own last-resort boundary standing inside everything that outlives a
// screen. Nothing below the frame may decide the language, the theme, what the person is carrying, or how a followed
// link leaves the application, so a test that supplied fewer would be proving a second arrangement — and the mode is
// half of that arrangement rather than a detail of it: it invokes every effect twice on mount, which is the difference
// between a screen that behaves and one that behaves the first time. The boundary is part of it for the same reason
// and one more: a render failure the application would contain and report is one this family would otherwise meet
// uncaught, so the wiring at the application root would be the one thing ninety-odd whole-application mounts never
// touch. What differs from `main.tsx` is what it resolves at the edge — a deployment, a credential store, and the
// shell operations — each of which arrives here as a double rather than as a question about the machine.
// The portrait is the one read this frame makes that does not go through a transport, octets not being text. Nothing
// here is about a picture, so the exchange answers that there is none and refuses both writes as unreachable.
const drawsNobody: PortraitExchange = {
    read: () => Promise.resolve({ outcome: 'none' }),
    replace: () => Promise.resolve({ outcome: 'refused', reason: 'unavailable' }),
    remove: () => Promise.resolve({ outcome: 'refused', reason: 'unavailable' }),
};

// A deployment serving no signal channel, which is what every test here is against: the stream mints a ticket, fails
// to open anything, and waits on a schedule that never fires — so every screen this family reaches behaves exactly as
// it did before there was a channel at all, which is the thing these tests are asserting.
const noSignalChannel: MailFathomSignalChannel = () => Promise.reject(new Error('no channel here'));

const neverReopens: SignalStreamSchedule = {
    wait: () => new Promise<void>(() => undefined),
    draw: () => 0,
};

export function renderApp(
    deployment: ClientDeployment = servedFrom,
    signedInWith: KeptSession | null = heldSession,
    send: DeploymentTransport = deploymentAnswering(),
    credentials: CredentialStore = storeKeeping(),
    telemetry: ClientTelemetry = noTelemetry,
): void {
    render(
        <StrictMode>
            <LocalizationProvider>
                <ToastsProvider>
                    <ThemeProvider>
                        <WorkspaceProvider>
                            <LinkOpenerContext value={() => Promise.resolve()}>
                                <SystemNotifierContext value={raisesNothing}>
                                    <AttachmentExchangeContext value={deliversNothing}>
                                        <AttachmentUploadContext value={uploadsNothing}>
                                            <TelemetryContext value={telemetry}>
                                                <Containment region="application">
                                                    <App
                                                        credentials={credentials}
                                                        deployment={deployment}
                                                        openSignals={noSignalChannel}
                                                        portraits={drawsNobody}
                                                        send={send}
                                                        signalSchedule={neverReopens}
                                                        signedInWith={signedInWith}
                                                    />
                                                </Containment>
                                            </TelemetryContext>
                                        </AttachmentUploadContext>
                                    </AttachmentExchangeContext>
                                </SystemNotifierContext>
                            </LinkOpenerContext>
                        </WorkspaceProvider>
                    </ThemeProvider>
                </ToastsProvider>
            </LocalizationProvider>
        </StrictMode>,
    );
}

/** A telemetry double holding what the frame asked it to record, which is what these tests read back. */
export function telemetryRecording(): {
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
            renderFailed: () => undefined,
        },
        exportedFor,
        permitted,
        stopped,
        events,
    };
}

// The frame is on the screen once the summary above the space has an answer to state, which is what every test that
// acts on the frame waits for rather than for a timer.
export async function framed(): Promise<void> {
    await screen.findByText('Every account is up to date.');
}

export function typeAddress(entry: string): void {
    fireEvent.change(screen.getByRole('textbox', { name: 'Server' }), { target: { value: entry } });
}

export function signIn(userName = 'user', password = 'open sesame'): void {
    fireEvent.change(screen.getByRole('textbox', { name: 'Login' }), { target: { value: userName } });
    fireEvent.change(screen.getByLabelText('Password'), { target: { value: password } });
    fireEvent.click(screen.getByRole('button', { name: 'Connect' }));
}

export function openingAt(address: string): void {
    window.history.replaceState(null, '', address);
}

export async function goTo(space: string): Promise<void> {
    // Matched on the start of the name rather than the whole of it: a space with nothing behind it yet says so in its
    // own accessible name, and this helper is used to reach both kinds.
    fireEvent.click(screen.getByRole('link', { name: new RegExp(`^${space}`, 'u') }));

    await screen.findByRole('main', { name: space });
}

/**
 * The arrangement every file in this family opens each of its tests with, and puts back afterwards.
 *
 * Called from the file rather than installed by importing this module, so a reader of one of those files can see that
 * its tests are given a width, an address, and a cleared record without having to know what an import did to them.
 */
export function resetsBetweenTests(): void {
    // The frame is read at the width the workspace opens out at, which is what every composition in this family was
    // written against: jsdom lays nothing out, so the width is answered here rather than measured. Anything else —
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
        document.documentElement.removeAttribute('lang');
        document.documentElement.removeAttribute('data-theme');
    });
}

// The way out of the frame, taken the way a person takes it: the row in the account menu. It is what a test reaches
// the sign-in screen through, that screen being where a chosen deployment is pointed somewhere else.
//
// Awaited rather than pressed and left, because signing out asks the credential store to forget before the form comes
// back: everything a test does next is on the sign-in screen, and reaching for a control of it in the commit the press
// produced is what finds nothing on a machine loaded enough to put the two apart.
export async function signOut(): Promise<void> {
    fireEvent.click(screen.getByRole('button', { name: 'Sign out', hidden: true }));

    await screen.findByRole('textbox', { name: 'Login' });
}

// Inside the frame the language and the telemetry decision are made on the settings screen rather than in the menu
// that leads to it, which is where the design project puts them — so a test about either opens that screen the way a
// person does, on the tab that holds both, which is the second of its two.
export function openSettings(): void {
    fireEvent.click(screen.getByRole('button', { name: 'Settings', hidden: true }));
    fireEvent.click(screen.getByRole('tab', { name: 'Application', hidden: true }));
}
