// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useEffect, useRef, useState, type ReactNode } from 'react';
import { useComposing } from '../composer/useComposing';
import { Control } from '../controls/Control';
import { Icon } from '../controls/Icon';
import { PlannedControl } from '../controls/PlannedControl';
import { SurfaceControl } from '../controls/SurfaceControl';
import { useLocalization } from '../localization/useLocalization';
import { useScreenLayer } from '../shell/screenLayers';
import { useDesktopComposition, useTwoPanes, useWideWorkspace } from '../shell/useWideWorkspace';
import { scopeKey } from '../workspace/mailScope';
import { useWorkspace } from '../workspace/useWorkspace';
import { AiFilters } from './AiFilters';
import { ListWidthGrip } from './ListWidthGrip';
import { listWidthWithin, readListWidth, storeListWidth } from './listWidth';
import { MailToolbar } from './MailToolbar';
import { SelectionBar } from './SelectionBar';

// The Mail space as the design project composes it, out of the three regions a mail client is: the mailboxes, the list
// of what is in the one that is scoped, and what is open from it. What this component owns is the composition alone —
// each region is handed in already built — and the composition comes out of one tree, decided by the width the space
// has been given rather than by which head it runs on.
//
// **Three widths decide it, and they are three separate questions rather than one asked three ways.** The design
// project's prototype asks exactly these, and `shell/useWideWorkspace.ts` is where each is asked once:
//
// - *Two panes or one.* Below that width the space draws the list **or** what is open and never both, so a row that is
//   not on the screen is not in the document either; above it the two stand side by side with a boundary to move.
// - *The mailbox column or a drawer.* Only the desktop composition has room for a third column, so at the tablet and
//   the fold the mailboxes stand in a drawer over the space, reached from a control in the list's own header.
// - *The toolbar at all*, which is the phone question: bottom navigation already spends the foot of a phone-width
//   window, and the toolbar's acts are on the selection bar and the message's own head there.
//
// So the four compositions the project frames fall out of those three: the phone has one pane, a drawer and no toolbar;
// the fold and the tablet have two panes, a drawer and a toolbar; the desktop has two panes, the mailbox column and the
// tab strip. Nothing is hidden by the width alone; what a narrower composition cannot show at once is reached rather
// than dropped.
//
// Where focus goes when the single-pane shape changes what it shows is this component's too: a message coming in front
// of the list is a view change, and so is going back, and a reader on a keyboard is put at the start of what replaced
// what they were on rather than left on an element that is no longer there. A resize that changes the shape is not, so
// the width changing moves nothing.
//
// How the wide shape divides its width between the list and the pane is the reader's, and this is where that lives: it
// is the composition's own number rather than either column's, and only the shape that draws both columns has a
// boundary to move.
//
// A person working in tabs gets one more row above all of it, and it is handed in already built for the reason every
// region is: what is open is the frame's rather than this composition's, and what this owns is that the strip stands
// over the whole space rather than over one column of it — and that the narrow shape draws none of it.

