// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { attachmentRefusalForStatus, longestResponseBody, type ClientRequest } from '@mailfathom/client-backend';

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
 * Reads the octets up to the size the message said the file holds, and stops there.
 *
 * `response.blob()` would buffer whatever arrives before anything got to look at it, which is the wrong order for a
 * download a person is waiting on: they are told a size before they ask, so an answer larger than that is refused
 * rather than written to their machine, and reading in chunks is also what makes the progress they are shown real
 * rather than a guess.
 *
 * The two ways it can end without a file are kept apart rather than collapsed into an absence, because they are two
 * different sentences to a reader: an answer larger than the message described is a defect worth reporting, and a
 * connection that stopped partway through is the ordinary one to try again.
 */
async function readBoundedContent(
    response: Response,
    longest: number,
    arrived: (octets: number) => void,
): Promise<readonly Uint8Array<ArrayBuffer>[] | 'largerThanDescribed' | 'unavailable'> {
    const reading = response.body?.getReader();

    if (reading === undefined) {
        return 'unavailable';
    }

    const chunks: Uint8Array<ArrayBuffer>[] = [];
    let octets = 0;

    for (;;) {
        let chunk: ReadableStreamReadResult<Uint8Array>;

        try {
            chunk = await reading.read();
        } catch {
            return 'unavailable';
        }

        if (chunk.done) {
            return chunks;
        }

        octets += chunk.value.byteLength;

        if (octets > longest) {
            await reading.cancel();

            return 'largerThanDescribed';
        }

        // Copied out of the chunk the stream handed over rather than kept as it stands, because a stream's own buffer
        // may be a shared one and a `Blob` is composed from unshared memory. The copy replaces the chunk rather than
        // standing beside it, so what is held at once is the file and one chunk rather than the file twice.
        chunks.push(chunk.value.slice());
        arrived(octets);
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
