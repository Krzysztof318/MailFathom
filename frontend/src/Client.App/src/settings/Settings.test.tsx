// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { act, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { largestPortraitOctets } from '@mailfathom/client-backend';
import type { TelemetryForwarding } from '../deployment/telemetryForwarding';
import { LocalizationProvider } from '../localization/Localization';
import { chooseSystemNotifications } from '../preferences/systemNotifications';
import type { ClientPreferencesInForce } from '../preferences/useClientPreferences';
import type { OwnProfileInForce } from '../profile/useOwnProfile';
import { deviceKeys } from '../device/deviceStore';
import { SystemNotifierContext, type SystemNotifier } from '../shellOperations/systemNotifier';
import { Settings } from './Settings';

const settings: ClientPreferencesInForce = {
    openMailInTabs: false,
    markReadOnOpen: true,
    telemetryEnabled: true,
    expandWholeThread: false,
    embeddedHtmlMessages: false,
    notStated: false,
    chooseTheme: () => undefined,
    chooseTabMode: () => undefined,
    chooseTelemetry: () => undefined,
    chooseThreadExpansion: () => undefined,
    chooseMessageView: () => undefined,
};

const named: OwnProfileInForce = {
    displayName: 'Ada Lovelace',
    changeable: true,
    picture: null,
    nameNotAcceptable: false,
    nameNotStated: false,
    pictureNotStated: false,
    correctName: () => undefined,
    choosePicture: () => undefined,
    removePicture: () => undefined,
};

/** The ordinary case: a deployment that answered, and forwards what this client records to itself. */
const forwardedTo: TelemetryForwarding = { answered: true, destination: 'https://mail.example' };

function renderSettings({
    profile = named,
    preferences = settings,
    telemetryForwarding = forwardedTo,
    deploymentVersion = '0.9.0',
    onClose = () => undefined,
    head = raisesNothing,
}: {
    readonly profile?: OwnProfileInForce;
    readonly preferences?: ClientPreferencesInForce;
    readonly telemetryForwarding?: TelemetryForwarding;
    readonly deploymentVersion?: string | null;
    readonly onClose?: () => void;

    /** The head this surface is drawn in, which decides whether it offers a system notification at all. */
    readonly head?: SystemNotifier;
} = {}): void {
    render(
        <LocalizationProvider>
            <SystemNotifierContext value={head}>
                <Settings
                    profile={profile}
                    preferences={preferences}
                    telemetryForwarding={telemetryForwarding}
                    deploymentVersion={deploymentVersion}
                    onClose={onClose}
                />
            </SystemNotifierContext>
        </LocalizationProvider>,
    );
}

/** The web head, where nothing offered the operation and the row therefore has nothing to decide. */
const raisesNothing: SystemNotifier = { offered: false, raise: () => Promise.resolve('unavailable') };

/** A head that offered it, which is the desktop one. */
const raisesThem: SystemNotifier = { offered: true, raise: () => Promise.resolve('raised') };

/** The second tab opened, which is where everything about the client rather than about the person is. */
function openApplication(): void {
    fireEvent.click(screen.getByRole('tab', { name: 'Application' }));
}

/** One file of a stated kind and size, which is the whole of what the client judges a chosen picture by. */
function file(name: string, type: string, octets: number): File {
    return new File([new Uint8Array(octets)], name, { type });
}

function choose(picture: File): void {
    fireEvent.change(screen.getByLabelText('Picture'), { target: { files: [picture] } });
}

// The one value this surface writes outside React is the machine's own answer about system notifications, so it is
// taken back between tests rather than carried into the next one as a decision somebody made.
afterEach(() => {
    window.localStorage.removeItem(deviceKeys.systemNotifications);
});

