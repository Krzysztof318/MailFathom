// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { createElement, useId, useRef, useState, type KeyboardEvent, type ReactNode } from 'react';
import { Icon } from '../controls/Icon';
import type { IconName } from '../controls/icons';
import type { MessageKey } from '../localization/en';
import { useLocalization } from '../localization/useLocalization';
import { useCoarsePointer } from '../shell/useWideWorkspace';
import { followable, writtenIn, type WrittenNode } from './writtenText';

// The words of a message being written, as the design project draws them: an editable region over a row of formatting
// controls, with the row replaced by a toggle where a finger is what drives the screen.
//
// **The region is the document's rather than React's.** What is inside a `contenteditable` is written by the browser
// as somebody types, so re-rendering it from state would put the caret back at the start on every keystroke. The tree
// it opens holding is therefore rendered once and never again — `opening` freezes it at the first render — and every
// later reading goes the other way, out of the document and into the closed set `writtenText.ts` states. Nothing here
// writes markup and nothing parses any, which is what lets an editor exist at all under ADR 0024's rule.
//
// **`execCommand` is the editing, and it is deprecated with nothing to replace it.** No standard succeeded it, every
// engine implements it, and the alternative is a document-editing model this client would have to write and test
// itself. What it is told first is to compose with elements rather than with inline styles, which is what keeps its
// output inside the set above.

/** One control on the formatting bar: what it is called, what it does, and how the design draws it. */
interface FormattingControl {
    readonly key: string;

    /** The editing command it performs, and the argument the command takes where it takes one. */
    readonly command: string;
    readonly argument?: string;

    readonly named: MessageKey;

    /** The typographic mark the design draws it as, for the six that are read as letters rather than as symbols. */
    readonly mark?: string;

    /** How that mark is drawn, which is what makes the bold control look bold. */
    readonly marked?: string;

    /** The symbol the design draws it as, for the three that are not letters. */
    readonly icon?: IconName;

    /** Whether it asks for an address before it acts, which only the link does. */
    readonly asksForAnAddress?: true;
}

// The design's own nine, in the design's own order.
const formattingControls: readonly FormattingControl[] = [
    { key: 'bold', command: 'bold', named: 'compose.formatBold', mark: 'B', marked: 'font-semibold' },
    { key: 'italic', command: 'italic', named: 'compose.formatItalic', mark: 'I', marked: 'font-serif italic' },
    { key: 'underline', command: 'underline', named: 'compose.formatUnderline', mark: 'U', marked: 'underline' },
    {
        key: 'strikethrough',
        command: 'strikeThrough',
        named: 'compose.formatStrikethrough',
        mark: 'S',
        marked: 'line-through',
    },
    { key: 'bulleted', command: 'insertUnorderedList', named: 'compose.formatBulletedList', mark: '•—' },
    { key: 'numbered', command: 'insertOrderedList', named: 'compose.formatNumberedList', mark: '1.' },
    {
        key: 'quote',
        command: 'formatBlock',
        argument: 'blockquote',
        named: 'compose.formatQuote',
        icon: 'format_quote',
    },
    { key: 'link', command: 'createLink', named: 'compose.formatLink', icon: 'link', asksForAnAddress: true },
    { key: 'clear', command: 'removeFormat', named: 'compose.formatClear', icon: 'format_clear' },
];

