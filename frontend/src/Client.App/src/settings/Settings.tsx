// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useEffect, useId, useRef, useState } from 'react';
import { Icon } from '../controls/Icon';
import { PersonAvatar } from '../controls/PersonAvatar';
import { Switch } from '../controls/Switch';
import { VersionLine } from '../controls/VersionLine';
import type { TelemetryForwarding } from '../deployment/telemetryForwarding';
import type { MessageKey } from '../localization/en';
import { useLocalization } from '../localization/useLocalization';
import type { ClientPreferencesInForce } from '../preferences/useClientPreferences';
import type { OwnProfileInForce } from '../profile/useOwnProfile';
import { LanguageSegments } from '../shell/Preferences';
import { chosenPortrait, type PortraitChoice } from './chosenPortrait';

// The surface behind the account menu's own row, and the one place the client edits the person rather than the mail.
//
// One surface in two compositions, chosen by width exactly as every other pane in this client is: over the workspace
// breakpoint the design project draws it as a card of a stated width and height over a scrim, and below it as the whole
// screen with a larger head and a taller body. Nothing here branches on that. The difference is entirely what a card
// looks like against what a screen looks like, so it is written as the width variant on the element itself — a
// component asking which composition it is in would be the same defect as one asking which head it is running on.
//
// It is a `dialog` opened as a modal rather than a panel drawn to look like one, in both compositions, because
// everything the acceptance asks of it is what that element already does: it takes focus, it keeps focus, it closes on
// Escape, it puts the page behind it out of reach, and closing it hands focus back to whatever opened it. A
// hand-written trap would be a second implementation of all five, and the one that gets them wrong.
//
// It is mounted while it is open and unmounted when it closes, which is what makes "the tab last open is remembered
// nowhere" a property of the tree rather than a reset somebody has to remember to write.
//
// One control the project draws is missing, and deliberately: the choice between the simplified and the HTML view of a
// message edits a preference this deployment does not hold, so drawing it would be a switch that decides nothing. It is
// #1508, and the section heading it belongs under is here because the thread-expansion switch stands under it too.

/** Which half of the surface is being read, in the order the design project puts the two tabs in. */
const tabs = ['profile', 'application'] as const;

type SettingsTab = (typeof tabs)[number];

const tabNames: Readonly<Record<SettingsTab, MessageKey>> = {
    profile: 'settings.profile',
    application: 'settings.application',
};

export function Settings({
    profile,
    preferences,
    telemetryForwarding,
    deploymentVersion,
    onClose,
}: {
    /** Who the client is drawing, and the three ways this surface changes it. */
    readonly profile: OwnProfileInForce;

    /** The settings that follow the person, of which this surface edits two. */
    readonly preferences: ClientPreferencesInForce;

    /** What this deployment has said about forwarding this client's telemetry, including that it has said nothing. */
    readonly telemetryForwarding: TelemetryForwarding;

    /** What the deployment answered it is running, or `null` while nothing has answered, for the line at the foot. */
    readonly deploymentVersion: string | null;

    readonly onClose: () => void;
}) {
    const { translate } = useLocalization();
    const panel = useRef<HTMLDialogElement>(null);
    const named = useId();
    const tabId = useId();
    const [open, setOpen] = useState<SettingsTab>('profile');

    // The one imperative browser API this surface synchronizes with, which is the whole of what an effect is for. It
    // runs once, because the element is in the document exactly as long as the surface is open.
    useEffect(() => {
        panel.current?.showModal();
    }, []);

    return (
        <dialog
            ref={panel}
            aria-labelledby={named}
            onClose={onClose}
            className="m-0 h-full max-h-full w-full max-w-full rounded-none border-0 bg-panel p-0 text-base text-text backdrop:bg-scrim open:flex open:flex-col workspace:m-auto workspace:h-settings-card workspace:w-settings workspace:rounded-3xl workspace:border workspace:border-line workspace:shadow-dialog"
        >
            <div className="flex items-center gap-3 border-b border-line bg-sunken px-3.5 pt-3.5 pb-3 workspace:pt-2.5 workspace:pb-2.5">
                <h2 id={named} className="text-2xl font-semibold workspace:text-md">
                    {translate('settings.title')}
                </h2>

                {/* Closed through the element rather than by answering upwards, so that the native `close` event is
                    the one path out and `onClose` is called once however the surface was left. */}
                <button
                    type="button"
                    aria-label={translate('settings.close')}
                    className="ms-auto flex size-6.5 items-center justify-center rounded-md text-muted transition hover:bg-hover hover:text-text"
                    onClick={() => {
                        panel.current?.close();
                    }}
                >
                    <Icon name="close" className="size-4.25" />
                </button>
            </div>

            <SettingsTabs id={tabId} open={open} onOpen={setOpen} />

            <div
                role="tabpanel"
                id={`${tabId}-panel`}
                aria-labelledby={`${tabId}-${open}`}
                className="flex min-h-0 flex-1 flex-col gap-2.75 overflow-y-auto px-4 pt-3.5 pb-7 workspace:gap-2.25 workspace:pb-5"
            >
                {open === 'profile' ? (
                    <Profile profile={profile} />
                ) : (
                    <Application preferences={preferences} telemetryForwarding={telemetryForwarding} />
                )}
            </div>

            {/* Outside the scrolling panel and under both tabs, which is where the design project draws it: what is
                running is about the client rather than about whichever half of this surface is open, and a line that
                scrolled away with the profile would be one somebody has to go looking for. */}
            <VersionLine
                deploymentVersion={deploymentVersion}
                className="border-t border-line bg-sunken px-4 py-2.5 text-center text-2xs text-faint"
            />
        </dialog>
    );
}