describe('Settings', () => {
    it('is a dialog named for what it holds', () => {
        renderSettings();

        expect(screen.getByRole('dialog', { name: 'Settings' })).toBeDefined();
    });

    it('draws the name this deployment records the person under', () => {
        renderSettings();

        expect(screen.getByRole('textbox', { name: 'Full name' })).toHaveProperty('value', 'Ada Lovelace');
    });

    it('sends a corrected name once the person has moved on from the field', () => {
        const correctName = vi.fn();
        renderSettings({ profile: { ...named, correctName } });

        const field = screen.getByRole('textbox', { name: 'Full name' });
        fireEvent.change(field, { target: { value: 'Grace Hopper' } });
        fireEvent.blur(field);

        expect(correctName).toHaveBeenCalledWith('Grace Hopper');
    });

    it('sends nothing where the field was left holding the name it was given', () => {
        const correctName = vi.fn();
        renderSettings({ profile: { ...named, correctName } });

        fireEvent.blur(screen.getByRole('textbox', { name: 'Full name' }));

        expect(correctName).not.toHaveBeenCalled();
    });

    it('draws the name as read-only, with the reason, where this deployment will not take a change of it', () => {
        renderSettings({ profile: { ...named, changeable: false } });

        expect(screen.getByRole('textbox', { name: 'Full name' })).toHaveProperty('readOnly', true);
        expect(screen.getByText(/keeps your name/u)).toBeDefined();
    });

    it('still offers the picture, which this deployment grants on reading mail rather than on the name', () => {
        const choosePicture = vi.fn();
        renderSettings({
            profile: { ...named, changeable: false, picture: 'data:image/png;base64,AA==', choosePicture },
        });

        const picture = file('portrait.png', 'image/png', 64);
        choose(picture);

        expect(choosePicture).toHaveBeenCalledWith(picture, 'image/png');
        expect(screen.getByRole('button', { name: 'Remove' })).toBeDefined();
    });

    it('sends nothing from a read-only field, whatever a person manages to leave in it', () => {
        const correctName = vi.fn();
        renderSettings({ profile: { ...named, changeable: false, correctName } });

        const field = screen.getByRole('textbox', { name: 'Full name' });
        fireEvent.change(field, { target: { value: 'Grace Hopper' } });
        fireEvent.blur(field);

        expect(correctName).not.toHaveBeenCalled();
    });

    it('says which bounds a picture has to meet before one is chosen', () => {
        renderSettings();

        expect(screen.getByText('JPG/PNG, up to 1 MB')).toBeDefined();
    });

    it('sends a picture of a kind this surface stores, under the kind it is', () => {
        const choosePicture = vi.fn();
        renderSettings({ profile: { ...named, choosePicture } });

        const picture = file('portrait.png', 'image/png', 64);
        choose(picture);

        expect(choosePicture).toHaveBeenCalledWith(picture, 'image/png');
    });

    it('refuses a file that is neither kind at the control rather than sending it', () => {
        const choosePicture = vi.fn();
        renderSettings({ profile: { ...named, choosePicture } });

        choose(file('portrait.gif', 'image/gif', 64));

        expect(choosePicture).not.toHaveBeenCalled();
        expect(screen.getByText(/neither a JPEG nor a PNG/u)).toBeDefined();
    });

    it('refuses a picture over a megabyte at the control rather than sending it', () => {
        const choosePicture = vi.fn();
        renderSettings({ profile: { ...named, choosePicture } });

        choose(file('portrait.png', 'image/png', largestPortraitOctets + 1));

        expect(choosePicture).not.toHaveBeenCalled();
        expect(screen.getByText(/larger than 1 MB/u)).toBeDefined();
    });

    it('clears the refusal once an admissible picture is chosen instead', () => {
        renderSettings();

        choose(file('portrait.gif', 'image/gif', 64));
        choose(file('portrait.png', 'image/png', 64));

        expect(screen.queryByText(/neither a JPEG nor a PNG/u)).toBeNull();
    });

    it('offers removal only to somebody who has a picture to remove', () => {
        renderSettings({ profile: { ...named, picture: null } });

        expect(screen.queryByRole('button', { name: 'Remove' })).toBeNull();
    });

    it('removes the picture, which is what falls back to the initials', () => {
        const removePicture = vi.fn();
        renderSettings({ profile: { ...named, picture: 'data:image/png;base64,AA==', removePicture } });

        fireEvent.click(screen.getByRole('button', { name: 'Remove' }));

        expect(removePicture).toHaveBeenCalledOnce();
    });

    it('says when the deployment would not take the name that was typed', () => {
        renderSettings({ profile: { ...named, nameNotAcceptable: true } });

        expect(screen.getByText(/was not accepted/u)).toBeDefined();
    });

    it('says when a change of the name did not reach the deployment at all', () => {
        renderSettings({ profile: { ...named, nameNotStated: true } });

        expect(screen.getByText(/not saved to the deployment/u)).toBeDefined();
    });

    it('says when a picture did not reach the deployment', () => {
        renderSettings({ profile: { ...named, pictureNotStated: true } });

        expect(screen.getByText(/was not saved to the deployment/u)).toBeDefined();
    });

    it('draws telemetry as the decision to withhold it, so the switch being on is the private answer', () => {
        renderSettings({ preferences: { ...settings, telemetryEnabled: false } });
        openApplication();

        expect(screen.getByRole('switch', { name: /Do not send telemetry/u })).toHaveProperty('checked', true);
    });

    it('hands the switch to what holds the preference, stated as what may be sent', () => {
        const chooseTelemetry = vi.fn();
        renderSettings({ preferences: { ...settings, chooseTelemetry } });
        openApplication();

        fireEvent.click(screen.getByRole('switch', { name: /Do not send telemetry/u }));

        expect(chooseTelemetry).toHaveBeenCalledWith(false);
    });

    it('says what withholding telemetry costs, and only while it is withheld', () => {
        renderSettings({ preferences: { ...settings, telemetryEnabled: true } });
        openApplication();

        expect(screen.queryByText(/support is harder/u)).toBeNull();
    });

    it('says what withholding telemetry costs once it is withheld', () => {
        renderSettings({ preferences: { ...settings, telemetryEnabled: false } });
        openApplication();

        expect(screen.getByText(/support is harder/u)).toBeDefined();
    });

    it('names where the records go, so the decision is about something rather than about a word', () => {
        renderSettings({ telemetryForwarding: forwardedTo });
        openApplication();

        expect(screen.getByText(/Sent to https:\/\/mail\.example/u)).toBeDefined();
    });

    it('says what a record carries and what it never carries', () => {
        renderSettings();
        openApplication();

        expect(screen.getByText(/never your mail, your addresses, your folders, or your password/u)).toBeDefined();
    });

    // A deployment that forwards nothing has nothing behind the switch, so it says so rather than offering a control
    // that decides nothing about a client that is already sending nothing.
    it('offers no switch where the deployment forwards no telemetry, and says why', () => {
        renderSettings({ telemetryForwarding: { answered: true, destination: null } });
        openApplication();

        expect(screen.getByText(/forwards no telemetry/u)).toBeDefined();
        expect(screen.queryByRole('switch', { name: /Do not send telemetry/u })).toBeNull();
    });

    // A deployment that has said nothing has not said no, and the frame records under the person's own answer while
    // it waits — so drawing the confirmed sentence here would say nothing is being sent while something is.
    it('says it is waiting rather than that there is nothing to turn off, before the deployment answers', () => {
        renderSettings({ telemetryForwarding: { answered: false } });
        openApplication();

        expect(screen.getByText(/Waiting for this deployment to say whether it forwards telemetry/u)).toBeDefined();
        expect(screen.queryByText(/forwards no telemetry/u)).toBeNull();
        expect(screen.queryByRole('switch', { name: /Do not send telemetry/u })).toBeNull();
    });

    it('offers the language here rather than in the menu that leads here', () => {
        renderSettings();
        openApplication();

        expect(screen.getByRole('group', { name: 'Language' })).toBeDefined();
    });

    it('opens on the person rather than on the client, which is the order the design project puts the two in', () => {
        renderSettings();

        expect(screen.getByRole('tab', { selected: true })).toHaveProperty('textContent', 'Profile');
        expect(screen.getByRole('textbox', { name: 'Full name' })).toBeDefined();
        expect(screen.queryByRole('group', { name: 'Language' })).toBeNull();
    });

    it('announces the tab that is open as the selected one and draws only its own half', () => {
        renderSettings();
        openApplication();

        expect(screen.getByRole('tab', { selected: true })).toHaveProperty('textContent', 'Application');
        expect(screen.queryByRole('textbox', { name: 'Full name' })).toBeNull();
    });

    it('moves between the tabs with the arrow keys, which is the keyboard a tab list carries', () => {
        renderSettings();

        fireEvent.keyDown(screen.getByRole('tablist'), { key: 'ArrowRight' });

        expect(screen.getByRole('tab', { selected: true })).toHaveProperty('textContent', 'Application');

        fireEvent.keyDown(screen.getByRole('tablist'), { key: 'ArrowLeft' });

        expect(screen.getByRole('tab', { selected: true })).toHaveProperty('textContent', 'Profile');
    });

    it('stays on the tab at the end of the list rather than wrapping round to the other one', () => {
        renderSettings();

        fireEvent.keyDown(screen.getByRole('tablist'), { key: 'ArrowLeft' });

        expect(screen.getByRole('tab', { selected: true })).toHaveProperty('textContent', 'Profile');
    });

    it('leaves one tab stop on the list, which is the tab that is open', () => {
        renderSettings();

        expect(screen.getByRole('tab', { name: 'Profile' })).toHaveProperty('tabIndex', 0);
        expect(screen.getByRole('tab', { name: 'Application' })).toHaveProperty('tabIndex', -1);
    });

    it('says where the name and the picture are held, and where they are not sent', () => {
        renderSettings();

        expect(screen.getByText(/Neither is sent to your mail server/u)).toBeDefined();
    });

    it('draws a conversation opening expanded as a choice that is off until it is made', () => {
        renderSettings();
        openApplication();

        expect(screen.getByRole('switch', { name: /Expand the whole thread/u })).toHaveProperty('checked', false);
    });

    it('hands the thread-expansion switch to what holds the preference', () => {
        const chooseThreadExpansion = vi.fn();
        renderSettings({ preferences: { ...settings, chooseThreadExpansion } });
        openApplication();

        fireEvent.click(screen.getByRole('switch', { name: /Expand the whole thread/u }));

        expect(chooseThreadExpansion).toHaveBeenCalledWith(true);
    });

    it('draws the thread-expansion switch as on where that is what the person set', () => {
        renderSettings({ preferences: { ...settings, expandWholeThread: true } });
        openApplication();

        expect(screen.getByRole('switch', { name: /Expand the whole thread/u })).toHaveProperty('checked', true);
    });

    it('offers no system-notification switch where the head offered no such operation', () => {
        renderSettings();
        openApplication();

        expect(screen.queryByRole('switch', { name: /Notify me on this machine/u })).toBeNull();
    });

    it('draws the system-notification switch as on where the machine has been left raising them', () => {
        renderSettings({ head: raisesThem });
        openApplication();

        expect(screen.getByRole('switch', { name: /Notify me on this machine/u })).toHaveProperty('checked', true);
    });

    it('keeps a refusal on the device, which is where the answer belongs and what the next start reads', () => {
        renderSettings({ head: raisesThem });
        openApplication();

        fireEvent.click(screen.getByRole('switch', { name: /Notify me on this machine/u }));

        expect(window.localStorage.getItem(deviceKeys.systemNotifications)).toBe('false');
        expect(screen.getByRole('switch', { name: /Notify me on this machine/u })).toHaveProperty('checked', false);
    });

    it('follows a refusal written while it is open, since the operating system is the other writer', () => {
        renderSettings({ head: raisesThem });
        openApplication();

        act(() => {
            chooseSystemNotifications(false);
        });

        expect(screen.getByRole('switch', { name: /Notify me on this machine/u })).toHaveProperty('checked', false);
    });

    it('closes from its own control, which is the way out that does not need a keyboard', () => {
        const onClose = vi.fn();
        renderSettings({ onClose });

        fireEvent.click(screen.getByRole('button', { name: 'Close settings' }));

        expect(onClose).toHaveBeenCalledOnce();
    });

    it('says what the client and the deployment are running, at its foot', () => {
        renderSettings({ deploymentVersion: '0.9.0' });

        expect(screen.getByText(/deployment 0\.9\.0/u)).toBeDefined();
    });

    it('says what the client alone is running while the deployment has answered nothing', () => {
        renderSettings({ deploymentVersion: null });

        expect(screen.queryByText(/, deployment /u)).toBeNull();
        expect(screen.getByText(/^MailFathom Client /u)).toBeDefined();
    });

    it('says it under either tab, that being about the client rather than about what is open', () => {
        renderSettings({ deploymentVersion: '0.9.0' });
        openApplication();

        expect(screen.getByText(/deployment 0\.9\.0/u)).toBeDefined();
    });
});
