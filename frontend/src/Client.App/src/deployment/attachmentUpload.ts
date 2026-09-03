// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { createContext, useContext } from 'react';
import { longestResponseBody, type ClientRequest, type ClientResponse } from '@mailfathom/client-backend';

// Putting one file the author is attaching on the wire. It is the third module in this directory that calls `fetch`
// and the second that carries octets: `Client.Backend` declares no DOM, so a `File`, a `Blob`, and an `AbortSignal`
// can only be named on this side of the boundary.
//
// What that package still owns is everything that is the contract — the route, the credential, what the file declares
// itself to be, and what an answer means — so this hands back the status and the body and decides nothing about
// either. It is the mirror of `attachmentDelivery.ts`: that one reads octets off the wire, this one writes them to it.

/**
 * Uploads one file as the body of the composed request.
 *
 * @param request What `stageMailDraftAttachment` composed, which carries the route, the credential, and the type.
 * @param file The octets, which are the whole of the request body: there is no form to compose and no boundary to
 * write, so what the deployment stages is exactly the file the author picked.
 * @param abandoned Abandons the upload when the author takes the file back off before it has finished arriving.
 * @returns What came back, or `null` where nothing did — a connection refused, or an upload that was abandoned.
 */
export type AttachmentUpload = (
    request: ClientRequest,
    file: Blob,
    abandoned: AbortSignal,
) => Promise<ClientResponse | null>;

export const AttachmentUploadContext = createContext<AttachmentUpload | null>(null);

export function useAttachmentUpload(): AttachmentUpload {
    const upload = useContext(AttachmentUploadContext);

    if (upload === null) {
        throw new Error('A component attached a file outside the AttachmentUploadContext that main.tsx supplies.');
    }

    return upload;
}

export const uploadAttachment: AttachmentUpload = async (request, file, abandoned) => {
    try {
        const response = await fetch(request.path, {
            method: request.method,
            headers: { ...request.headers },
            body: file,
            signal: abandoned,
        });

        return {
            status: response.status,
            body: await readBoundedAnswer(response, request.longestAnswer ?? longestResponseBody),
            headers: Object.fromEntries(response.headers),
        };
    } catch {
        // A connection refused, a name that does not resolve, and the author taking the file back off all arrive here
        // as one rejected promise. Which of them it was is the composer's to know — it is what holds the signal — and
        // nothing here has to tell them apart to report that nothing came back.
        return null;
    }
};

// The answer to an upload is one staged-file record, so the bound the request states is what it is read under. An
// answer past it comes back empty, which the parser already refuses as unreadable.
//
// Read whole rather than streamed against the bound, which is the one thing this does differently from
// `sendToDeployment.ts`: that transport also answers the address a client is asking whether MailFathom is even at, so
// it must not buffer a stranger's answer before anything looks at it. This request is only ever made to a deployment
// this client is already signed in to and has already read mail from, so the bound is a ceiling on a known answer
// rather than a defence against an unknown one.
async function readBoundedAnswer(response: Response, longest: number): Promise<string> {
    // Measured in bytes, because that is what the bound counts: a decoded string's length is UTF-16 code units, so
    // reading it that way would let an answer past the ceiling through whenever it carried multi-byte characters.
    const octets = await response.arrayBuffer();

    return octets.byteLength > longest ? '' : new TextDecoder().decode(octets);
}