/**
 * The two halves of the surface as a tab list, which is what the design project draws and what a screen reader is owed.
 *
 * Buttons carrying the tab role rather than radio inputs, because a tab list has its own keyboard contract: one tab
 * stop for the whole list, the arrow keys moving between the tabs inside it, and Home and End reaching the ends. That
 * is what the roving `tabIndex` below is — the selected tab is the one the Tab key lands on, and the arrows move both
 * the selection and the focus, which is the pattern for tabs whose panel follows the selection.
 */
function SettingsTabs({
    id,
    open,
    onOpen,
}: {
    readonly id: string;
    readonly open: SettingsTab;
    readonly onOpen: (tab: SettingsTab) => void;
}) {
    const { translate } = useLocalization();

    // The two tabs themselves, so a key press can put focus on the one it selected. Held by identity rather than found
    // by a selector, because `useId` composes identifiers a CSS selector would have to be escaped for.
    const buttons = useRef(new Map<SettingsTab, HTMLButtonElement>());

    function move(to: SettingsTab): void {
        onOpen(to);
        buttons.current.get(to)?.focus();
    }

    return (
        <div
            role="tablist"
            aria-label={translate('settings.sections')}
            className="flex items-stretch gap-3.5 border-b border-line bg-sunken px-3.5"
            onKeyDown={(event) => {
                const stepped = steppedTo(event.key);

                if (stepped !== null) {
                    event.preventDefault();
                    move(stepped);
                }
            }}
        >
            {tabs.map((offered) => (
                <button
                    key={offered}
                    ref={(element) => {
                        if (element === null) {
                            buttons.current.delete(offered);
                        } else {
                            buttons.current.set(offered, element);
                        }
                    }}
                    id={`${id}-${offered}`}
                    type="button"
                    role="tab"
                    aria-selected={offered === open}
                    aria-controls={`${id}-panel`}
                    tabIndex={offered === open ? 0 : -1}
                    className={`border-b-2 px-px pt-2.5 pb-2 text-sm whitespace-nowrap transition ${
                        offered === open
                            ? 'border-accent font-semibold text-text'
                            : 'border-transparent text-muted hover:text-text'
                    }`}
                    onClick={() => {
                        onOpen(offered);
                    }}
                >
                    {translate(tabNames[offered])}
                </button>
            ))}
        </div>
    );
}

/**
 * Which tab a key press moves to, or `null` where the press is not one this list answers.
 *
 * The list does not wrap at its ends, which is what makes Home and End the same answers as the two arrows while there
 * are two tabs: a list of two that wrapped would make the left and right arrows one gesture, and holding either down
 * would oscillate the selection under the reader.
 */
function steppedTo(key: string): SettingsTab | null {
    switch (key) {
        case 'ArrowLeft':
        case 'Home':
            return 'profile';
        case 'ArrowRight':
        case 'End':
            return 'application';
        default:
            return null;
    }
}

function Divider() {
    return <div className="my-1 h-px bg-line-soft" />;
}

function SectionName({ children }: { readonly children: string }) {
    return <p className="text-2xs tracking-widest text-faint uppercase">{children}</p>;
}

// What the client is read in and how it draws a conversation, in the design project's own order: the language, then
// the message view, then the privacy section carrying the telemetry decision.
function Application({
    preferences,
    telemetryForwarding,
}: {
    readonly preferences: ClientPreferencesInForce;
    readonly telemetryForwarding: TelemetryForwarding;
}) {
    const { translate } = useLocalization();

    return (
        <>
            <SectionName>{translate('shell.language')}</SectionName>
            <LanguageSegments />

            <Divider />

            <SectionName>{translate('settings.messageView')}</SectionName>
            <ThreadExpansion preferences={preferences} />

            <Divider />

            <SectionName>{translate('settings.privacy')}</SectionName>
            <Telemetry preferences={preferences} forwarding={telemetryForwarding} />
        </>
    );
}

