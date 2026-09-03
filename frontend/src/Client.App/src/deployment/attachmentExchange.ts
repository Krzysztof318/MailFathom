// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { createContext, useContext } from 'react';
import {
    attachmentRefusalForStatus,
    longestResponseBody,
    type ClientFailureReason,
    type ClientRequest,
} from '@mailfathom/client-backend';
import { readBoundedContent } from './boundedBody';
import { asDataUrl } from './dataUrl';

// The two things the client does with one file a message carries: hand it to the person to keep, and show it inside the
// client. Both are one boundary rather than two — one route, one credential, one bound, one way out — which is why they
// are one interface the application supplies rather than two operations a screen reaches for separately.
//
// It sits beside `sendToDeployment.ts` because these are the whole of what calls `fetch` here — `Client.Backend`
// declares no DOM, so a `ReadableStream`, an `AbortSignal`, and a `Blob` can only be named on this side of the
// boundary. What that package still owns is the part that is the contract: the route, the credential, the header the
// answer is asked in, and the bound it is read under all arrive as the composed `ClientRequest`.
//
// A download hands the octets over and lets go of them, so nothing above this module holds a file it saved. A read
// answers what a screen may draw — an address for a picture, the words for text — which is the one place octets a
// message carries become a value the application holds, and it is bounded above for exactly that reason.

/** What happened to a download, where an expected failure is a value rather than an exception. */
export type AttachmentDeliveryOutcome =
    'delivered' | 'abandoned' | 'unauthenticated' | 'unauthorized' | 'unavailable' | 'largerThanDescribed';

/**
 * Why a file could not be shown.
 *
 * The five a download can end on, and one that only showing has: octets that arrived whole and that nothing here could
 * turn into something to draw. A download cannot reach it — what it does with the octets is hand them to the platform
 * to save, which does not read them.
 */
export type ShowingRefusal = Exclude<AttachmentDeliveryOutcome, 'delivered'> | 'unreadable';

/**
 * How a file is being asked for: as an address a picture is drawn at, or as the words it holds.
 *
 * Text carries the character set the message declared for it, because that is a fact about the octets rather than about
 * the screen: mail carries plenty of encodings that are not UTF-8, and a file decoded as one it is not arrives as a
 * screenful of replacement characters rather than as anything a reader could act on.
 */
export type ShownAs = { readonly as: 'picture' } | { readonly as: 'text'; readonly charset: string };

/** What a read of one file answered: something the screen may draw, or why there is nothing to draw. */
export type AttachmentRead =
    | { readonly outcome: 'shown'; readonly content: string }
    | { readonly outcome: 'refused'; readonly refusal: ShowingRefusal };

/**
 * Fetching one file a message carries, for the two things the client does with one.
 *
 * It is an interface rather than two loose functions for the reason `portraitExchange.ts` gives about its three: the
 * two are one boundary, so a screen proving what it does about a refusal receives one object and the application
 * supplies one at its edge.
 */
export interface AttachmentExchange {
    /**
     * Downloads one file and hands it to the person as a file to keep.
     *
     * @param request What `mailAttachmentRequest` composed, which carries the route, the credential, and the bound.
     * @param fileName What to offer the file under, already reduced to a name this client is willing to write.
     * @param arrived How many octets have been read so far, reported as they arrive so a screen can say so.
     * @param abandoned Abandons the download when the person waiting on it gives up.
     * @returns What happened, which is `delivered` only where the whole file arrived within its stated size.
     */
    deliver(
        request: ClientRequest,
        fileName: string,
        arrived: (octets: number) => void,
        abandoned: AbortSignal,
    ): Promise<AttachmentDeliveryOutcome>;

