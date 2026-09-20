// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useId, useState, type RefObject } from 'react';
import { Icon } from '../controls/Icon';
import { useLocalization } from '../localization/useLocalization';
import { useScreenLayer } from '../shell/screenLayers';
import {
    reminderLeadFrom,
    reminderPresetsFor,
    reminderUnits,
    withReminderLead,
    withoutReminderLead,
    type ReminderUnit,
} from './reminderLeads';
import { wordReminderCount, wordReminderLead } from './reminderWords';

// What announces an event, and the one place a person decides it. Every surface that sets a reminder — the event
// panel, the dialog an event is created in, and the task due date stage 10 attaches the same leads to — opens this
// rather than drawing a set of chips of its own, which is the whole reason it takes the leads and answers with them
// instead of knowing what it is attached to.
//
// **An event carrying none says so in as many words.** A panel with no chips pressed reads as a control nobody has
// touched; what the design project states instead is that nothing will be raised about this event, because that is a
// decision somebody made rather than a field they left.
//
// **The leads are the caller's state, and every act here answers with the whole set.** A panel that held its own copy
// would be a second answer to what the event carries, and the one thing a person is certain of on leaving it is what
// the chips said — so what they saw is what the caller writes.

export function ReminderPanel({
    panel,
    subject,
    allDay,
    reminders,
    onRemindersChanged,
}: {
    /**
     * The panel itself, held by the caller so that every way of opening it reaches the same one.
     *
     * The element is the state, exactly as it is for a confirmation: neither this component nor the screen above it
     * holds a second copy of whether the panel is open.
     */
    readonly panel: RefObject<HTMLDialogElement | null>;

    /** What is being reminded about, which is the event's own name. */
    readonly subject: string;

    /** Whether it is stated as a day rather than as a clock time, which decides both the presets and the anchor. */
    readonly allDay: boolean;

    /** The leads it is announced at, in minutes before it. */
    readonly reminders: readonly number[];

    /** Answers with the whole set after every act, which is what the caller writes. */
    readonly onRemindersChanged: (reminders: readonly number[]) => void;
}) {
    const { locale, translate } = useLocalization();
    const names = useId();
    const describes = useId();
    const countsLabel = useId();
    const [standing, setStanding] = useState(false);
    const [unit, setUnit] = useState<ReminderUnit>('minutes');
    const [typed, setTyped] = useState('');

    // The panel stands over whatever opened it, so the back gesture leaves it before reaching anything behind it,
    // which is what Escape already does on the same element.
    useScreenLayer(standing, () => {
        panel.current?.close();
    });

    function toggle(minutesBefore: number): void {
        onRemindersChanged(
            reminders.includes(minutesBefore)
                ? withoutReminderLead(reminders, minutesBefore)
                : withReminderLead(reminders, minutesBefore),
        );
    }

    function addTyped(): void {
        const stated = reminderLeadFrom(Number(typed), unit);

        if (stated === null) {
            return;
        }

        setTyped('');
        onRemindersChanged(withReminderLead(reminders, stated));
    }

    return (
        <dialog
            ref={panel}
            aria-labelledby={names}
            aria-describedby={describes}
            className="m-auto w-dialog max-w-dialog-narrow rounded-2xl border border-line bg-panel p-5 text-text shadow-dialog backdrop:bg-scrim"
            onToggle={(event) => {
                setStanding(event.newState === 'open');
            }}
        >
            <div className="flex flex-col gap-3.5">
                <div className="flex items-start gap-2.5">
                    <Icon name="notifications_active" className="mt-0.5 size-5 shrink-0 text-muted" />

                    <div className="flex min-w-0 flex-1 flex-col gap-0.5">
                        <h2 id={names} className="text-lg font-semibold">
                            {translate('reminders.title')}
                        </h2>

                        <p className="text-base text-muted text-pretty">
                            {translate('reminders.about', {
                                subject,
                                anchor: translate(allDay ? 'reminders.anchor.allDay' : 'reminders.anchor.timed'),
                            })}
                        </p>
                    </div>

                    <span id={countsLabel} className="shrink-0 rounded-full bg-hover px-2 py-0.5 text-sm text-muted">
                        {wordReminderCount(reminders.length, locale, translate)}
                    </span>
                </div>

                <ul className="flex flex-wrap gap-2">
                    {reminderPresetsFor(allDay).map((preset) => {
                        const set = reminders.includes(preset);

                        return (
                            <li key={preset}>
                                <button
                                    type="button"
                                    aria-pressed={set}
                                    className={`flex items-center gap-1.5 rounded-xl border px-3 py-1.5 text-base ${
                                        set
                                            ? 'border-accent bg-accent-soft font-semibold text-accent-strong'
                                            : 'border-line bg-panel text-text-soft'
                                    }`}
                                    onClick={() => {
                                        toggle(preset);
                                    }}
                                >
                                    <Icon name={set ? 'check' : 'add'} className="size-4 shrink-0" />
                                    {wordReminderLead(preset, allDay, locale, translate)}
                                </button>
                            </li>
                        );
                    })}
                </ul>

                <div className="flex flex-wrap items-center gap-2">
                    <input
                        type="number"
                        min={1}
                        inputMode="numeric"
                        aria-label={translate('reminders.custom.count')}
                        placeholder={translate('reminders.custom.hint')}
                        value={typed}
                        className="w-20 rounded-lg border border-line bg-panel px-2.5 py-1.5 text-base text-text"
                        onChange={(event) => {
                            setTyped(event.currentTarget.value);
                        }}
                    />

                    <div
                        role="group"
                        aria-label={translate('reminders.custom.unit')}
                        className="flex gap-0.5 rounded-xl border border-line bg-panel p-0.5"
                    >
                        {reminderUnits.map((offered) => (
                            <button
                                key={offered}
                                type="button"
                                aria-pressed={unit === offered}
                                className={`rounded-lg px-2.5 py-1 text-base ${
                                    unit === offered ? 'bg-accent font-semibold text-on-accent' : 'text-text-soft'
                                }`}
                                onClick={() => {
                                    setUnit(offered);
                                }}
                            >
                                {translate(`reminders.unit.${offered}`)}
                            </button>
                        ))}
                    </div>

                    <button
                        type="button"
                        className="rounded-xl border border-accent-line bg-accent-soft px-3 py-1.5 text-base font-semibold text-accent-strong"
                        onClick={addTyped}
                    >
                        {translate('reminders.custom.add')}
                    </button>
                </div>

                {reminders.length === 0 ? (
                    <p className="text-base text-muted text-pretty">{translate('reminders.none')}</p>
                ) : (
                    <ul className="flex flex-wrap gap-1.5 border-t border-line-soft pt-3">
                        {reminders.map((lead) => {
                            const said = wordReminderLead(lead, allDay, locale, translate);

                            return (
                                <li
                                    key={lead}
                                    className="flex items-center gap-1.5 rounded-full border border-accent-line bg-panel py-1 pr-1.5 pl-2.5 text-base"
                                >
                                    {said}

                                    <button
                                        type="button"
                                        aria-label={translate('reminders.remove', { lead: said })}
                                        className="text-faint"
                                        onClick={() => {
                                            onRemindersChanged(withoutReminderLead(reminders, lead));
                                        }}
                                    >
                                        <Icon name="close" className="size-4" />
                                    </button>
                                </li>
                            );
                        })}
                    </ul>
                )}

                <p id={describes} className="flex items-start gap-2 text-base text-muted text-pretty">
                    <Icon name="info" className="mt-0.5 size-4 shrink-0 text-faint" />
                    {translate('reminders.raises')}
                </p>

                <div className="flex flex-wrap items-center justify-between gap-2">
                    <button
                        type="button"
                        className="text-base text-text-soft underline"
                        onClick={() => {
                            onRemindersChanged([]);
                        }}
                    >
                        {translate('reminders.turnAllOff')}
                    </button>

                    <button
                        type="button"
                        className="rounded-lg bg-accent px-4 py-2 text-base font-semibold text-on-accent"
                        onClick={() => {
                            panel.current?.close();
                        }}
                    >
                        {translate('reminders.done')}
                    </button>
                </div>
            </div>
        </dialog>
    );
}
