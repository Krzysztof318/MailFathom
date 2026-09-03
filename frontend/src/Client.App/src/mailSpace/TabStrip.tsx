// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useRef, useState, type KeyboardEvent } from 'react';
import { Icon } from '../controls/Icon';
import type { IconName } from '../controls/icons';
import { useLocalization } from '../localization/useLocalization';
import { closed, type OpenTab, type OpenTabKind } from './openTabs';

// The strip of what is open, which the design project draws above the content pane on a wide screen. It is a tab list
// to the platform rather than a row of buttons that look like one: the arrow keys move between tabs, Enter and Space
// bring one forward, Delete closes the one focused, and the one being read is the one reported as selected.
//
// Manual activation rather than automatic, which is what the width of this decision buys: bringing a tab forward reads
// a message, so a reader arrowing along the strip to reach the fourth tab would otherwise start three reads they never
// asked for.
//
// The close control sits beside the tab rather than inside it, and that is a rule rather than a layout preference: the
// `tab` role makes its own contents presentational, so a button drawn inside one is a button a screen reader never
// announces. It is reached with Tab from the tab it belongs to, and Delete is the path that needs no pointer at all.

const kindIcons: Readonly<Record<OpenTabKind, IconName>> = {
    thread: 'mail',
    attachment: 'description',
    fullHtml: 'code',
    draft: 'edit_square',
};

export function TabStrip({
    tabs,
    active,
    onActivate,
    onClose,
    onCloseEverything,
}: {
    readonly tabs: readonly OpenTab[];

    /** Which tab the content pane is drawing, or `null` where the pane is drawing none of them. */
    readonly active: string | null;

    readonly onActivate: (key: string) => void;
    readonly onClose: (key: string) => void;
    readonly onCloseEverything: () => void;
}) {
    const { translate } = useLocalization();
    const [reached, setReached] = useState<string | null>(null);
    const [followed, setFollowed] = useState(active);
    const buttons = useRef(new Map<string, HTMLButtonElement>());

    // Arrowing along the strip moves focus without bringing a tab forward, so where the keyboard has got to is its own
    // fact rather than the tab being read. Opening a message resets it, because focus following the arrow keys is a
    // place inside the strip and reading a different message is somewhere else entirely.
    if (followed !== active) {
        setFollowed(active);
        setReached(active);
    }

    // Only one tab is in the tab order, which is what a tab list is: reaching the strip and then arrowing along it is
    // one stop rather than one per tab. The tab being read stands in where the keyboard has not been here, and the
    // first tab where nothing is being read, so the strip is never a region the keyboard cannot enter.
    const focusable = tabs.some((tab) => tab.key === reached) ? reached : (active ?? tabs[0]?.key ?? null);

    function focusOn(key: string | null): void {
        if (key !== null) {
            setReached(key);
            buttons.current.get(key)?.focus();
        }
    }

    // Where the reader is left after a close, decided by the same rule that decides which tab is read next rather than
    // by a second copy of it. Nothing is focused where the last tab went: the strip is not on the screen any more, and
    // what replaced it is what takes focus.
    function close(key: string): void {
        focusOn(closed({ tabs, active }, key).active);
        onClose(key);
    }

    function onKeyDown(event: KeyboardEvent<HTMLDivElement>): void {
        const at = tabs.findIndex((tab) => tab.key === focusable);

        if (at < 0) {
            return;
        }

        switch (event.key) {
            case 'ArrowRight':
                focusOn(tabs[Math.min(at + 1, tabs.length - 1)]?.key ?? null);
                break;
            case 'ArrowLeft':
                focusOn(tabs[Math.max(at - 1, 0)]?.key ?? null);
                break;
            case 'Home':
                focusOn(tabs[0]?.key ?? null);
                break;
            case 'End':
                focusOn(tabs[tabs.length - 1]?.key ?? null);
                break;
            case 'Delete':
                close(tabs[at]?.key ?? '');
                break;
            default:
                return;
        }

        event.preventDefault();
    }

    return (
        <div className="flex shrink-0 items-stretch border-b border-line bg-sunken">
            <div
                role="tablist"
                aria-label={translate('tabs.strip')}
                className="flex min-w-0 flex-1 items-end gap-0.75 overflow-x-auto pt-1.75 pe-1 ps-3.5"
                onKeyDown={onKeyDown}
            >
                {tabs.map((tab) => {
                    const selected = tab.key === active;
                    const title = tab.title ?? translate('message.noSubject');

                    return (
                        // Presentational, because what the tab list owns is the tab: the shape a reader sees is one
                        // thing and the two controls inside it are two, and only the first of them is a tab.
                        <div
                            key={tab.key}
                            role="presentation"
                            className={`flex max-w-tab items-center rounded-t-xl border ${
                                selected ? 'border-line bg-panel text-text' : 'border-transparent text-muted'
                            }`}
                        >
                            <button
                                ref={(button) => {
                                    if (button === null) {
                                        buttons.current.delete(tab.key);
                                    } else {
                                        buttons.current.set(tab.key, button);
                                    }
                                }}
                                type="button"
                                role="tab"
                                aria-selected={selected}
                                aria-keyshortcuts="Delete"
                                tabIndex={tab.key === focusable ? 0 : -1}
                                className="flex min-w-0 items-center gap-2 py-2 ps-2.75 text-base whitespace-nowrap transition hover:text-text"
                                onClick={() => {
                                    setReached(tab.key);
                                    onActivate(tab.key);
                                }}
                            >
                                <Icon name={kindIcons[tab.kind]} className="size-3.75 shrink-0 text-faint" />
                                <span className={`truncate ${selected ? 'font-semibold' : ''}`}>{title}</span>
                            </button>

                            <button
                                type="button"
                                aria-label={translate('tabs.close', { title })}
                                tabIndex={tab.key === focusable ? 0 : -1}
                                className="me-1.5 ms-1 flex size-5 shrink-0 items-center justify-center rounded-xs text-faint transition hover:bg-hover hover:text-text"
                                onClick={() => {
                                    close(tab.key);
                                }}
                            >
                                <Icon name="close" className="size-3.75" />
                            </button>
                        </div>
                    );
                })}
            </div>

            <CloseEverything
                open={tabs.length}
                draft={tabs.some((tab) => tab.kind === 'draft')}
                onConfirm={onCloseEverything}
            />
        </div>
    );
}

