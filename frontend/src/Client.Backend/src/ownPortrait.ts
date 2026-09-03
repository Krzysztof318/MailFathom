// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { headersFor, routeFor, type ClientSession } from './session';
import type { ClientRequest } from './transport';

// The picture the signed-in person is drawn by. Like a message's attachment, and for the same reason, this module
// composes the requests and stops there: what a read answers with is octets and what a replacement carries is a file,
// and a `MailFathomTransport` speaks in text — so putting these on the wire is the application's adapter, exactly as
// calling `fetch` at all is.
//
// What stays here is everything the boundary owns: the route, the credential, the kinds this surface accepts, the
// bound an upload is refused past, and the header the answer is asked in. No screen and no adapter above learns a path
// or a header name.

/** The route the acting person's portrait is read, replaced, and removed at, relative to the client prefix. */
export const ownPortraitRoute = '/portrait';

/**
 * The largest portrait this surface accepts, in octets.
 *
 * It is the deployment's own bound restated rather than a second opinion: an upload past it is refused there before a
 * handler is entered, and a client that knew nothing of it would let somebody wait for a megabyte to travel in order
 * to be told no. It is also the bound a read is taken under, because a portrait larger than the one that could have
 * been stored is not one this deployment wrote.
 */
export const largestPortraitOctets = 1_048_576;

/** The kinds of picture this surface stores, which is the whole set rather than a preference among more. */
export const portraitImageTypes = ['image/jpeg', 'image/png'] as const;

/** One kind of picture a portrait may be. */
export type PortraitImageType = (typeof portraitImageTypes)[number];

/** Whether a kind a browser reported for a chosen file is one this surface stores. */
export function isPortraitImageType(value: string): value is PortraitImageType {
    return portraitImageTypes.includes(value as PortraitImageType);
}

/**
 * Composes the request that reads the picture the signed-in person is drawn by.
 *
 * The answer is octets rather than JSON, so the request asks for the two kinds this surface serves; the credential is
 * unchanged, being the access control the route applies.
 */
export function readOwnPortraitRequest(session: ClientSession): ClientRequest {
    return {
        method: 'GET',
        path: routeFor(session, ownPortraitRoute),
        headers: { ...headersFor(session), Accept: portraitImageTypes.join(', ') },
        longestAnswer: largestPortraitOctets,
    };
}

/**
 * Composes the request that replaces the picture, under the kind the chosen file was found to be.
 *
 * The octets are not here: a file is a browser value this package may not name, so the adapter that puts this request
 * on the wire carries them. What this states is the kind they will be sent under, which the deployment reads the
 * signature against rather than trusting.
 */
export function replaceOwnPortraitRequest(session: ClientSession, type: PortraitImageType): ClientRequest {
    return {
        method: 'POST',
        path: routeFor(session, ownPortraitRoute),
        headers: { ...headersFor(session), 'Content-Type': type },
    };
}

/** Composes the request that removes the picture, leaving everything else about the person as it was. */
export function removeOwnPortraitRequest(session: ClientSession): ClientRequest {
    return {
        method: 'DELETE',
        path: routeFor(session, ownPortraitRoute),
        headers: headersFor(session),
    };
}
