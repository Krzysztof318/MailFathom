// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { isPortraitImageType, largestPortraitOctets, type PortraitImageType } from '@mailfathom/client-backend';

// Whether a file somebody picked may be sent at all. The deployment refuses both of these itself — it reads the kind
// from the signature the file opens with, which is the check that counts — and this one exists so that a person who
// picked the wrong file is told at the control they picked it with rather than after a megabyte has travelled and come
// back as a failed request.
//
// It takes the two facts a browser reports about a file rather than the file, so it is a function over values: what
// answers it is not a `File` this module had to build to be tested.

/** Whether a file may be sent as somebody's portrait, and under which kind where it may. */
export type PortraitChoice =
    | { readonly outcome: 'admissible'; readonly type: PortraitImageType }
    | { readonly outcome: 'notAnImageKind' }
    | { readonly outcome: 'largerThanAllowed' };

/**
 * Judges a chosen file against the two things this surface bounds a portrait by.
 *
 * @param type The kind the browser reported for the file, which is what its name and its registry say rather than what
 * its octets are — the deployment reads the signature, and this is only what can be known before sending.
 * @param octets How large the file is.
 * @returns That it may be sent and under which kind, or which of the two bounds it failed.
 */
export function chosenPortrait(type: string, octets: number): PortraitChoice {
    if (!isPortraitImageType(type)) {
        return { outcome: 'notAnImageKind' };
    }

    return octets > largestPortraitOctets ? { outcome: 'largerThanAllowed' } : { outcome: 'admissible', type };
}