// Closing everything at once is the one act here that cannot be undone by pressing the thing again, so it is confirmed
// and the confirmation says what it costs: how many tabs go, and — where one of them is a draft nobody has sent — that
// the words in it go with them.
//
// The platform's own dialog, so the page behind it is inert, focus is held inside it, Escape leaves it, and leaving it
// puts focus back on the control that opened it — four things none of which is written here. The control and the
// question belong together for that last one: whether the dialog is open is the element's own state rather than a
// second copy of it in React, so both answers leave through `close()` and the platform restores focus either way.
// Which answer it was travels the way the platform carries it, in the return value.
//
// The design project draws the sentence about a draft unconditionally; it is said here only where a draft is actually
// open, because a confirmation that names a consequence that will not happen is a confirmation a reader learns to stop
// reading.
const everythingCloses = 'close-everything';

function CloseEverything({
    open,
    draft,
    onConfirm,
}: {
    readonly open: number;
    readonly draft: boolean;
    readonly onConfirm: () => void;
}) {
    const { locale, translate } = useLocalization();
    const asked = useRef<HTMLDialogElement>(null);

    return (
        <>
            <button
                type="button"
                aria-label={translate('tabs.closeAll')}
                title={translate('tabs.closeAll')}
                className="mx-1.5 my-auto flex size-7 shrink-0 items-center justify-center rounded-md text-muted transition hover:bg-hover hover:text-text"
                onClick={() => {
                    asked.current?.showModal();
                }}
            >
                <Icon name="cancel" className="size-4.5" />
            </button>

            <dialog
                ref={asked}
                aria-labelledby="close-every-tab"
                className="m-auto w-96 max-w-full rounded-3xl border border-line bg-panel p-5 text-text shadow-dialog backdrop:bg-scrim"
                onClose={() => {
                    if (asked.current?.returnValue === everythingCloses) {
                        onConfirm();
                    }
                }}
            >
                <div className="flex flex-col gap-3.5">
                    <h2 id="close-every-tab" className="text-xl font-semibold">
                        {translate('tabs.closeAllQuestion')}
                    </h2>

                    <p className="text-base text-muted">
                        {translate('tabs.closeAllOpen', { count: new Intl.NumberFormat(locale).format(open) })}
                        {draft ? ` ${translate('tabs.closeAllDraft')}` : ''}
                    </p>

                    <div className="flex justify-end gap-2">
                        <button
                            type="button"
                            className="rounded-lg border border-line bg-sunken px-3.75 py-2 text-base text-text-soft transition hover:bg-hover"
                            onClick={() => {
                                asked.current?.close();
                            }}
                        >
                            {translate('tabs.closeAllCancel')}
                        </button>

                        <button
                            type="button"
                            className="rounded-lg bg-accent px-4 py-2 text-base font-semibold text-on-accent transition hover:opacity-90"
                            onClick={() => {
                                asked.current?.close(everythingCloses);
                            }}
                        >
                            {translate('tabs.closeAllConfirm')}
                        </button>
                    </div>
                </div>
            </dialog>
        </>
    );
}