    /**
     * Reads one file and answers what a screen may draw it as.
     *
     * @param request What `mailAttachmentRequest` composed, whose bound is the size the message described.
     * @param shown Which of the two forms the screen asked for, decided from what the file declares itself to be.
     * @param abandoned Discards the read when the screen that started it stops listening.
     * @returns The address a picture is drawn at or the words text holds, or why neither could be produced.
     */
    read(request: ClientRequest, shown: ShownAs, abandoned: AbortSignal): Promise<AttachmentRead>;
}

/**
 * Which failure a download amounts to, for the record `Client.Backend` keeps of the request it composed.
 *
 * Two of the six are not failures of the request. A delivered file plainly is not, and neither is a download somebody
 * stopped: it ended because the person asked it to, and recording that as `unavailable` would put their change of mind
 * in the dimension an operator reads for a deployment that is not answering. An answer larger than the message
 * described is `unreadable` — the body was refused rather than absent, which is the reason that word already carries.
 */
export function deliveryFailureOf(outcome: AttachmentDeliveryOutcome): ClientFailureReason | null {
    switch (outcome) {
        case 'delivered':
        case 'abandoned':
            return null;
        case 'largerThanDescribed':
            return 'unreadable';
        case 'unauthenticated':
        case 'unauthorized':
        case 'unavailable':
            return outcome;
    }
}

/** Which failure a read amounts to, by the same reading, a file that was shown being no failure at all. */
export function showingFailureOf(read: AttachmentRead): ClientFailureReason | null {
    if (read.outcome === 'shown') {
        return null;
    }

    return read.refusal === 'unreadable' ? 'unreadable' : deliveryFailureOf(read.refusal);
}

// Reached through a context rather than handed down, for the reason `shellOperations/linkOpener.ts` gives about the
// operation it carries: which implementation satisfies it is the composition root's decision, and the row that calls it
// is several components below the screen that owns the message — none of which has a reason to name a download it never
// makes.
export const AttachmentExchangeContext = createContext<AttachmentExchange | null>(null);

export function useAttachmentExchange(): AttachmentExchange {
    const exchange = useContext(AttachmentExchangeContext);

    if (exchange === null) {
        throw new Error(
            'A component asked for an attachment outside the AttachmentExchangeContext that main.tsx supplies.',
        );
    }

    return exchange;
}

export const attachmentExchange: AttachmentExchange = {
    deliver: async (request, fileName, arrived, abandoned) => {
        const answer = await fetchedOctets(request, arrived, abandoned);

        if (typeof answer === 'string') {
            return answer;
        }

        keepAsFile(answer, fileName);

        return 'delivered';
    },

    read: async (request, shown, abandoned) => {
        const answer = await fetchedOctets(request, () => undefined, abandoned);

        if (typeof answer === 'string') {
            return { outcome: 'refused', refusal: answer };
        }

        try {
            return { outcome: 'shown', content: await drawnFrom(answer, shown) };
        } catch {
            return { outcome: 'refused', refusal: 'unreadable' };
        }
    },
};

/**
 * What one answer to that route amounts to: the octets the file holds, or the refusal that stands in their place.
 *
 * It is a function of the answer rather than of the request, which is the split `attachmentUpload.ts` draws for the
 * same reason — the deciding half is what a suite can put a constructed `Response` to, and the `fetch` around it is
 * the browser's. Both operations above ask it, because they differ in what they do with the octets rather than in what
 * an answer means: a second copy of the status mapping and the bounded walk is the copy that would come to disagree
 * about what a `403` means.
 *
 * @param response What came back, whose body is walked rather than buffered.
 * @param longest The most octets this answer may hold, which is the size the message described the part at.
 * @param arrived How many octets have been read so far, reported as they arrive where a screen says so.
 */
export async function attachmentOctetsOf(
    response: Response,
    longest: number,
    arrived: (octets: number) => void = () => undefined,
): Promise<readonly Uint8Array<ArrayBuffer>[] | Exclude<AttachmentDeliveryOutcome, 'delivered' | 'abandoned'>> {
    if (response.status !== 200) {
        return attachmentRefusalForStatus(response.status);
    }

    return readBoundedContent(response, longest, arrived);
}

