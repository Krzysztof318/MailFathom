// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useEffect, useRef } from 'react';
import type { DayLayout, PersonalTask } from '@mailfathom/client-backend';
import { Control } from '../controls/Control';
import { SecondaryButton } from '../controls/SecondaryButton';
import type { MessageKey } from '../localization/en';
import { useLocalization } from '../localization/useLocalization';

// How spoken-for the day is, and the offer to arrange what is left of it — the design project's second sidebar panel.
//
// **What the panel says about the day is counted rather than composed.** The design writes a sentence a model would
// have written, ending in a judgement about which task to start with; this deployment publishes no route that answers
// that, so what is drawn is the two numbers the screen already holds — how much is due today and how much of the day
// is already promised. The *AI* mark stays, because the act the panel carries is the arranged day and that is composed
// by a provider.
//
// **Nothing is applied until somebody accepts it.** The arrangement comes back as an offer: the placements are drawn
// with the times they suggest, what does not fit the day is named, and the calendar is written only by the control
// that says so. That is the acceptance this panel exists to satisfy, and it is why the offer can also be put down
// again without writing anything.
//
// **Each number is its own sentence rather than a hole in a shared one.** The two counts inflect the noun after them
// differently in Polish and would otherwise need a form per pair, which is a table of sixteen entries saying one thing.
//
// **The act is absent rather than inert where it would do nothing.** A deployment that arranges no day, and a
// credential that may not ask, each meet a panel that counts the day and offers nothing — which is what the
// capability route exists to make answerable before the control is drawn.

// How much is due today, in the forms a language has for the noun, and the same for how much the calendar already
// holds. `Intl.PluralRules` selects between them, which Polish needs and English hides.
const tasksCounted: Readonly<Record<Intl.LDMLPluralRule, MessageKey>> = {
    zero: 'tasks.capacityTasks.other',
    one: 'tasks.capacityTasks.one',
    two: 'tasks.capacityTasks.other',
    few: 'tasks.capacityTasks.few',
    many: 'tasks.capacityTasks.many',
    other: 'tasks.capacityTasks.other',
};

const eventsCounted: Readonly<Record<Intl.LDMLPluralRule, MessageKey>> = {
    zero: 'tasks.capacityEvents.other',
    one: 'tasks.capacityEvents.one',
    two: 'tasks.capacityEvents.other',
    few: 'tasks.capacityEvents.few',
    many: 'tasks.capacityEvents.many',
    other: 'tasks.capacityEvents.other',
};

const notTodayCounted: Readonly<Record<Intl.LDMLPluralRule, MessageKey>> = {
    zero: 'tasks.notToday.other',
    one: 'tasks.notToday.one',
    two: 'tasks.notToday.other',
    few: 'tasks.notToday.few',
    many: 'tasks.notToday.many',
    other: 'tasks.notToday.other',
};