export function WrittenMessage({
    opened,
    onWritten,
    onSendAsked,
}: {
    /** What the region opens holding, read once: a message this tab was already writing, or nothing. */
    readonly opened: readonly WrittenNode[];

    readonly onWritten: (written: readonly WrittenNode[]) => void;

    /** What the design's own shortcut asks for, which is the send question rather than the send. */
    readonly onSendAsked: () => void;
}) {
    const { translate } = useLocalization();
    const coarse = useCoarsePointer();
    const [opening] = useState(opened);
    const [barShown, setBarShown] = useState(false);
    const [address, setAddress] = useState<string | null>(null);
    const words = useRef<HTMLDivElement>(null);
    const linking = useRef<Range | null>(null);
    const addressId = useId();

    // What the design decides about the bar: drawn outright where a pointer is fine, and behind a toggle where a
    // finger is what drives the screen — the keyboard and the message needing the room a bar would take.
    const drawTheBar = !coarse || barShown;

    function written(): void {
        if (words.current !== null) {
            onWritten(writtenIn(words.current));
        }
    }

    function apply(command: string, argument?: string): void {
        words.current?.focus();

        // The selection a control took the focus away from, put back before the command reads it. Only the link asks
        // for anything, so this is null for the other eight and they act on what the region still holds.
        const held = linking.current;

        if (held !== null) {
            const selection = window.getSelection();

            selection?.removeAllRanges();
            selection?.addRange(held);
            linking.current = null;
        }

        try {
            // Elements rather than inline styles, so what the commands below compose stays inside the closed set
            // `writtenText.ts` states rather than arriving as a span this client would unwrap and lose.
            edit('styleWithCSS', 'false');
            edit(command, argument);
        } catch {
            // A command an engine does not carry leaves the message exactly as it was, which is the whole of what it
            // costs: nothing here reads a return value, and refusing to draw the control would be worse.
        }

        written();
    }

    function askForAnAddress(): void {
        const selection = window.getSelection();

        linking.current = selection !== null && selection.rangeCount > 0 ? selection.getRangeAt(0) : null;
        setAddress('');
    }

    // Giving the address up, which is both what escape does and what an address this client would not follow leaves.
    // The held selection goes with it: kept, it would be put back over whatever was selected next, and the control
    // pressed after a link somebody thought better of would act somewhere nobody was looking.
    function giveUpTheAddress(): void {
        linking.current = null;
        setAddress(null);
        words.current?.focus();
    }

    function insertTheLink(): void {
        const written = address ?? '';

        if (followable(written)) {
            setAddress(null);
            apply('createLink', written);
        } else {
            giveUpTheAddress();
        }
    }

    return (
        <>
            {drawTheBar ? (
                <div
                    role="group"
                    aria-label={translate('compose.formatting')}
                    className="flex shrink-0 flex-wrap items-center gap-0.5 border-b border-line-soft px-3 py-1.5 pointer-coarse:gap-1.5 pointer-coarse:py-2.25"
                >
                    {formattingControls.map((control) => (
                        <button
                            key={control.key}
                            type="button"
                            aria-label={translate(control.named)}
                            aria-expanded={control.asksForAnAddress === true ? address !== null : undefined}
                            className={`flex h-7.5 min-w-7.5 items-center justify-center rounded-md px-1.75 text-base text-text-soft transition hover:bg-hover hover:text-text pointer-coarse:border pointer-coarse:border-line ${
                                control.marked ?? ''
                            }`}
                            onMouseDown={(event) => {
                                // The selection is what every one of these acts on, and a press that moved the focus
                                // would have collapsed it before the command ran.
                                event.preventDefault();
                            }}
                            onClick={() => {
                                if (control.asksForAnAddress === true) {
                                    askForAnAddress();
                                } else {
                                    apply(control.command, control.argument);
                                }
                            }}
                        >
                            {control.icon === undefined ? (
                                control.mark
                            ) : (
                                <Icon name={control.icon} className="size-4.25" />
                            )}
                        </button>
                    ))}

                    {coarse ? null : (
                        <p className="ms-auto text-xs text-faint">{translate('compose.formattingHint')}</p>
                    )}
                </div>
            ) : null}

            {address === null ? null : (
                <div className="flex shrink-0 flex-wrap items-center gap-2.5 border-b border-line-soft px-3.75 py-2.25">
                    <label htmlFor={addressId} className="w-22 shrink-0 text-sm text-muted">
                        {translate('compose.linkAddress')}
                    </label>

                    <input
                        id={addressId}
                        autoFocus
                        value={address}
                        inputMode="url"
                        placeholder={translate('compose.linkAddressPlaceholder')}
                        className="min-w-40 flex-1 border-none bg-transparent text-base text-text outline-none placeholder:text-faint"
                        onChange={(event) => {
                            setAddress(event.target.value);
                        }}
                        onKeyDown={(event) => {
                            if (event.key === 'Enter') {
                                event.preventDefault();
                                insertTheLink();
                            }

                            if (event.key === 'Escape') {
                                event.preventDefault();
                                giveUpTheAddress();
                            }
                        }}
                    />

                    <button
                        type="button"
                        className="shrink-0 rounded-md px-2 py-1 text-sm font-semibold text-accent-deep transition hover:bg-hover"
                        onClick={insertTheLink}
                    >
                        {translate('compose.insertLink')}
                    </button>
                </div>
            )}

            {coarse ? (
                <button
                    type="button"
                    aria-expanded={barShown}
                    className="flex shrink-0 items-center gap-2 border-b border-line-soft px-3.25 py-2 text-base text-text-soft transition hover:bg-hover"
                    onClick={() => {
                        setBarShown(!barShown);
                    }}
                >
                    <Icon name="text_format" className="size-5.25" />
                    <span className="min-w-0 flex-1 text-start">
                        {translate(barShown ? 'compose.formattingHide' : 'compose.formatting')}
                    </span>
                    <Icon name={barShown ? 'expand_less' : 'expand_more'} className="size-5 text-muted" />
                </button>
            ) : null}

            <div
                ref={words}
                role="textbox"
                aria-multiline="true"
                aria-label={translate('compose.words')}
                data-placeholder={translate('compose.wordsPlaceholder')}
                contentEditable
                suppressContentEditableWarning
                tabIndex={0}
                className="min-h-42.5 flex-1 overflow-auto px-4.25 py-4 text-lg text-text-soft outline-none"
                onInput={written}
                onPaste={(event) => {
                    // What arrives from somewhere else arrives as text. A message carries the formatting its author
                    // gave it with these controls, and nothing this client did not write reaches the tree at all.
                    event.preventDefault();
                    apply('insertText', event.clipboardData.getData('text/plain'));
                }}
                onKeyDown={(event: KeyboardEvent<HTMLDivElement>) => {
                    // The design's own shortcut, and it opens the confirmation rather than sending: what a keyboard
                    // saves is reaching for the control, never the reading of who the message is for.
                    if (event.key === 'Enter' && (event.ctrlKey || event.metaKey)) {
                        event.preventDefault();
                        onSendAsked();
                    }
                }}
            >
                <WrittenTree written={opening} />
            </div>
        </>
    );
}

// The editing itself, named once rather than at each call site. `document.execCommand` is deprecated with nothing
// standard to replace it: no successor shipped, every engine implements it, and the alternative is a document-editing
// model this client would have to write and test itself.
function edit(command: string, argument?: string): void {
    // eslint-disable-next-line @typescript-eslint/no-deprecated -- The reason is the comment above this function.
    document.execCommand(command, false, argument);
}

// What was already written, drawn as ordinary elements once. It is rendered from the closed set rather than from
// markup, which is what keeps a parser off this path: the region's first content is React's, and every reading after
// that goes the other way.
function WrittenTree({ written }: { readonly written: readonly WrittenNode[] }): ReactNode {
    return (
        <>
            {written.map((node, position) => (
                <WrittenPart key={position} node={node} />
            ))}
        </>
    );
}

function WrittenPart({ node }: { readonly node: WrittenNode }): ReactNode {
    if ('text' in node) {
        return node.text;
    }

    if (node.element === 'br') {
        return <br />;
    }

    return createElement(
        node.element,
        node.address === null ? null : { href: node.address },
        <WrittenTree written={node.holds} />,
    );
}