/** The octets one file holds, asked for over the wire, with the way out of the wait named as what ended it. */
async function fetchedOctets(
    request: ClientRequest,
    arrived: (octets: number) => void,
    abandoned: AbortSignal,
): Promise<readonly Uint8Array<ArrayBuffer>[] | Exclude<AttachmentDeliveryOutcome, 'delivered'>> {
    let response: Response;

    try {
        response = await fetch(request.path, {
            method: request.method,
            headers: { ...request.headers },
            signal: abandoned,
        });
    } catch {
        // A connection refused, a name that does not resolve, and a person pressing the way out all arrive here as one
        // rejected promise, so which of them it was is asked of the signal rather than of the error.
        return abandoned.aborted ? 'abandoned' : 'unavailable';
    }

    const answer = await attachmentOctetsOf(response, request.longestAnswer ?? longestResponseBody, arrived);

    // A read that stopped partway is reported as the person's act where it was one: the answer had begun, so what
    // ended it is asked of the signal rather than of the refusal. A refusal the deployment itself stated keeps its own
    // name, which is why this reaches only an answer that had already been accepted.
    return response.status === 200 && typeof answer === 'string' && abandoned.aborted ? 'abandoned' : answer;
}

/**
 * What a screen draws the octets as.
 *
 * Exported beside the deciding half above for the same reason: it is the second half of a read that owes a suite an
 * assertion, and it takes octets rather than a request so that nothing has to stand in for the wire to make one.
 *
 * A picture becomes an address under the general binary type rather than under what the sender declared the part to be,
 * for the same reason a download is saved under it: an address carries its own origin, and a message whose picture
 * claims to be markup would otherwise be a document a browser could be talked into rendering. The element that draws it
 * decides what it is — an `img` renders a picture and nothing else, whatever the address says.
 *
 * Text is decoded under the character set the message declared, falling back to UTF-8 where that is a label the
 * platform does not know — which a sender is free to write, this being a header they composed.
 */
export async function drawnFrom(octets: readonly Uint8Array<ArrayBuffer>[], shown: ShownAs): Promise<string> {
    if (shown.as === 'picture') {
        return asDataUrl(new Blob([...octets], { type: 'application/octet-stream' }));
    }

    // One decoder for the whole read, which is what `stream: true` is for: a character split across two chunks is held
    // until the chunk carrying the rest of it arrives, where a decoder built per chunk would emit a replacement
    // character in its place. The final `decode()` with nothing to decode is what flushes a trailing partial sequence.
    const decoding = decoderFor(shown.charset);

    return octets.map((chunk) => decoding.decode(chunk, { stream: true })).join('') + decoding.decode();
}

/** A decoder for the character set the message declared, or the default one where it named something unknown. */
function decoderFor(charset: string): TextDecoder {
    try {
        return new TextDecoder(charset);
    } catch {
        return new TextDecoder();
    }
}

/**
 * Hands the octets to the person as a file to keep, and lets go of them.
 *
 * The type is always the general binary one rather than what the sender declared the part to be. A blob URL carries its
 * own origin, so a message whose attachment claims to be markup would otherwise be a scripted page a browser could be
 * talked into opening rather than saving — which is the same reason the service serves every download as an attachment
 * with sniffing turned off. What names the file is its extension, which the sender's own name already carries.
 *
 * The URL is released in the same turn. A blob URL keeps its octets alive for as long as the document holds it, so a
 * reader who downloads several large files in one sitting would otherwise be holding all of them in memory until they
 * navigated away.
 */
function keepAsFile(octets: readonly Uint8Array<ArrayBuffer>[], fileName: string): void {
    const address = URL.createObjectURL(new Blob([...octets], { type: 'application/octet-stream' }));
    const offered = document.createElement('a');

    offered.href = address;
    offered.download = fileName;
    offered.click();

    URL.revokeObjectURL(address);
}
