// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

// What somebody has written, as a closed tree rather than as markup, and the two readings a message going out needs
// from it: the HTML part and the plain-text alternative beside it.
//
// **Nothing here parses a string into nodes.** ADR 0024 states that for a message arriving, and the reasoning holds
// just as firmly for one going out: the composer reads its own editable region node by node into the closed set below,
// renders that set back as ordinary elements, and writes the HTML part by escaping every value it puts in one. So no
// markup is parsed anywhere on this path, no sanitizer is pinned, and what a poisoned session store could put back on
// the screen is bounded by what this type can express rather than by what a filter remembered to strip.
//
// The set is DOM-shaped rather than semantic, and deliberately so. What produces it is a browser editing a
// `contenteditable` region, and each engine wraps a line, a list, and an emphasis its own way — a semantic block model
// would have to guess which of those it was looking at, and would lose whatever it guessed wrong. Reading the shape
// that is actually there loses nothing, and closing the *set* is what makes it safe.

/** The elements a written message may be made of, which is what the formatting controls produce and nothing else. */
export type WrittenElement =
    'a' | 'b' | 'blockquote' | 'br' | 'div' | 'em' | 'i' | 'li' | 'ol' | 'p' | 's' | 'strike' | 'strong' | 'u' | 'ul';

/** A run of text somebody typed. */
export interface WrittenText {
    readonly text: string;
}

/** One element around what it holds. */
export interface WrittenMarkup {
    readonly element: WrittenElement;

    /** Where a link goes, which only a link carries and which is always one this client would follow. */
    readonly address: string | null;

    readonly holds: readonly WrittenNode[];
}

export type WrittenNode = WrittenText | WrittenMarkup;

const writableElements: readonly string[] = [
    'a',
    'b',
    'blockquote',
    'br',
    'div',
    'em',
    'i',
    'li',
    'ol',
    'p',
    's',
    'strike',
    'strong',
    'u',
    'ul',
];

// The schemes a link written here may name. Everything else — `javascript:`, `data:`, a scheme nobody has heard of —
// is a link the composer refuses to make rather than one a reader has to be careful about, and a relative address is
// refused with them: what a message says is read on somebody else's machine, where this deployment's paths mean
// nothing.
const followableSchemes: readonly string[] = ['http:', 'https:', 'mailto:'];

// What a written message may carry. They bound both directions rather than only the read: a message the composer
// wrote past one of them would be kept and then refused on the next reload, which is a draft lost silently — so
// `writtenIn` holds to the depth below as it reads, and the other two are set far above anything typing produces.
const mostWrittenNodes = 100_000;
const longestWrittenRun = 100_000;
const deepestWriting = 64;

/** Whether a link written here is one this client would make, which is a scheme question rather than a spelling one. */
export function followable(address: string): boolean {
    try {
        return followableSchemes.includes(new URL(address).protocol);
    } catch {
        // An address that is not one at all is not one to link to either.
        return false;
    }
}

/**
 * What is written inside an element, read into the closed set above.
 *
 * An element outside the set is unwrapped rather than dropped, so what somebody typed inside one survives a wrapper
 * this client has no name for — which is what an engine leaves behind after an editing command it composed its own way.
 */
export function writtenIn(parent: Node, depth = 0): readonly WrittenNode[] {
    const written: WrittenNode[] = [];

    // Past the depth a kept message may be read back at, an element is unwrapped rather than kept. Reading deeper
    // would compose a message `writtenTextIn` then refuses, and a draft that survives being written and not the
    // reload after it is a draft lost with nothing said.
    if (depth >= deepestWriting) {
        return [{ text: parent.textContent ?? '' }];
    }

    for (const node of [...parent.childNodes]) {
        if (node.nodeType === Node.TEXT_NODE) {
            const text = node.nodeValue ?? '';

            if (text !== '') {
                written.push({ text });
            }

            continue;
        }

        if (!(node instanceof Element)) {
            continue;
        }

        const named = node.tagName.toLowerCase();
        const holds = writtenIn(node, depth + 1);

        if (!writableElements.includes(named)) {
            written.push(...holds);

            continue;
        }

        const element = named as WrittenElement;
        const address = element === 'a' ? node.getAttribute('href') : null;

        // A link to nowhere is a link this client did not make, so what it held is kept and the link itself is not.
        if (element === 'a' && (address === null || !followable(address))) {
            written.push(...holds);

            continue;
        }

        written.push({ element, address, holds });
    }

    return written;
}

/** The HTML part of a message going out, with every value in it escaped rather than trusted. */
export function htmlOf(written: readonly WrittenNode[]): string {
    return written.map(markupOf).join('');
}