export function MailSpace({
    folders,
    list,
    mail,
    tabs,
    intent,
    status,
    person,
}: {
    /** The scope selector, which is the mailbox column's first thing. */
    readonly folders: ReactNode;

    /** The mail in scope, searchable, which is the middle column. */
    readonly list: ReactNode;

    /** What is open, which is the reading column. */
    readonly mail: ReactNode;

    /**
     * The strip naming everything open, which stands across the top of the space above the toolbar.
     *
     * `null` where a person is not working in tabs, or has nothing open — which is every narrow window, since a row of
     * tabs above a mailbox column and a list needs room the composition does not have there.
     */
    readonly tabs: ReactNode;

    /**
     * The question the reader is composing, which stands at the foot of the reading column — and, in the narrow shape,
     * at the foot of whichever column is on the screen, so that it is never behind a message somebody has to open.
     */
    readonly intent: ReactNode;

    /** What the deployment says about the connection, which stands at the foot of the mailbox column. */
    readonly status: ReactNode;

    /**
     * Who is signed in, which is what the split is kept under: two people sharing a machine each get their own.
     *
     * `null` where the credential this client holds names nobody it composed, which is a split that lasts the run
     * rather than a screen that refuses to draw.
     */
    readonly person: string | null;
}) {
    const { translate } = useLocalization();
    const { workspace, revise } = useWorkspace();
    const composing = useComposing();
    const wide = useWideWorkspace();
    const twoPanes = useTwoPanes();
    const desktop = useDesktopComposition();
    const [listWidth, setListWidth] = useState(() => readListWidth(person));
    const [drawerOpen, setDrawerOpen] = useState(false);
    const drawer = useRef<HTMLDialogElement>(null);
    const listColumn = useRef<HTMLElement>(null);
    const readingColumn = useRef<HTMLElement>(null);

    // What is being written stands where what is being read stands, so the narrow shape brings the reading column
    // in front of the list for a message somebody is writing exactly as it does for one they opened.
    const readingInFront =
        !twoPanes && (workspace.selection !== null || workspace.conversation !== null || composing.opening !== null);

    // Read from the workspace rather than held here, because the column is not the only thing that draws differently
    // once it is folded: the tree inside it draws a symbol where it drew a name, and the tree is a region handed in
    // already built rather than a child this component could pass a prop to.
    const folded = workspace.mailboxesFolded;

    // A width chosen on one screen is read back on whatever screen the client opens on next, so the window is measured
    // rather than trusted: what the two columns share is what they measure to, and a stored width the window no longer
    // has room for is brought back to what fits instead of pushing the message off the side.
    //
    // An effect because a `ResizeObserver` is something outside React, and it watches both columns because what the
    // two of them add up to is the measurement — a number nothing this sets can move, since the pane takes back
    // whatever the list gives up. So it settles in one pass rather than chasing itself.
    useEffect(() => {
        const listed = listColumn.current;
        const reading = readingColumn.current;

        if (!twoPanes || listed === null || reading === null || typeof ResizeObserver !== 'function') {
            return;
        }

        const watched = new ResizeObserver(() => {
            const room = listed.offsetWidth + reading.offsetWidth;

            if (room > 0) {
                setListWidth((chosen) => listWidthWithin(chosen, room));
            }
        });

        watched.observe(listed);
        watched.observe(reading);

        return () => {
            watched.disconnect();
        };
    }, [twoPanes]);

    // Focus is placed on the shape changing what it shows, and not on the width changing the shape: both are recorded
    // and only the first moves anything. A ref rather than state, because what it holds is what was last drawn.
    const shown = useRef({ twoPanes, readingInFront });

    useEffect(() => {
        const before = shown.current;
        shown.current = { twoPanes, readingInFront };

        if (before.twoPanes === twoPanes && before.readingInFront !== readingInFront) {
            (readingInFront ? readingColumn : listColumn).current?.focus();
        }
    }, [twoPanes, readingInFront]);

    // The drawer is a way to point at a mailbox, so pointing at one closes it: a reader who chose a folder wants the
    // folder's mail, which is behind the drawer they chose it in. The dialog is the platform's own, so closing it puts
    // focus back on the control that opened it without anything here remembering which control that was.
    const scope = scopeKey(workspace.scope);
    const shownScope = useRef(scope);

    useEffect(() => {
        if (shownScope.current !== scope) {
            shownScope.current = scope;
            drawer.current?.close();
        }
    }, [scope]);

    // The drawer belongs to the two compositions with no room for a mailbox column, so a window widened past that room
    // takes it off the screen without any way out of it firing `close`. What it was is given up as the composition
    // changes rather than in an effect answering that it did: the width is what caused it, so a render answers it and
    // nothing is left standing for a frame. Narrowing again therefore finds a drawer that is shut, which is what it is.
    const [composedForDesktop, setComposedForDesktop] = useState(desktop);

    if (composedForDesktop !== desktop) {
        setComposedForDesktop(desktop);
        setDrawerOpen(false);
    }

    // The drawer stands over the space rather than beside it, so the back gesture closes it before it takes a message
    // off the screen, and taking the navigation to another destination leaves none of it behind. Whether it is open is
    // held here because the platform publishes no such value: `close` is the one event every way out of it fires — the
    // control inside it, Escape, and the back gesture alike — so this cannot disagree with what is on the screen.
    useScreenLayer(drawerOpen, () => {
        drawer.current?.close();
    });

    function goBackToList(): void {
        revise({ selection: null, conversation: null });
    }

    // Every width a move produces is held inside the same bounds, so a drag and a key cannot disagree about where the
    // boundary may stand. A room of nothing is a layout that has not happened yet — an environment that computes no
    // sizes, or the frame before the first one — and is read as an unmeasured window rather than as no room at all.
    function withinTheRoom(width: number): number {
        const room = (listColumn.current?.offsetWidth ?? 0) + (readingColumn.current?.offsetWidth ?? 0);

        return listWidthWithin(width, room > 0 ? room : Number.POSITIVE_INFINITY);
    }

    return (
        <div className="flex min-h-0 flex-1 flex-col">
            {desktop ? tabs : null}

            {/* The bar replaces the toolbar while messages are picked out, which is the design project's composition:
                one strip saying what the next press is about. It stands at every width because the narrow shape has no
                toolbar to replace and a selection still needs both a way to act on it and a way out of it. */}
            {workspace.selected.length > 0 ? <SelectionBar /> : wide ? <MailToolbar /> : null}

            <div className="flex min-h-0 flex-1">
                {desktop ? (
                    <aside
                        aria-label={translate('mailboxes.open')}
                        className={`flex shrink-0 flex-col border-e border-line bg-sunken ${folded ? 'w-mailboxes-folded' : 'w-mailboxes'}`}
                    >
                        <Mailboxes
                            folders={folders}
                            status={status}
                            folded={folded}
                            control={
                                <SurfaceControl
                                    label={translate(folded ? 'mailboxes.unfold' : 'mailboxes.fold')}
                                    icon={folded ? 'chevron_right' : 'chevron_left'}
                                    onActivate={() => {
                                        revise({ mailboxesFolded: !folded });
                                    }}
                                />
                            }
                        />
                    </aside>
                ) : (
                    <dialog
                        ref={drawer}
                        onClose={() => {
                            setDrawerOpen(false);
                        }}
                        aria-label={translate('mailboxes.open')}
                        // It stands in the platform's top layer, which the frame's own safe-area padding is not around,
                        // so a drawer running the height of the screen carries the insets it crosses itself.
                        className="fixed inset-y-0 left-0 m-0 h-full max-h-none w-drawer max-w-full border-0 border-e border-line bg-sunken p-0 pt-safe-top pb-safe-bottom pl-safe-left text-text shadow-overlay backdrop:bg-scrim"
                    >
                        <div className="flex h-full flex-col">
                            <Mailboxes
                                folders={folders}
                                status={status}
                                folded={false}
                                control={
                                    <SurfaceControl
                                        label={translate('mailboxes.close')}
                                        icon="close"
                                        onActivate={() => {
                                            drawer.current?.close();
                                        }}
                                    />
                                }
                            />
                        </div>
                    </dialog>
                )}

                {twoPanes || !readingInFront ? (
                    <section
                        ref={listColumn}
                        tabIndex={-1}
                        aria-label={translate('mail.listColumn')}
                        className={`flex min-h-0 min-w-0 flex-col bg-panel ${twoPanes ? '' : 'flex-1'}`}
                        /* The one width in the client a person sets rather than the design, which is why it is drawn
                           from a value instead of a utility. The narrow shape has no boundary to move, so it takes the
                           whole column and this says nothing about it.
                           Left able to shrink on purpose: a width chosen on a wider screen is read back before
                           anything has measured this one, and a column that refuses to give way in that frame would
                           push the message off the side. Flexbox holds it inside the window until the measurement
                           below brings it back to a width that fits. */
                        style={twoPanes ? { width: `${String(listWidth)}px` } : undefined}
                    >
                        {desktop ? null : (
                            <div className="flex items-center px-2 pt-2">
                                <SurfaceControl
                                    label={translate('mailboxes.open')}
                                    icon="menu"
                                    onActivate={() => {
                                        drawer.current?.showModal();
                                        setDrawerOpen(true);
                                    }}
                                />
                            </div>
                        )}

                        {/* The list and, in the narrow shape, the control that writes a message standing over its
                            bottom corner. Over the *list* rather than over the window, because the window's own
                            bottom corner is where the question field and the navigation are: a control placed against
                            the viewport would sit on top of both, and it would move whenever either changed height.
                            Positioned against this box instead, it keeps the corner a thumb reaches whatever stands
                            under it. */}
                        <div className="relative flex min-h-0 flex-1 flex-col">
                            {list}

                            {twoPanes || readingInFront ? null : composing.offered ? (
                                <Control
                                    label={translate('mail.compose')}
                                    icon="edit_square"
                                    shape="floating"
                                    className="absolute right-4.5 bottom-4.5"
                                    onPress={() => {
                                        composing.compose({ kind: 'new' });
                                    }}
                                />
                            ) : (
                                <PlannedControl
                                    label={translate('mail.compose')}
                                    icon="edit_square"
                                    shape="floating"
                                    className="absolute right-4.5 bottom-4.5"
                                />
                            )}
                        </div>

                        {twoPanes ? null : intent}
                    </section>
                ) : null}

                {/* The boundary is only there where both columns are: one column at a time has nothing between them,
                    and the line the grip draws is the border the list would otherwise carry. */}
                {twoPanes ? (
                    <ListWidthGrip
                        width={listWidth}
                        onWidth={(moved) => {
                            setListWidth(withinTheRoom(moved));
                        }}
                        onChosen={(chosen) => {
                            const settled = withinTheRoom(chosen);

                            setListWidth(settled);
                            storeListWidth(person, settled);
                        }}
                    />
                ) : null}

                {twoPanes || readingInFront ? (
                    <section
                        ref={readingColumn}
                        tabIndex={-1}
                        aria-label={translate('mail.readingColumn')}
                        className="flex min-h-0 min-w-0 flex-1 flex-col bg-panel"
                    >
                        {twoPanes ? null : (
                            <div className="flex items-center px-2 pt-2">
                                <button
                                    type="button"
                                    className="flex items-center gap-1.5 rounded-lg px-2 py-1.5 text-base text-text-soft transition hover:bg-hover"
                                    onClick={goBackToList}
                                >
                                    <Icon name="arrow_back" className="size-5" />
                                    {translate('mail.backToList')}
                                </button>
                            </div>
                        )}

                        {/* Everything the pane lays out takes the pane's own width. The ceiling a wide window needs
                            binds the words of a message rather than the column they sit in — `messageBody/Message.tsx`
                            carries it — because a head, a verdict, a row of attachment cards, and the field the reader
                            asks in are not text read line by line and have no measure to keep. */}
                        <div className="min-h-0 flex-1 overflow-y-auto">{mail}</div>

                        {intent}
                    </section>
                ) : null}
            </div>
        </div>
    );
}

