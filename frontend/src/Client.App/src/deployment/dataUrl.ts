// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

// Turning octets a deployment answered with into an address the client may draw them at, for the two adapters in this
// directory that draw one: the picture the signed-in person is drawn by, and a picture a message carries.
//
// It is one function rather than one per adapter for the reason `boundedBody.ts` beside it gives about the bound — the
// decision is the same each time, and the alternative to a data URL is the same each time too.
//
// **A data URL rather than a blob URL, and that is the decision this module records.** A blob URL keeps its octets
// alive until somebody releases it, so it puts a lifetime on a value that travels through several components and is
// discarded by a screen closing rather than by a call — which is a leak nothing in a type would catch. A picture read
// under a bound costs less held as text than that bookkeeping costs in defects, and the bound is what makes that true:
// both callers refuse an answer larger than a few megabytes before this is reached.

/**
 * The address the client may draw a picture at, from the octets it arrived as.
 *
 * `FileReader` rather than encoding by hand, because the platform already does exactly this and a megabyte encoded a
 * character at a time is the loop nobody should write twice.
 *
 * @param picture The octets, under the media type they are to be drawn as.
 * @returns The address, which rejects where the platform could not read what it was handed.
 */
export function asDataUrl(picture: Blob): Promise<string> {
    return new Promise((resolve, reject) => {
        const reading = new FileReader();

        reading.onload = () => {
            resolve(typeof reading.result === 'string' ? reading.result : '');
        };
        reading.onerror = () => {
            reject(reading.error ?? new Error('The octets could not be read as an address to draw them at.'));
        };
        reading.readAsDataURL(picture);
    });
}
