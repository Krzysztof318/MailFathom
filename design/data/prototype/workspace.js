// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

// Who is signed in, and the build the About screen names.
MailFathomDesign.data.me = { name: "Karolina Kowalska", address: "k.kowalska@company.example" };
MailFathomDesign.data.app = { version: "2.4.2", build: "build 20260907" };

// The mail accounts in the folder column and the Move dialog: `counts` are the unread badges on the standard
// folders, and `moveFolders` the folders Move offers in that account.
MailFathomDesign.data.accounts = [
  { key: "acc1", label: "Nordwind · work", address: "k.kowalska@nordwind.example", dot: "var(--accent)", counts: { Inbox: "8", Drafts: "2", Spam: "3" },
    moveFolders: [
      { icon: "inbox", label: "Inbox" }, { icon: "archive", label: "Archive" },
      { icon: "folder", label: "Cases" }, { icon: "folder", label: "Contracts" }, { icon: "folder", label: "Archive 2026" },
    ] },
  { key: "acc2", label: "Personal mail", address: "kowalska.k@mail.example", dot: "#4d9aa8", counts: { Inbox: "4" },
    moveFolders: [
      { icon: "inbox", label: "Inbox" }, { icon: "archive", label: "Archive" },
      { icon: "folder", label: "Clients" }, { icon: "folder", label: "To decide" },
    ] },
];

// The unified view across both accounts at the top of the folder column.
MailFathomDesign.data.unifiedAccounts = {
  label: "All accounts", title: "Unified folders - two accounts", dot: "linear-gradient(135deg,var(--accent) 50%,#4d9aa8 50%)",
  counts: { Inbox: "12", Drafts: "2" },
};

// The mailboxes a message can be sent from, and the Reply-To a message carries unless another is added.
MailFathomDesign.data.mailboxes = [
  { mail: "k.kowalska@nordwind.pl", dot: "var(--accent)" },
  { mail: "kowalska.k@mail.example", dot: "#4d9aa8" },
  { mail: "board@nordwind.example", dot: "#b0873a" },
];
MailFathomDesign.data.defaultReplyTo = "k.kowalska@nordwind.example";

// The accounts Settings lists, with what their connection forms hold.
MailFathomDesign.data.mailAccounts = [
  { id: "a1", name: "Karolina Kowalska", mail: "k.kowalska@nordwind.pl", login: "k.kowalska@nordwind.pl", pass: "hunter-nordwind-24", dot: "var(--accent)", imapHost: "imap.nordwind.pl", imapPort: "993", imapEnc: "ssl", smtpHost: "smtp.nordwind.pl", smtpPort: "587", smtpEnc: "starttls", earliest: "", folders: { inbox: "INBOX", sent: "INBOX.Sent", drafts: "INBOX.Drafts", archive: "INBOX.Archive", junk: "INBOX.Spam", trash: "INBOX.Trash" } },
  { id: "a2", name: "Karolina - private", mail: "kowalska.k@mail.example", login: "kowalska.k", pass: "mailexample2026", dot: "#4d9aa8", imapHost: "imap.mail.example", imapPort: "993", imapEnc: "ssl", smtpHost: "smtp.mail.example", smtpPort: "465", smtpEnc: "ssl" },
  { id: "a3", name: "Nordwind board", mail: "board@nordwind.example", login: "board", pass: "board-shared-key", dot: "#b0873a", imapHost: "imap.nordwind.example", imapPort: "143", imapEnc: "starttls", smtpHost: "smtp.nordwind.example", smtpPort: "587", smtpEnc: "starttls" },
];