// The mailbox column's contents, drawn once for the two places they stand: the column beside the list, and the drawer
// in front of it. The heading, the tree, the filters MailFathom's own reading will add, and the connection at the foot.
//
// Folded, it is the same tree at the rail's width rather than a column emptied of it: the point of folding is to give
// the width to the list while keeping the mailboxes one click away, and a rail with nothing in it would make the
// control the only thing left to click. What the rail does drop is the heading, which names a column a reader can see
// the whole of, and the connection line at the foot, which is a sentence and has nowhere to wrap to.
function Mailboxes({
    folders,
    status,
    folded,
    control,
}: {
    readonly folders: ReactNode;
    readonly status: ReactNode;
    readonly folded: boolean;
    readonly control: ReactNode;
}) {
    const { translate } = useLocalization();

    return (
        <>
            <div className={`flex items-center px-2.25 pt-3 pb-1.5 ${folded ? 'justify-end' : 'justify-between'}`}>
                {folded ? null : (
                    <p className="ps-1.5 text-xs tracking-widest text-muted uppercase">
                        {translate('mailboxes.heading')}
                    </p>
                )}

                {control}
            </div>

            <div className="flex min-h-0 flex-1 flex-col overflow-y-auto px-2.25 py-1">
                {folders}
                <AiFilters folded={folded} />
            </div>

            {folded ? null : <div className="border-t border-line px-3.5 py-2.5">{status}</div>}
        </>
    );
}
