// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { headersFor, routeFor, type ClientSession } from './session';
import type { ClientRequest } from './transport';

// The one operation on this surface that is not read through a transport, because what it answers with is octets and a
// `MailFathomTransport` answers with text. So this module composes the request and stops there: putting it on the wire
// with a progress report and a way to abandon it is the application's adapter, exactly as calling `fetch` at all is.
//
// What stays here is everything the boundary owns — the route, the credential, the header the answer is asked in, and
// the bound the answer is read under — so no screen and no adapter above learns a path or a header name.

/** The route one file a message carries is served at, relative to the client prefix. */
export function mailAttachmentRoute(storedEmailId: string, position: number): string {
    return `/messages/${encodeURIComponent(storedEmailId)}/attachments/${position.toFixed(0)}`;
}

/**
 * Composes the request that downloads one file a message carries.
 *
 * @param session Who is asking, and where.
 * @param storedEmailId The message the file belongs to, as a read of that message published it.
 * @param position The file's place in the order that read listed the message's attachments in.
 * @param describedSizeOctets How many octets that read said the file holds, which becomes the bound the answer is read
 * under: a response larger than the description is refused rather than saved, so a deployment cannot hand a reader more
 * than the screen told them they were asking for.
 * @returns The request an adapter puts on the wire.
 */
export function mailAttachmentRequest(
    session: ClientSession,
    storedEmailId: string,
    position: number,
    describedSizeOctets: number,
): ClientRequest {
    return {
        method: 'GET',
        path: routeFor(session, mailAttachmentRoute(storedEmailId, position)),

        // Octets rather than JSON, which is the one place a request on this surface asks for something else. The
        // credential is unchanged: it is the access control this route applies, and no second capability is minted.
        headers: { ...headersFor(session), Accept: 'application/octet-stream' },
        longestAnswer: describedSizeOctets,
    };
}

/**
 * Why a download did not answer with a file.
 *
 * It reuses nothing from `ClientFailureReason` deliberately: three of those four are about a read of a description and
 * the fourth says a body could not be parsed, while what can go wrong here is a refusal, an unreachable deployment, or
 * an answer that did not hold what the message said it would.
 */
export type MailAttachmentRefusal = 'unauthenticated' | 'unauthorized' | 'unavailable' | 'largerThanDescribed';

/** The refusal an HTTP status stands for, for a status this package did not expect to succeed. */
export function attachmentRefusalForStatus(status: number): MailAttachmentRefusal {
    switch (status) {
        case 401:
            return 'unauthenticated';
        case 403:
            return 'unauthorized';
        default:
            return 'unavailable';
    }
}
