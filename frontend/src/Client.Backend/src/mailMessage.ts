// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { failed, failureReasonForStatus, read, type ClientResult } from './failure';
import { headersFor, routeFor, type ClientSession } from './session';
import { send, type MailFathomTransport } from './transport';

// Everything a reading pane draws around a message, which is the other half of the body route. What the service serves
// here is a description rather than any of the message's content beyond its headers: no octet of a file, and no picture
// the body embeds. This is the client's trust boundary for that description, so every field is checked before it becomes
// a value a screen may draw, and every collection carries a bound checked during the walk rather than after it.

/** The route one message is served at, relative to the client prefix. */
export function mailMessageRoute(storedEmailId: string): string {
    return `/messages/${encodeURIComponent(storedEmailId)}`;
}

/** The header one address appeared in, which is what decides where a screen draws it. */
export type MailParticipantRole = 'Sender' | 'From' | 'ReplyTo' | 'To' | 'Cc' | 'Bcc';

/** What the receiving mail server established about the author the message displays. */
export type MailAuthorAuthentication = 'NotEstablished' | 'Failed' | 'Authenticated';

/** Whether this deployment recognizes that author. */
export type MailDeploymentTrust = 'Unknown' | 'Trusted';

/** One address a message wrote, and the header it wrote it in. */
export interface MailParticipant {
    readonly role: MailParticipantRole;
    readonly address: string;
    readonly displayName: string | null;
}

/** What a message displays above its body. */
export interface MailMessageHeaders {
    readonly subject: string | null;
    readonly sentAt: string | null;
    readonly receivedAt: string | null;
    readonly participants: readonly MailParticipant[];
    readonly messageId: string | null;
    readonly inReplyTo: string | null;
    readonly references: readonly string[];
}

/** Whether a message has a body to draw, and which forms of it the sender wrote. */
export interface MailMessageBodyForms {
    readonly availability: string;
    readonly plainText: boolean;
    readonly html: boolean;
}

/**
 * What this deployment established about the author a message displays.
 *
 * The two outcomes are held side by side and never collapsed into one value, for the reason the service publishes them
 * that way: one is a fact a receiving server established about the message, the other is this deployment's own
 * classification of the author it established, and a client that combined them would be inventing the rule that does so.
 *
 * `authenticatedDomain` is who actually sent the message, which is what a screen names instead of the `From` value the
 * message displays. It is stated and never judged: comparing it against the displayed domain would be evaluating a
 * policy the service deliberately does not, so what a reader acts on stays `authorAuthentication`.
 */
export interface MailSenderVerdict {
    readonly authorAuthentication: MailAuthorAuthentication;
    readonly deploymentTrust: MailDeploymentTrust;
    readonly authenticatedDomain: string | null;
}

/**
 * One file a message carries, described and carrying none of what it holds.
 *
 * The position is the identity because it is the only stable one a message's parts have, and it is what the download
 * route is asked with. The file name is text a sender chose: it arrives normalized to a bare name, and
 * `wasFileNameNormalized` says whether that rewrote anything — which is the case worth drawing carefully rather than a
 * detail to hide.
 */
export interface MailAttachment {
    readonly position: number;
    readonly fileName: string | null;
    readonly wasFileNameNormalized: boolean;
    readonly mediaType: string;
    readonly sizeOctets: number;
}

/** The counts for everything a message carries besides its body, or `null` where nothing has read its parts. */
export interface MailCarried {
    readonly attachmentCount: number;
    readonly totalSizeOctets: number;
    readonly inlineResourceCount: number;
    readonly encrypted: boolean;
    readonly unverifiedSignature: boolean;
    readonly unexpandedTnefPart: boolean;
}

/** One message as the reading pane draws it, without its body and without any file it carries. */
export interface MailMessage {
    readonly storedEmailId: string;
    readonly account: string;
    readonly folder: string;
    readonly threadId: string | null;
    readonly sizeOctets: number;
    readonly headers: MailMessageHeaders;
    readonly body: MailMessageBodyForms;
    readonly sender: MailSenderVerdict;
    readonly attachments: readonly MailAttachment[];
    readonly carried: MailCarried | null;
    readonly unread: boolean;
    readonly flagged: boolean;
    readonly answered: boolean;
}

// A description composes to a stated size, so the backstop written for a stranger is far looser than this answer ever
// needs. It is generous against the bounds below rather than tight against them: the point of failing here is to stop a
// body being buffered whole, and the point of failing there is to refuse a description this client would not draw.
const longestMessageAnswer = 4 * 1024 * 1024;

