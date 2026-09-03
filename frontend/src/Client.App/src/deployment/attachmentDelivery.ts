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

// Fetching one file a message carries and handing it to the person, which is one operation rather than two: nothing
// above this module ever holds the octets, so no screen and no component in the client has a reason to.
//
// It sits beside `sendToDeployment.ts` because the two are the whole of what calls `fetch` here — `Client.Backend`
// declares no DOM, so a `ReadableStream`, an `AbortSignal`, and a `Blob` can only be named on this side of the boundary.
// What that package still owns is the part that is the contract: the route, the credential, the header the answer is
// asked in, and the bound it is read under all arrive as the composed `ClientRequest`.

/** What happened to a download, where an expected failure is a value rather than an exception. */
export type AttachmentDeliveryOutcome =
    'delivered' | 'abandoned' | 'unauthenticated' | 'unauthorized' | 'unavailable' | 'largerThanDescribed';

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

/**
 * Downloads one file and hands it to the person as a file to keep.
 *
 * @param request What `mailAttachmentRequest` composed, which carries the route, the credential, and the bound.
 * @param fileName What to offer the file under, already reduced to a name this client is willing to write.
 * @param arrived How many octets have been read so far, reported as they arrive so a screen can say so.
 * @param abandoned Abandons the download when the person waiting on it gives up.
 * @returns What happened, which is `delivered` only where the whole file arrived within its stated size.
 */
export type AttachmentDelivery = (
    request: ClientRequest,
    fileName: string,
    arrived: (octets: number) => void,
    abandoned: AbortSignal,
) => Promise<AttachmentDeliveryOutcome>;

// Reached through a context rather than handed down, for the reason `shellOperations/linkOpener.ts` gives about
// the operation it carries: which implementation satisfies it is the composition root's decision, and the row
// that calls it is several components below the screen that owns the message — none of which has a reason to
// name a download it never makes.
export const AttachmentDeliveryContext = createContext<AttachmentDelivery | null>(null);

export function useAttachmentDelivery(): AttachmentDelivery {
    const delivery = useContext(AttachmentDeliveryContext);

    if (delivery === null) {
        throw new Error(
            'A component asked for an attachment outside the AttachmentDeliveryContext that main.tsx supplies.',
        );
    }

    return delivery;
}

export const deliverAttachment: AttachmentDelivery = async (request, fileName, arrived, abandoned) => {
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

    if (response.status !== 200) {
        return attachmentRefusalForStatus(response.status);
    }

    const octets = await readBoundedContent(response, request.longestAnswer ?? longestResponseBody, arrived);

    if (typeof octets === 'string') {
        return abandoned.aborted ? 'abandoned' : octets;
    }

    keepAsFile(octets, fileName);

    return 'delivered';
};

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
