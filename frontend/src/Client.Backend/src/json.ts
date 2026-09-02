// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

// The one shape every parser in this package starts from. A response body is untrusted input, so nothing here reads a
// field off a value before that value has been established to be an object with fields at all — and `typeof null` and
// `typeof []` both answer `'object'`, which is the whole reason this is a function rather than a check written inline.
//
// It is stated once for the package rather than per operation: three parsers needed it, and three copies of the same
// two lines is three places for one of them to stop refusing an array.

/** Whether the value is an object with fields, which an array and `null` are not. */
export function isRecord(value: unknown): value is Readonly<Record<string, unknown>> {
    return typeof value === 'object' && value !== null && !Array.isArray(value);
}

/** The value read as a record of fields, or `null` where it is anything else. */
export function asRecord(value: unknown): Readonly<Record<string, unknown>> | null {
    return isRecord(value) ? value : null;
}
