// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { createContext, useContext } from 'react';
import { longestResponseBody, type ClientRequest, type ClientResponse } from '@mailfathom/client-backend';
import { readBoundedContent } from './boundedBody';

// Putting one file the author is attaching on the wire. It is the third module in this directory that calls `fetch`
// and the second that carries octets: `Client.Backend` declares no DOM, so a `File`, a `Blob`, and an `AbortSignal`
// can only be named on this side of the boundary.
//
// What that package still owns is everything that is the contract — the route, the credential, what the file declares
// itself to be, and what an answer means — so this hands back the status and the body and decides nothing about
// either. It is the mirror of `attachmentExchange.ts`: that one reads octets off the wire, this one writes them to it.

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

        return await answerOf(response, request.longestAnswer ?? longestResponseBody);
    } catch {
        // A connection refused, a name that does not resolve, and the author taking the file back off all arrive here
        // as one rejected promise. Which of them it was is the composer's to know — it is what holds the signal — and
        // nothing here has to tell them apart to report that nothing came back.
        return null;
    }
};

/**
 * What one answer to an upload amounts to, read under the size it is allowed to hold.
 *
 * Separate from the request above because that one calls `fetch` and this one is everything the client decides about
 * what came back — which is what a test reaches, on a `Response` it constructed rather than on a patched global.
 *
 * The answer is one staged-file record, so an answer past the bound comes back with nothing in it, which the parser
 * already refuses as unreadable. It is walked rather than buffered, for the reason `boundedBody.ts` states: a ceiling
 * an answer is measured against after it is all in memory is a ceiling that was never applied.
 */
export async function answerOf(response: Response, longest: number): Promise<ClientResponse> {
    const octets = await readBoundedContent(response, longest);

    return {
        status: response.status,
        body: typeof octets === 'string' ? '' : new TextDecoder().decode(joined(octets)),
        headers: Object.fromEntries(response.headers),
    };
}

function joined(chunks: readonly Uint8Array<ArrayBuffer>[]): Uint8Array<ArrayBuffer> {
    const whole = new Uint8Array(chunks.reduce((octets, chunk) => octets + chunk.byteLength, 0));
    let written = 0;

    for (const chunk of chunks) {
        whole.set(chunk, written);
        written += chunk.byteLength;
    }

    return whole;
}
