// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { App } from './App';
import { Containment } from './containment/Containment';
import { regionOf } from './containment/caughtRegion';
import { showStaticFailure } from './containment/staticFailure';
import { adoptedDeployment } from './deployment/adoptedDeployment';
import { attachmentExchange, AttachmentExchangeContext } from './deployment/attachmentExchange';
import { AttachmentUploadContext, uploadAttachment } from './deployment/attachmentUpload';
import { portraitExchange } from './deployment/portraitExchange';
import { sendToDeployment } from './deployment/sendToDeployment';
import { LocalizationProvider } from './localization/Localization';
import { configuredConnection } from './shellOperations/configuredConnection';
import { LinkOpenerContext, linkOpenerForThisApplication } from './shellOperations/linkOpener';
import { credentialStore } from './signIn/credentialStore';
import { browserSchedule, openSignalChannel } from './signals/signalChannel';
import { clientTelemetryForThisApplication, TelemetryContext } from './telemetry/clientTelemetry';
import { ThemeProvider } from './theme/Theme';
import { ToastsProvider } from './toasts/Toasts';
import { WorkspaceProvider } from './workspace/Workspace';
import './styles.css';

// The one place a head is asked about. Everything below receives the operation rather than the answer to that
// question, which is what keeps one client one client across both heads.
const openLink = linkOpenerForThisApplication();

// The one place the client's traces, metrics, and logs are composed. It is here rather than beside a screen for the
// reason the link opener is: what this client reports about itself is the application's decision and not one screen's,
// and everything below receives it as a value rather than registering anything of its own.
const telemetry = clientTelemetryForThisApplication();

const container = document.getElementById('root');

// A document with nothing to mount into, and a resolution that rejected before anything was rendered, are the two
// failures that happen before there is a client to contain one. Both used to end in a blank document with the reason
// visible in a console nobody opens; both now say so where somebody is looking, and both are reported.
if (container === null) {
    telemetry.renderFailed('application', new Error('index.html carries no #root element for the client to mount.'));
    showStaticFailure();
} else {
    open(container).catch((reason: unknown) => {
        telemetry.renderFailed('application', reason);
        showStaticFailure();
    });
}

/**
 * The edge of the application, and the one place the deployment this run belongs to and the credential it holds are
 * resolved. Both arrive below as values, which is what keeps the difference between the two heads — a client served by
 * its deployment or pointed at one, a credential in a keychain or in the tab — out of every screen underneath.
 *
 * The credential store is asked before anything is rendered rather than while it is, because a screen that mounts
 * signed out and then swaps to the workspace is a screen somebody has already started reading. What a deployment
 * configured is asked first, for the same reason and one more: the address it may carry is what the credential kept on
 * this machine is filed under, so a client that read the store before it knew where it was pointed would read back the
 * credential of whichever deployment it was pointed at last.
 *
 * The five things that outlive every screen stand above it: the language it reads in, the theme it is painted in, what
 * the person is carrying between the spaces, how a link they follow leaves the application, and the surface the client
 * says back on. Each is above the frame because nothing below the frame may be what decides it, and each is above the
 * sign-in screen for the same reason — somebody who has not signed in yet reads in a language, is painted in a theme,
 * and is told what just happened exactly as somebody who has.
 */
async function open(root: HTMLElement): Promise<void> {
    const deployment = adoptedDeployment(await configuredConnection());
    const adopted = deployment.outcome === 'resolved' ? deployment.adopted : null;
    const credentials = await credentialStore();
    const signedInWith = adopted === null ? null : await credentials.read(adopted.deployment);

    // The root is where what the deployment is told about a failed render is composed, which is why the boundaries
    // below report nothing themselves: one more region is one more boundary rather than one more reporter. React
    // names the boundary that caught, so which region contained it is read from that; a failure no boundary caught
    // has taken the whole application with it, and what is left of the document is the surface it carries itself.
    //
    // Both still write what was thrown to the console, which is what React's own handlers do and what stating a
    // handler would otherwise take away: the record that leaves for the deployment says which region and which class
    // and deliberately no more, so whoever is in front of the browser would be left with less than they had.
    createRoot(root, {
        onCaughtError: (error, at) => {
            console.error(error);
            telemetry.renderFailed(regionOf(at.errorBoundary), error);
        },
        onUncaughtError: (error) => {
            console.error(error);
            telemetry.renderFailed('application', error);
            showStaticFailure();
        },
    }).render(
        <StrictMode>
            <LocalizationProvider>
                <ToastsProvider>
                    <ThemeProvider>
                        <WorkspaceProvider>
                            <LinkOpenerContext value={openLink}>
                                <AttachmentExchangeContext value={attachmentExchange}>
                                    <AttachmentUploadContext value={uploadAttachment}>
                                        <TelemetryContext value={telemetry}>
                                            {/* The last resort, standing inside everything that outlives a screen so
                                                that what it draws is read in the reader's own language and painted in
                                                their own theme. Every narrower boundary is placed beside the region
                                                it stands around; this one is what catches whatever none of them did. */}
                                            <Containment region="application">
                                                <App
                                                    credentials={credentials}
                                                    deployment={deployment}
                                                    openSignals={openSignalChannel}
                                                    portraits={portraitExchange}
                                                    send={sendToDeployment}
                                                    signalSchedule={browserSchedule}
                                                    signedInWith={signedInWith}
                                                />
                                            </Containment>
                                        </TelemetryContext>
                                    </AttachmentUploadContext>
                                </AttachmentExchangeContext>
                            </LinkOpenerContext>
                        </WorkspaceProvider>
                    </ThemeProvider>
                </ToastsProvider>
            </LocalizationProvider>
        </StrictMode>,
    );
}