// What the service will compose at most, mirrored so a description larger than that is refused rather than drawn. The
// part count is the ceiling `MaxMimePartCount` defaults to, rounded up, because every part of a message can be an
// attachment; the address count is far above any real header set and exists for the case the answer is not one. Each is
// checked during the walk, because a bound applied after a walk that completed has already paid for what it read.
const bounds = {
    maximumAttachments: 1024,
    maximumParticipants: 2048,
    maximumReferences: 512,
    maximumTextLength: 4096,
    maximumFileNameLength: 1024,
    maximumMediaTypeLength: 256,
} as const;

const participantRoles: readonly MailParticipantRole[] = ['Sender', 'From', 'ReplyTo', 'To', 'Cc', 'Bcc'];

const authorAuthentications: readonly MailAuthorAuthentication[] = ['NotEstablished', 'Failed', 'Authenticated'];

const deploymentTrusts: readonly MailDeploymentTrust[] = ['Unknown', 'Trusted'];

/** Reads one message's description, answering an expected failure as a value rather than by throwing. */
export async function readMailMessage(
    session: ClientSession,
    transport: MailFathomTransport,
    storedEmailId: string,
): Promise<ClientResult<MailMessage>> {
    const response = await send(transport, {
        method: 'GET',
        path: routeFor(session, mailMessageRoute(storedEmailId)),
        headers: headersFor(session),
        longestAnswer: longestMessageAnswer,
    });

    if (response === null) {
        return failed('unavailable', null);
    }

    if (response.status !== 200) {
        return failed(failureReasonForStatus(response.status), response.status);
    }

    const message = parseMessage(response.body);

    return message === null ? failed('unreadable', response.status) : read(message);
}

function parseMessage(body: string): MailMessage | null {
    let parsed: unknown;

    try {
        parsed = JSON.parse(body);
    } catch {
        return null;
    }

    const record = asRecord(parsed);
    if (record === null) {
        return null;
    }

    const storedEmailId = record['storedEmailId'];
    const account = record['account'];
    const folder = record['folder'];
    const threadId = record['threadId'] ?? null;
    const sizeOctets = record['sizeOctets'];

    if (!isText(storedEmailId) || !isText(account) || !isText(folder)) {
        return null;
    }

    if (threadId !== null && !isText(threadId)) {
        return null;
    }

    if (!isCount(sizeOctets)) {
        return null;
    }

    const headers = parseHeaders(record['headers']);
    const forms = parseBodyForms(record['body']);
    const sender = parseSenderVerdict(record['sender']);
    const attachments = parseAttachments(record['attachments']);
    const carried = parseCarried(record['carried'] ?? null);
    const unread = record['unread'];
    const flagged = record['flagged'];
    const answered = record['answered'];

    if (headers === null || forms === null || sender === null || attachments === null || carried === undefined) {
        return null;
    }

    if (typeof unread !== 'boolean' || typeof flagged !== 'boolean' || typeof answered !== 'boolean') {
        return null;
    }

    return {
        storedEmailId,
        account,
        folder,
        threadId,
        sizeOctets,
        headers,
        body: forms,
        sender,
        attachments,
        carried,
        unread,
        flagged,
        answered,
    };
}

function parseHeaders(value: unknown): MailMessageHeaders | null {
    const record = asRecord(value);
    if (record === null) {
        return null;
    }

    const subject = record['subject'] ?? null;
    const sentAt = record['sentAt'] ?? null;
    const receivedAt = record['receivedAt'] ?? null;
    const messageId = record['messageId'] ?? null;
    const inReplyTo = record['inReplyTo'] ?? null;

    if (!isOptionalText(subject) || !isOptionalText(sentAt) || !isOptionalText(receivedAt)) {
        return null;
    }

    if (!isOptionalText(messageId) || !isOptionalText(inReplyTo)) {
        return null;
    }

    const participants = parseParticipants(record['participants']);
    const references = parseReferences(record['references']);

    if (participants === null || references === null) {
        return null;
    }

    return { subject, sentAt, receivedAt, participants, messageId, inReplyTo, references };
}

function parseParticipants(value: unknown): readonly MailParticipant[] | null {
    if (!Array.isArray(value) || value.length > bounds.maximumParticipants) {
        return null;
    }

    const participants: MailParticipant[] = [];
    for (const entry of value) {
        const participant = parseParticipant(entry);
        if (participant === null) {
            return null;
        }

        participants.push(participant);
    }

    return participants;
}

function parseParticipant(value: unknown): MailParticipant | null {
    const record = asRecord(value);
    if (record === null) {
        return null;
    }

    const role = record['role'];
    const address = record['address'];
    const displayName = record['displayName'] ?? null;

    if (!isOneOf(role, participantRoles) || !isText(address) || !isOptionalText(displayName)) {
        return null;
    }

    return { role, address, displayName };
}

