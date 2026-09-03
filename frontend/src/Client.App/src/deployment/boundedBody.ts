// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

// Reading an answer's octets under a ceiling, for the two adapters in this directory that fetch them: the attachment a
// reader asked for, and the picture the signed-in person is drawn by. It is one function rather than one per adapter
// because the bound is the same decision each time — refuse during the walk rather than after it — and a second copy
// of it is the one that would come to check the size of something already in memory.
//
// It lives here rather than in `Client.Backend` for the reason every module in this directory does: that package
// declares no DOM, so a `ReadableStream` and a `Response` can only be named on this side of the boundary. What it
// still owns is the number — `longestResponseBody`, and the per-route bounds composed onto a request.

/**
 * Reads the octets of an answer up to the size it is allowed to hold, and stops there.
 *
 * `response.blob()` would buffer whatever arrives before anything got to look at it, which is the wrong order at a
 * boundary a bound exists for: the point of the ceiling is that an answer larger than it never occupies memory, and an
 * address the client has not yet had reason to trust is exactly where that matters. Reading in chunks is also what
 * makes progress a screen shows real rather than a guess.
 *
 * The two ways it can end without octets are kept apart rather than collapsed into an absence, because they are two
 * different sentences to a reader: an answer larger than it was allowed to be is a defect worth reporting, and a
 * connection that stopped partway through is the ordinary one to try again.
 *
 * @param response The answer to read, whose body is walked rather than buffered.
 * @param longest The most octets this answer may hold, applied while reading rather than once it is all in memory.
 * @param arrived How many octets have been read so far, reported as they arrive where a screen says so.
 */
export async function readBoundedContent(
    response: Response,
    longest: number,
    arrived: (octets: number) => void = () => undefined,
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
        // standing beside it, so what is held at once is the answer and one chunk rather than the answer twice.
        chunks.push(chunk.value.slice());
        arrived(octets);
    }
}
