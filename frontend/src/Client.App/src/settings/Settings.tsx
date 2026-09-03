// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useEffect, useId, useRef, useState } from 'react';
import { Icon } from '../controls/Icon';
import { PersonAvatar } from '../controls/PersonAvatar';
import { Switch } from '../controls/Switch';
import { useLocalization } from '../localization/useLocalization';
import type { ClientPreferencesInForce } from '../preferences/useClientPreferences';
import type { OwnProfileInForce } from '../profile/useOwnProfile';
import { LanguageSegments } from '../shell/Preferences';
import { chosenPortrait, type PortraitChoice } from './chosenPortrait';

// The screen behind the account menu's own row, and the one place the client edits the person rather than the mail.
// The design project draws it as a modal panel of a fixed width holding three sections: who they are, what the client
// reads in, and what may be said about them.
//
// It is a `dialog` opened as a modal rather than a panel drawn to look like one, because everything the acceptance
// asks of it is what that element already does: it takes focus, it keeps focus, it closes on Escape, it puts the page
// behind it out of reach, and closing it hands focus back to whatever opened it. A hand-written trap would be a second
// implementation of all five, and the one that gets them wrong.
//
// One section the project draws is missing, and deliberately: the choice between the simplified and the HTML view of a
// message edits a preference this deployment does not hold, so drawing it would be a switch that decides nothing.
// It is #1508.

export function Settings({
    open,
    profile,
    preferences,
    onClose,
}: {
    readonly open: boolean;

    /** Who the client is drawing, and the three ways this screen changes it. */
    readonly profile: OwnProfileInForce;

    /** The settings that follow the person, of which this screen edits one. */
    readonly preferences: ClientPreferencesInForce;

    readonly onClose: () => void;
}) {
    const { translate } = useLocalization();
    const panel = useRef<HTMLDialogElement>(null);
    const named = useId();

    // The one imperative browser API this screen synchronizes with, which is the whole of what an effect is for. The
    // element's own state is asked rather than assumed, because `close` fires for Escape as well as for the control
    // and the answer above would otherwise be told to close something already closed.
    useEffect(() => {
        const element = panel.current;

        if (element === null) {
            return;
        }

        if (open && !element.open) {
            element.showModal();
        }

        if (!open && element.open) {
            element.close();
        }
    }, [open]);

    return (
        <dialog
            ref={panel}
            aria-labelledby={named}
            onClose={onClose}
            className="m-auto w-95 max-w-full rounded-3xl border border-line bg-panel p-0 text-base text-text shadow-dialog backdrop:bg-scrim"
        >
            <div className="flex items-center gap-3 border-b border-line bg-sunken px-3.5 py-2.5">
                <h2 id={named} className="text-md font-semibold">
                    {translate('settings.title')}
                </h2>

                <button
                    type="button"
                    aria-label={translate('settings.close')}
                    className="ms-auto flex size-6.5 items-center justify-center rounded-md text-muted transition hover:bg-hover hover:text-text"
                    onClick={onClose}
                >
                    <Icon name="close" className="size-4.25" />
                </button>
            </div>

            <div className="flex flex-col gap-2.25 overflow-y-auto px-3.5 py-3">
                <Profile profile={profile} />

                <Divider />

                <SectionName>{translate('shell.language')}</SectionName>
                <LanguageSegments />

                <Divider />

                <SectionName>{translate('settings.privacy')}</SectionName>
                <Telemetry preferences={preferences} />
            </div>
        </dialog>
    );
}

function Divider() {
    return <div className="my-1 h-px bg-line-soft" />;
}

function SectionName({ children }: { readonly children: string }) {
    return <p className="text-2xs tracking-widest text-faint uppercase">{children}</p>;
}

// What the deployment records the person as, and the picture they are drawn by. The name is corrected in place rather
// than through a form with a button: there is one field, and a person who has typed their name and moved on has said
// what they mean as plainly as one who pressed Save.
function Profile({ profile }: { readonly profile: OwnProfileInForce }) {
    const { translate } = useLocalization();

    // What a file the person picked was refused for, which belongs to the control they picked it with rather than to
    // the profile: it is decided here and never sent, so nothing outside this screen has anything to do about it.
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
            <SectionName>{translate('settings.profile')}</SectionName>

            <div className="flex items-center gap-2.75">
                <PersonAvatar displayName={profile.displayName} picture={profile.picture} place="profile" />

                <div className="flex min-w-0 flex-1 flex-col gap-1.25">
                    <label htmlFor={named} className="sr-only">
                        {translate('settings.name')}
                    </label>

                    {/* Uncontrolled, and remounted by the name the deployment holds. What somebody is typing is not
                        state this screen has any use for, and a second copy of it beside the stored name is the pair
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

                    {profile.changeable ? (
                        <div className="flex flex-wrap items-center gap-1.25">
                            <label className="flex cursor-pointer items-center gap-1.25 rounded-md border border-line-strong px-2.25 py-1 text-xs text-text-soft transition hover:bg-hover has-[:focus-visible]:outline-2 has-[:focus-visible]:outline-offset-2 has-[:focus-visible]:outline-accent">
                                <Icon name="add_a_photo" className="size-3.5" />
                                {translate('settings.choosePicture')}

                                {/* The file input carries the label rather than being clicked by a handler: a hidden
                                    input a control activates imperatively is one a keyboard reaches only by accident,
                                    and a label already names it and opens it. */}
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
                    ) : null}

                    <PictureNotice profile={profile} refused={refused} />
                </div>
            </div>

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
function Telemetry({ preferences }: { readonly preferences: ClientPreferencesInForce }) {
    const { translate } = useLocalization();
    const withheld = !preferences.telemetryEnabled;

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