function parseReferences(value: unknown): readonly string[] | null {
    if (!Array.isArray(value) || value.length > bounds.maximumReferences) {
        return null;
    }

    const references: string[] = [];
    for (const entry of value) {
        if (!isText(entry)) {
            return null;
        }

        references.push(entry);
    }

    return references;
}

function parseBodyForms(value: unknown): MailMessageBodyForms | null {
    const record = asRecord(value);
    if (record === null) {
        return null;
    }

    const availability = record['availability'];
    const plainText = record['plainText'];
    const html = record['html'];

    if (!isText(availability) || typeof plainText !== 'boolean' || typeof html !== 'boolean') {
        return null;
    }

    return { availability, plainText, html };
}

function parseSenderVerdict(value: unknown): MailSenderVerdict | null {
    const record = asRecord(value);
    if (record === null) {
        return null;
    }

    const authorAuthentication = record['authorAuthentication'];
    const deploymentTrust = record['deploymentTrust'];
    const authenticatedDomain = record['authenticatedDomain'] ?? null;

    if (!isOneOf(authorAuthentication, authorAuthentications) || !isOneOf(deploymentTrust, deploymentTrusts)) {
        return null;
    }

    if (!isOptionalText(authenticatedDomain)) {
        return null;
    }

    return { authorAuthentication, deploymentTrust, authenticatedDomain };
}

function parseAttachments(value: unknown): readonly MailAttachment[] | null {
    if (!Array.isArray(value) || value.length > bounds.maximumAttachments) {
        return null;
    }

    const attachments: MailAttachment[] = [];
    for (const entry of value) {
        const attachment = parseAttachment(entry);
        if (attachment === null) {
            return null;
        }

        attachments.push(attachment);
    }

    return attachments;
}

function parseAttachment(value: unknown): MailAttachment | null {
    const record = asRecord(value);
    if (record === null) {
        return null;
    }

    const position = record['position'];
    const fileName = record['fileName'] ?? null;
    const wasFileNameNormalized = record['wasFileNameNormalized'];
    const mediaType = record['mediaType'];
    const sizeOctets = record['sizeOctets'];

    if (!isCount(position) || typeof wasFileNameNormalized !== 'boolean' || !isCount(sizeOctets)) {
        return null;
    }

    if (fileName !== null && (!isText(fileName) || fileName.length > bounds.maximumFileNameLength)) {
        return null;
    }

    if (!isText(mediaType) || mediaType.length > bounds.maximumMediaTypeLength) {
        return null;
    }

    return { position, fileName, wasFileNameNormalized, mediaType, sizeOctets };
}

// Answers `undefined` for a shape it refuses, because `null` is one of the two answers the service legitimately gives:
// a message whose parts nothing has ever read carries no counts at all.
function parseCarried(value: unknown): MailCarried | null | undefined {
    if (value === null) {
        return null;
    }

    const record = asRecord(value);
    if (record === null) {
        return undefined;
    }

    const attachmentCount = record['attachmentCount'];
    const totalSizeOctets = record['totalSizeOctets'];
    const inlineResourceCount = record['inlineResourceCount'];
    const encrypted = record['encrypted'];
    const unverifiedSignature = record['unverifiedSignature'];
    const unexpandedTnefPart = record['unexpandedTnefPart'];

    if (!isCount(attachmentCount) || !isCount(totalSizeOctets) || !isCount(inlineResourceCount)) {
        return undefined;
    }

    if (
        typeof encrypted !== 'boolean' ||
        typeof unverifiedSignature !== 'boolean' ||
        typeof unexpandedTnefPart !== 'boolean'
    ) {
        return undefined;
    }

    return {
        attachmentCount,
        totalSizeOctets,
        inlineResourceCount,
        encrypted,
        unverifiedSignature,
        unexpandedTnefPart,
    };
}

function asRecord(value: unknown): Readonly<Record<string, unknown>> | null {
    return typeof value === 'object' && value !== null && !Array.isArray(value)
        ? (value as Record<string, unknown>)
        : null;
}

function isText(value: unknown): value is string {
    return typeof value === 'string' && value.length <= bounds.maximumTextLength;
}

function isOptionalText(value: unknown): value is string | null {
    return value === null || isText(value);
}

function isCount(value: unknown): value is number {
    return typeof value === 'number' && Number.isSafeInteger(value) && value >= 0;
}

function isOneOf<TValue extends string>(value: unknown, members: readonly TValue[]): value is TValue {
    return typeof value === 'string' && members.includes(value as TValue);
}