// Whether a conversation opens with every message drawn. Stated as the choice rather than as its absence, which is the
// way round the design project draws it: the switch being on is the conversation opening expanded, and the line under
// it says what the client does without it — which is what it does today and what an unset preference reads as.
function ThreadExpansion({ preferences }: { readonly preferences: ClientPreferencesInForce }) {
    const { translate } = useLocalization();

    return (
        <label className="flex cursor-pointer items-start gap-2.75 rounded-xl border border-line bg-sunken px-2.5 py-2.25 transition hover:bg-hover">
            <span className="flex min-w-0 flex-1 flex-col gap-0.75">
                {translate('settings.expandWholeThread')}
                <span className="text-xs text-muted">{translate('settings.expandWholeThreadExplanation')}</span>
            </span>

            <Switch on={preferences.expandWholeThread} onChange={preferences.chooseThreadExpansion} />
        </label>
    );
}

// What the deployment records the person as, and the picture they are drawn by. The name is corrected in place rather
// than through a form with a button: there is one field, and a person who has typed their name and moved on has said
// what they mean as plainly as one who pressed Save.
function Profile({ profile }: { readonly profile: OwnProfileInForce }) {
    const { translate } = useLocalization();

    // What a file the person picked was refused for, which belongs to the control they picked it with rather than to
    // the profile: it is decided here and never sent, so nothing outside this surface has anything to do about it.
    const [refused, setRefused] = useState<PortraitChoice | null>(null);
    const named = useId();

    function pick(picture: File | undefined): void {
        if (picture === undefined) {
            return;
        }

        const choice = chosenPortrait(picture.type, picture.size);

        setRefused(choice.outcome === 'admissible' ? null : choice);

        if (choice.outcome === 'admissible') {
            profile.choosePicture(picture, choice.type);
        }
    }

    return (
        <>
            <div className="flex items-center gap-2.75">
                <PersonAvatar displayName={profile.displayName} picture={profile.picture} place="profile" />

                <div className="flex min-w-0 flex-1 flex-col gap-1.25">
                    <label htmlFor={named} className="sr-only">
                        {translate('settings.name')}
                    </label>

                    {/* Uncontrolled, and remounted by the name the deployment holds. What somebody is typing is not
                        state this surface has any use for, and a second copy of it beside the stored name is the pair
                        that comes to disagree; keying on the stored name is what redraws the field when an answer
                        arrives without discarding a correction that has not been sent yet. */}
                    <input
                        key={profile.displayName ?? ''}
                        id={named}
                        type="text"
                        defaultValue={profile.displayName ?? ''}
                        readOnly={!profile.changeable}
                        placeholder={translate('settings.name')}
                        className="w-full rounded-lg border border-line-strong bg-sunken px-2.5 py-1.5 text-base text-text outline-none read-only:text-muted focus:border-accent"
                        onBlur={(event) => {
                            correct(profile, event.target.value);
                        }}
                        onKeyDown={(event) => {
                            if (event.key === 'Enter') {
                                correct(profile, event.currentTarget.value);
                            }
                        }}
                    />

                    {/* Drawn whatever the field beside them is doing. `changeable` says whether *the name* would be
                        taken, which is a grant over somebody's mail configuration; the portrait routes ask only for
                        the grant to read mail, deliberately and for the reason the endpoint states — what a person is
                        drawn by is not decided by who maintains their mailboxes. Gating these on it would refuse a
                        change the deployment would have accepted. */}
                    <div className="flex flex-wrap items-center gap-1.25">
                        <label className="flex cursor-pointer items-center gap-1.25 rounded-md border border-line-strong px-2.25 py-1 text-xs text-text-soft transition hover:bg-hover has-[:focus-visible]:outline-2 has-[:focus-visible]:outline-offset-2 has-[:focus-visible]:outline-accent">
                            <Icon name="add_a_photo" className="size-3.5" />
                            {translate('settings.choosePicture')}

                            {/* The file input carries the label rather than being clicked by a handler: a hidden input
                                a control activates imperatively is one a keyboard reaches only by accident, and a
                                label already names it and opens it. */}
                            <input
                                type="file"
                                accept="image/jpeg,image/png"
                                className="sr-only"
                                onChange={(event) => {
                                    pick(event.target.files?.[0]);

                                    // Cleared so that picking the same file twice is a second choice rather than
                                    // nothing at all, which is what a person correcting a refusal does.
                                    event.target.value = '';
                                }}
                            />
                        </label>

                        {profile.picture === null ? null : (
                            <button
                                type="button"
                                className="flex items-center gap-1 rounded-md px-2 py-1 text-xs text-muted transition hover:bg-hover hover:text-text"
                                onClick={() => {
                                    setRefused(null);
                                    profile.removePicture();
                                }}
                            >
                                <Icon name="delete" className="size-3.5" />
                                {translate('settings.removePicture')}
                            </button>
                        )}

                        <span className="text-2xs text-faint">{translate('settings.pictureBounds')}</span>
                    </div>

                    <PictureNotice profile={profile} refused={refused} />
                </div>
            </div>

            <p className="text-2xs text-muted">{translate('settings.profileHeld')}</p>

            {profile.changeable ? null : <p className="text-2xs text-muted">{translate('settings.nameNotYours')}</p>}

            {profile.nameNotAcceptable ? (
                <p className="text-2xs text-warning">{translate('settings.nameNotAcceptable')}</p>
            ) : null}

            {profile.nameNotStated ? (
                <p className="text-2xs text-warning">{translate('settings.nameNotStored')}</p>
            ) : null}
        </>
    );
}

