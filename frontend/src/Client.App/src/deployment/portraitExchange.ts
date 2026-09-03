// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import {
    failureReasonForStatus,
    largestPortraitOctets,
    readOwnPortraitRequest,
    removeOwnPortraitRequest,
    replaceOwnPortraitRequest,
    type ClientFailureReason,
    type ClientRequest,
    type ClientSession,
    type PortraitImageType,
} from '@mailfathom/client-backend';
import { readBoundedContent } from './boundedBody';

// The third module in this directory that calls `fetch`, and the third for the same reason: `Client.Backend` declares
// no DOM, so a `Blob`, a `File`, and a `FileReader` can only be named on this side of the boundary. What that package
// still owns is the part that is the contract — the routes, the credential, the kinds, and the bound — all of which
// arrive here as composed requests.
//
// A read answers an address the client may draw rather than the octets themselves, so nothing above holds a picture.
// That address is a data URL rather than a blob URL deliberately: a blob URL keeps its octets alive until somebody
// releases it, which would put a lifetime on a value passed through three components, and one portrait bounded at a
// megabyte costs less held as text than that bookkeeping costs in defects.

/** What a read of the portrait answered. */
export type PortraitRead =
    | { readonly outcome: 'drawn'; readonly picture: string }
    | { readonly outcome: 'none' }
    | { readonly outcome: 'refused'; readonly reason: ClientFailureReason };

/** What a replacement or a removal answered. */
export type PortraitWrite =
    { readonly outcome: 'stored' } | { readonly outcome: 'refused'; readonly reason: ClientFailureReason };

/**
 * What the client may do with the picture the signed-in person is drawn by.
 *
 * It is an interface rather than three loose functions because the three are one boundary: a screen proving what it
 * does about a removal that was refused hands over one object, and the application supplies one at its edge.
 */
export interface PortraitExchange {
    /**
     * Fetches the picture, answering an address it may be drawn at, that there is none, or why it could not be read.
     *
     * @param session Who is asking, and where.
     * @param abandoned Discards the read when the screen that started it stops listening.
     */
    read(session: ClientSession, abandoned: AbortSignal): Promise<PortraitRead>;

    /** Replaces the picture with a file somebody chose, under the kind it was found to be. */
    replace(session: ClientSession, picture: Blob, type: PortraitImageType): Promise<PortraitWrite>;

    /** Removes the picture, leaving everything else about the person as it was. */
    remove(session: ClientSession): Promise<PortraitWrite>;
}

export const portraitExchange: PortraitExchange = {
    read: async (session, abandoned) => {
        const request = readOwnPortraitRequest(session);

        let response: Response;

        try {
            response = await fetch(request.path, {
                method: request.method,
                headers: { ...request.headers },
                signal: abandoned,
            });
        } catch {
            return { outcome: 'refused', reason: 'unavailable' };
        }

        // Having no picture is an ordinary state of the screen rather than a failure on it: the initials are what the
        // client draws instead, and it already has the name they come from.
        if (response.status === 204) {
            return { outcome: 'none' };
        }

        if (response.status !== 200) {
            return { outcome: 'refused', reason: failureReasonForStatus(response.status) };
        }

        // An answer larger than a stored portrait could have been is not one this deployment wrote, and the bound is
        // applied while the octets are read rather than once they are all in memory: what a ceiling at this boundary
        // is for is that an oversized or endless answer never occupies memory at all, which matters most for an
        // address a person named and this client has no reason yet to trust.
        const octets = await readBoundedContent(response, largestPortraitOctets);

        if (typeof octets === 'string') {
            // An answer over the bound and a connection that stopped partway through both leave the screen with no
            // picture to draw, which is the one thing it does about either.
            return { outcome: 'refused', reason: 'unreadable' };
        }

        try {
            const kind = response.headers.get('Content-Type') ?? '';

            return { outcome: 'drawn', picture: await asDataUrl(new Blob([...octets], { type: kind })) };
        } catch {
            return { outcome: 'refused', reason: 'unreadable' };
        }
    },

    replace: async (session, picture, type) => {
        const request = replaceOwnPortraitRequest(session, type);

        return await stated(request.method, request.path, request.headers, picture);
    },

    remove: async (session) => {
        const request = removeOwnPortraitRequest(session);

        return await stated(request.method, request.path, request.headers, null);
    },
};

/** Puts one write on the wire and reports whether it landed, an expected failure being a value here as everywhere. */
async function stated(
    method: ClientRequest['method'],
    path: string,
    headers: Readonly<Record<string, string>>,
    body: Blob | null,
): Promise<PortraitWrite> {
    let response: Response;

    try {
        response = await fetch(path, { method, headers: { ...headers }, body });
    } catch {
        return { outcome: 'refused', reason: 'unavailable' };
    }

    // Both writes are answered `204`, which is the whole of what says the deployment took them.
    return response.status === 204
        ? { outcome: 'stored' }
        : { outcome: 'refused', reason: failureReasonForStatus(response.status) };
}

/**
 * Turns the octets into an address the client may draw the picture at.
 *
 * `FileReader` rather than encoding by hand, because the platform already does exactly this and a megabyte encoded a
 * character at a time is the loop nobody should write twice.
 */
function asDataUrl(picture: Blob): Promise<string> {
    return new Promise((resolve, reject) => {
        const reading = new FileReader();

        reading.onload = () => {
            resolve(typeof reading.result === 'string' ? reading.result : '');
        };
        reading.onerror = () => {
            reject(reading.error ?? new Error('The portrait could not be read.'));
        };
        reading.readAsDataURL(picture);
    });
}