/**
 * The plain-text alternative every message goes out with beside its HTML part.
 *
 * It is a reading of the same tree rather than a second thing to keep in step with it, which is why the composer holds
 * one message and not two: a pair that can disagree is a message whose two halves say different things to two readers.
 */
export function plainTextOf(written: readonly WrittenNode[]): string {
    return collapsed(textOf(written, null));
}

/**
 * A written message read back out of somewhere a person can write, which is what a session store is.
 *
 * Answers `null` for anything this client did not write, bounds included, because a message with a hole in it is worse
 * than an empty composer: it reaches the confirmation as words nobody typed.
 */
export function writtenTextIn(value: unknown): readonly WrittenNode[] | null {
    let remaining = mostWrittenNodes;

    function nodesIn(held: unknown, depth: number): readonly WrittenNode[] | null {
        if (!Array.isArray(held) || depth > deepestWriting) {
            return null;
        }

        const written: WrittenNode[] = [];

        for (const node of held) {
            remaining -= 1;

            if (remaining < 0) {
                return null;
            }

            const read = nodeIn(node, depth);

            if (read === null) {
                return null;
            }

            written.push(read);
        }

        return written;
    }

    function nodeIn(node: unknown, depth: number): WrittenNode | null {
        if (typeof node !== 'object' || node === null || Array.isArray(node)) {
            return null;
        }

        const record = node as Record<string, unknown>;
        const text = record['text'];

        if (text !== undefined) {
            return typeof text === 'string' && text.length <= longestWrittenRun ? { text } : null;
        }

        const element = record['element'];
        const address = record['address'] ?? null;

        if (typeof element !== 'string' || !writableElements.includes(element)) {
            return null;
        }

        // Only a link carries one, which is what `writtenIn` writes and therefore the whole of what this may read
        // back: an address on anything else is a stored tree this client did not compose, and it would otherwise
        // leave as an `href` on an element that has no business holding one.
        if (address !== null && (element !== 'a' || typeof address !== 'string' || !followable(address))) {
            return null;
        }

        const holds = nodesIn(record['holds'], depth + 1);

        return holds === null ? null : { element: element as WrittenElement, address, holds };
    }

    return nodesIn(value, 0);
}

function markupOf(node: WrittenNode): string {
    if ('text' in node) {
        return escaped(node.text);
    }

    if (node.element === 'br') {
        return '<br />';
    }

    const address = node.address === null ? '' : ` href="${escaped(node.address).replace(/"/gu, '&quot;')}"`;

    return `<${node.element}${address}>${htmlOf(node.holds)}</${node.element}>`;
}

function escaped(text: string): string {
    return text.replace(/&/gu, '&amp;').replace(/</gu, '&lt;').replace(/>/gu, '&gt;');
}

// The text a tree reads as, before the whitespace it accumulated is collapsed. A list is walked knowing which kind it
// is, because that is the one thing an item cannot see from where it stands.
function textOf(written: readonly WrittenNode[], list: 'ol' | 'ul' | null): string {
    let items = 0;
    let text = '';

    for (const node of written) {
        if ('text' in node) {
            // The whitespace collapsing a browser does when it draws the same text, so a tree that arrived across
            // several source lines reads as the one line somebody was looking at.
            text += node.text.replace(/\s+/gu, ' ');

            continue;
        }

        switch (node.element) {
            case 'br':
                text += '\n';
                break;
            case 'li':
                items += 1;
                text += `${list === 'ol' ? `${items.toFixed(0)}. ` : '- '}${textOf(node.holds, null).trim()}\n`;
                break;
            case 'blockquote':
                text += `${quoted(textOf(node.holds, null))}\n`;
                break;
            case 'ol':
            case 'ul':
                text += `${textOf(node.holds, node.element)}\n`;
                break;
            case 'p':
                // A paragraph is a break in the reading rather than the next line, which is what the blank line
                // between two of them says. A `div` is how an engine writes one line after another and gets one.
                text += `${textOf(node.holds, null)}\n\n`;
                break;
            case 'div':
                text += `${textOf(node.holds, null)}\n`;
                break;
            default:
                text += textOf(node.holds, null);
                break;
        }
    }

    return text;
}

// A quotation as mail has always written one, so that a reader whose client shows the plain-text part still sees which
// half of the message is somebody else's.
function quoted(text: string): string {
    return text
        .trim()
        .split('\n')
        .map((line) => (line === '' ? '>' : `> ${line}`))
        .join('\n');
}

function collapsed(text: string): string {
    return text
        .replace(/[^\S\n]+$/gmu, '')
        .replace(/\n{3,}/gu, '\n\n')
        .trim();
}