// What went wrong with the picture, which is one line whether this client refused the file or the deployment refused
// the request. The client's own refusal is said first: it is about the file in front of the person.
function PictureNotice({
    profile,
    refused,
}: {
    readonly profile: OwnProfileInForce;
    readonly refused: PortraitChoice | null;
}) {
    const { translate } = useLocalization();

    if (refused !== null && refused.outcome !== 'admissible') {
        return (
            <p className="text-2xs text-warning">
                {translate(
                    refused.outcome === 'notAnImageKind'
                        ? 'settings.pictureNotAnImageKind'
                        : 'settings.pictureTooLarge',
                )}
            </p>
        );
    }

    return profile.pictureNotStated ? (
        <p className="text-2xs text-warning">{translate('settings.pictureNotStored')}</p>
    ) : null;
}

// Whether this deployment may be told what the client is doing, drawn the way the design project states it: as the
// decision to withhold rather than the decision to permit, so that the switch being on is the private answer.
//
// Three things stand beside the switch that the design project does not draw, all of them the acceptance of #1232 and
// all about the same thing — that a decision is only a decision if what is being decided is stated. Where the records
// go is named, because "telemetry" says nothing about who ends up holding it, and this client sends to the deployment
// somebody signed in to rather than anywhere else. A deployment that forwards none is said out loud instead of being
// drawn as a switch: there is nothing behind it there, so moving it would decide nothing. And a deployment that has
// not answered yet says that instead of either, because the frame records under the person's own answer while it
// waits — so drawing "nothing to turn off" over that state would be telling somebody nothing is being sent at exactly
// the moment something is.
function Telemetry({
    preferences,
    forwarding,
}: {
    readonly preferences: ClientPreferencesInForce;
    readonly forwarding: TelemetryForwarding;
}) {
    const { translate } = useLocalization();
    const withheld = !preferences.telemetryEnabled;

    if (!forwarding.answered) {
        return (
            <p className="rounded-xl border border-line bg-sunken px-2.5 py-2.25 text-xs text-muted">
                {translate('settings.telemetryUnanswered')}
            </p>
        );
    }

    const { destination } = forwarding;

    if (destination === null) {
        return (
            <p className="rounded-xl border border-line bg-sunken px-2.5 py-2.25 text-xs text-muted">
                {translate('settings.telemetryNotForwarded')}
            </p>
        );
    }

    return (
        <>
            <label className="flex cursor-pointer items-start gap-2.75 rounded-xl border border-line bg-sunken px-2.5 py-2.25 transition hover:bg-hover">
                <span className="flex min-w-0 flex-1 flex-col gap-0.75">
                    {translate('settings.telemetryWithheld')}
                    <span className="text-xs text-muted">{translate('settings.telemetryExplanation')}</span>
                </span>

                <Switch
                    on={withheld}
                    onChange={(chosen) => {
                        preferences.chooseTelemetry(!chosen);
                    }}
                />
            </label>

            <p className="text-2xs text-faint">
                {translate('settings.telemetryDestination', { address: destination })}
            </p>

            {withheld ? (
                <p className="flex items-start gap-2 rounded-xl border border-warning bg-warning-soft px-2.5 py-2.25 text-xs text-warning-text">
                    <Icon name="info" className="size-4" />
                    {translate('settings.telemetryWithheldWarning')}
                </p>
            ) : null}
        </>
    );
}

// A correction is sent where it says something different from what the deployment holds. Anything else would be a
// write per field somebody tabbed through, and the deployment would answer the name it already had.
function correct(profile: OwnProfileInForce, typed: string): void {
    if (profile.changeable && typed.trim() !== (profile.displayName ?? '')) {
        profile.correctName(typed);
    }
}
