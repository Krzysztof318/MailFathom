// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import type { Locale } from '../localization/locale';

// How large a file is, worded rather than counted out. The unit names are `Intl`'s under the active language, so no
// catalogue holds an abbreviation for a kilobyte and neither language's form has to be maintained beside the other's.

// The units a size is expressed in, smallest first, each with how many octets one of them holds. They are the sanctioned
// unit identifiers `Intl.NumberFormat` accepts, which is why the list stops where it does rather than at a round number.
const units = [
    { unit: 'byte', octets: 1 },
    { unit: 'kilobyte', octets: 1_000 },
    { unit: 'megabyte', octets: 1_000_000 },
    { unit: 'gigabyte', octets: 1_000_000_000 },
    { unit: 'terabyte', octets: 1_000_000_000_000 },
] as const;

/**
 * How large something is, in the largest unit that leaves a number a person reads at a glance.
 *
 * @param octets How many octets it holds, as the deployment reported it.
 * @param locale The language the wording is asked for in.
 * @returns The size as a person reads it.
 */
export function sizeOf(octets: number, locale: Locale): string {
    const chosen = units.findLast((candidate) => octets >= candidate.octets) ?? units[0];

    return new Intl.NumberFormat(locale, {
        style: 'unit',
        unit: chosen.unit,
        unitDisplay: 'short',

        // Whole octets and one place above them: half a kilobyte is worth saying and half an octet is not, and a file
        // reported to three decimal places reads as a measurement rather than as a size.
        maximumFractionDigits: chosen.unit === 'byte' ? 0 : 1,
    }).format(octets / chosen.octets);
}
