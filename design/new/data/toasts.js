// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

MailFathomDesign.data.toastsSamples = [
  { kind: "neutral", name: "Neutral", title: "Archived 3 threads", body: "", action: "Undo",
    desc: "Confirmation of an ordinary operation. No colour — it does not take attention but offers a way back." },
  { kind: "success", name: "Success", title: "Message sent", body: "To: anna.kowalska@lexmar.example", action: "",
    desc: "Operation finished. Green accent only on the icon and the timer bar." },
  { kind: "error", name: "Error", title: "Could not send", body: "The SMTP server refused the connection (421). The text was saved to Drafts.", action: "Retry",
    desc: "Something failed and needs a decision. Always with a cause and a way to fix it." },
  { kind: "warning", name: "Warning", title: "Cancelled", body: "The operation was aborted before saving — nothing was changed.", action: "",
    desc: "A side effect or an interruption. Not an error, but the user needs to know." },
  { kind: "info", name: "Information", title: "Index extended", body: "Search now covers the contents of PDF attachments.", action: "",
    desc: "A system message unrelated to the last click." },
  { kind: "loading", name: "Running operation", title: "Packing attachments…", body: "14 files → attachments.zip", action: "",
    desc: "Does not vanish on its own. Closing it asks for confirmation and aborts the operation." },
];

MailFathomDesign.data.toastsBurst = [
  { kind: "neutral", title: "Archived 3 threads", action: "Undo" },
  { kind: "success", title: "Message sent", body: "To: team@lexmar.example" },
  { kind: "info", title: "Index extended", body: "Search now covers the contents of PDF attachments." },
  { kind: "warning", title: "2 recipients skipped", body: "Addresses outside the domain need legal approval." },
  { kind: "error", title: "Could not send", body: "The SMTP server refused the connection (421).", action: "Retry" },
];

MailFathomDesign.data.toastsMessages = {
  restored: { title: "Restored 3 threads", body: "The threads are back in the inbox." },
  cancelled: { title: "Cancelled", body: "The operation was aborted before saving — nothing was changed." },
  cancelAskText: "The operation is running. Closing the toast aborts it — partial results will not be saved.",
};

MailFathomDesign.data.toastsTasks = {
  archive: {
    cancelText: "The ZIP archive is being prepared. Aborting discards the partially packed file.",
    doneTitle: "Archive ready", doneBody: "attachments.zip — 14 files", ms: 6000,
  },
  mailboxSync: {
    title: "Syncing the mailbox…", body: "1,240 of 3,800 messages",
    cancelText: "The sync is running. Aborting stops it where it is — downloaded messages stay, the rest has to be fetched again.",
    doneTitle: "Mailbox synced", doneBody: "3,800 messages, 12 new threads", ms: 5200,
  },
  sendFailure: {
    title: "Sending message…", body: "To: anna.kowalska@lexmar.example",
    cancelText: "The message is being sent. Aborting stops delivery and saves it as a draft.",
    doneKind: "error", doneTitle: "Could not send",
    doneBody: "The SMTP server refused the connection (421). The text was saved to Drafts.",
    doneAction: "Retry", ms: 3400,
  },
};

MailFathomDesign.data.toastsBlockingOperations = {
  migration: {
    title: "Mailbox migration in progress",
    body: "Moving 3,800 messages and 214 attachments to the new server. The app is locked so nothing is lost halfway.",
    finishedTitle: "Migration finished", finishedBody: "3,800 messages on the new server, 0 errors.",
    abortedBody: "Messages already moved stay on the new server, the rest is unchanged.",
  },
  indexRebuild: {
    title: "Rebuilding the index",
    body: "The search index is being rebuilt. The time depends on the number of messages.",
    finishedTitle: "Index rebuilt", finishedBody: "Search now runs on a fresh index.",
    abortedBody: "The index was restored to its previous version.",
  },
};

MailFathomDesign.data.toastsBlockSpecs = [
  { k: "When", v: "Only operations where aborting halfway corrupts data — migration, bulk change, export." },
  { k: "Exit", v: "Background clicks and Esc do nothing. The only way out is Cancel → “Really abort?”." },
  { k: "After aborting", v: "The overlay disappears and a warning toast says what was saved and what was not." },
];