export function DayCapacity({
    dueToday,
    eventsToday,
    offered,
    arranging,
    layout,
    placed,
    applying,
    onArrange,
    onApply,
    onPutDown,
}: {
    /** The tasks due today and not done, which is what the day is counted against. */
    readonly dueToday: number;

    /** How much of the day the calendar already holds. */
    readonly eventsToday: number;

    /** Whether this deployment arranges a day at all and this credential may ask it to. */
    readonly offered: boolean;

    /** Whether an arrangement is on its way, which is the wait the panel says it is in. */
    readonly arranging: boolean;

    /** The arrangement being offered, or `null` where none has been asked for or one was put down. */
    readonly layout: DayLayout | null;

    /** The tasks the arrangement names, so a placement can be drawn with the line it is about. */
    readonly placed: readonly PersonalTask[];

    /** Whether the offer is being written to the calendar. */
    readonly applying: boolean;

    readonly onArrange: () => void;
    readonly onApply: () => void;

    /** Puts the offer down without writing any of it, which is the way out of a state a person did not want. */
    readonly onPutDown: () => void;
}) {
    const { locale, translate } = useLocalization();

    // The control that asks for an arrangement is removed while the deployment composes one, and it is the control
    // that was pressed — so focus would fall to the document body and somebody reading with a keyboard or a screen
    // reader would be left nowhere while they wait. The sentence that replaces it is what they are waiting for.
    const arrangingSaid = useRef<HTMLParagraphElement | null>(null);

    useEffect(() => {
        if (arranging) {
            arrangingSaid.current?.focus();
        }
    }, [arranging]);

    const counted = new Intl.NumberFormat(locale);
    const forms = new Intl.PluralRules(locale);
    const clock = new Intl.DateTimeFormat(locale, { hour: '2-digit', minute: '2-digit' });
    const titles = new Map(placed.map((task) => [task.id, task.title]));

    return (
        <section
            aria-labelledby="tasks-day-capacity"
            className="flex flex-col gap-2.25 rounded-xl bg-accent-soft p-3.25"
        >
            <div className="flex items-center gap-2.25">
                <span className="rounded-sm bg-accent px-1.5 py-0.5 text-2xs font-semibold text-on-accent">
                    {translate('tasks.authored')}
                </span>
                <h2 id="tasks-day-capacity" className="text-xs tracking-widest text-accent-deep uppercase">
                    {translate('tasks.dayCapacity')}
                </h2>
            </div>

            <p className="text-sm text-text-soft text-pretty">
                {translate(tasksCounted[forms.select(dueToday)], { count: counted.format(dueToday) })}{' '}
                {translate(eventsCounted[forms.select(eventsToday)], { count: counted.format(eventsToday) })}
            </p>

            {arranging ? (
                <p ref={arrangingSaid} tabIndex={-1} role="status" className="text-sm text-accent-deep">
                    {translate('tasks.arranging')}
                </p>
            ) : null}

            {layout === null || arranging ? null : layout.outcome === 'AllowanceSpent' ? (
                <p role="status" className="text-sm text-warning text-pretty">
                    {translate('tasks.allowanceSpent')}
                </p>
            ) : layout.outcome === 'NotArranged' || layout.placements.length === 0 ? (
                <p role="status" className="text-sm text-text-soft text-pretty">
                    {translate('tasks.notArranged')}
                </p>
            ) : (
                <div className="flex flex-col gap-2.25">
                    <ul className="flex flex-col gap-1.5">
                        {layout.placements.map((placement) => (
                            <li
                                key={placement.taskId}
                                className="flex items-baseline gap-2.5 rounded-lg bg-panel px-2.75 py-2"
                            >
                                <time dateTime={placement.startAt} className="w-13 shrink-0 text-xs text-muted">
                                    {clock.format(new Date(placement.startAt))}
                                </time>
                                <span className="min-w-0 flex-1 text-sm text-pretty">
                                    {titles.get(placement.taskId) ?? translate('tasks.unnamedPlacement')}
                                </span>
                            </li>
                        ))}
                    </ul>

                    {layout.notToday.length === 0 ? null : (
                        <p className="text-sm text-text-soft text-pretty">
                            {translate(notTodayCounted[forms.select(layout.notToday.length)], {
                                count: counted.format(layout.notToday.length),
                            })}
                        </p>
                    )}

                    <div className="flex flex-wrap items-center gap-2">
                        <Control
                            label={translate(applying ? 'tasks.addingToCalendar' : 'tasks.addToCalendar')}
                            shape="primary"
                            onPress={onApply}
                        />
                        <SecondaryButton label={translate('tasks.putTheOfferDown')} onActivate={onPutDown} />
                    </div>
                </div>
            )}

            {!offered || layout !== null || arranging ? null : (
                <Control
                    label={translate('tasks.layOutToday')}
                    shape="primary"
                    className="self-start"
                    onPress={onArrange}
                />
            )}
        </section>
    );
}
