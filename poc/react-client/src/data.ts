/**
 * Every value the PoC renders. Transcribed from the Claude Design mockup
 * "MailFathom Demo" so the screens can be compared with it directly.
 */

export type Account = { address: string; freshness: string; stale?: boolean };

export const accounts: Account[] = [
  { address: "k.kasprowicz@firma.pl", freshness: "2 min" },
  { address: "kontakt@firma.pl", freshness: "6 min" },
  { address: "archiwum-2019", freshness: "4 godz.", stale: true },
];

export const savedViews = ["Renegocjacja Contoso", "Faktury 2026", "Umowy do odnowienia"];

export type RecentQuestion = { id: string; text: string; blocks: string; when: string };

export const recentQuestions: RecentQuestion[] = [
  {
    id: "contoso",
    text: "Jak zmieniały się warunki umowy z Contoso od 2021 roku?",
    blocks: "Timeline · FactTable",
    when: "wczoraj",
  },
  { id: "budget", text: "Kto zatwierdzał budżet indeksacji w 2023?", blocks: "People", when: "18.08" },
  { id: "policy", text: "Gdzie jest ostatnia wersja polisy ubezpieczeniowej?", blocks: "Answer · Gallery", when: "11.08" },
];

export const scopeSummary = "Zakres: 214 138 wiadomości · 31 402 załączniki";
export const mailboxSummary = "3 konta · 214 tys. wiadomości";
export const syncSummary = "Synchronizacja 2 min temu";

/** The one run the PoC can produce, keyed to the first recent question. */
export const run = {
  question: recentQuestions[0].text,
  scope: ["Wszystkie skrzynki", "2021–2026", "Contoso Sp. z o.o."],
  meta: "run 7f3a · 2,8 s · 41 dokumentów przeszukanych",
};

/** An Answer block is text interleaved with citation indices, never a formatted string. */
export type AnswerSegment = { text: string } | { cite: number };

const answerBody: AnswerSegment[] = [
  { text: "Warunki zmieniały się w trzech etapach: umowa ramowa z kwietnia 2021" },
  { cite: 1 },
  { text: ", wprowadzenie indeksacji CPI bez limitu w lutym 2023" },
  { cite: 3 },
  { text: " i skrócenie SLA z 4 h do 2 h w aneksie z listopada 2025" },
  { cite: 5 },
  { text: ". Propozycja na 2027 podnosi cenę o 8% i utrzymuje brak górnego limitu indeksacji." },
];

export const answer = {
  confidence: "pewność wysoka",
  gap: "1 luka: brak aneksu 2022",
  citationCount: "7 cytowań",
  body: answerBody,
};

export type TimelineEvent = { date: string; title: string; detail: string; current?: boolean };

export const timeline = {
  summary: "4 zdarzenia · 2021–2026",
  events: [
    { date: "12.04.2021", title: "Umowa ramowa", detail: "1 200 zł/mies · SLA 4 h" },
    { date: "03.02.2023", title: "Indeksacja CPI", detail: "bez górnego limitu" },
    { date: "18.11.2025", title: "Aneks SLA", detail: "4 h → 2 h · +8%" },
    { date: "26.08.2026", title: "Propozycja 2027", detail: "decyzja do 28.08", current: true },
  ] satisfies TimelineEvent[],
};

export type FactRow = {
  version: string;
  price: string;
  sla: string;
  indexation: string;
  cite: number;
  proposal?: boolean;
};

export const factTable = {
  note: "kolumny z katalogu · każda komórka ma źródło",
  columns: ["Wersja", "Cena", "SLA", "Indeksacja", "Źródło"],
  rows: [
    { version: "Umowa 2021", price: "1 200 zł", sla: "4 h", indexation: "brak", cite: 1 },
    { version: "Aneks 2023", price: "1 261 zł", sla: "4 h", indexation: "CPI, bez limitu", cite: 3 },
    { version: "Aneks 2025", price: "1 452 zł", sla: "2 h", indexation: "CPI, bez limitu", cite: 5 },
    { version: "Propozycja 2027", price: "1 568 zł", sla: "2 h", indexation: "CPI, bez limitu", cite: 7, proposal: true },
  ] satisfies FactRow[],
};

export type Evidence = {
  cite: number;
  title: string;
  quote: string;
  meta: string;
  /** Present when the citation resolves to a message the mail client can open. */
  threadId?: string;
};

export const evidenceList = {
  summary: "7 źródeł · 3 dokumenty",
  items: [
    {
      cite: 1,
      title: "Umowa ramowa.pdf",
      quote: "„Wynagrodzenie miesięczne wynosi 1 200 zł netto…”",
      meta: "12.04.2021 · trafność 0,94 · str. 3",
    },
    {
      cite: 3,
      title: "Re: indeksacja od 2023",
      quote: "„…waloryzacja wg CPI, bez górnego limitu.”",
      meta: "03.02.2023 · A. Kowalska · 0,91",
    },
    {
      cite: 5,
      title: "Aneks SLA.pdf",
      quote: "„Czas reakcji skraca się do 2 godzin…”",
      meta: "18.11.2025 · trafność 0,89",
      threadId: "aneks",
    },
  ] satisfies Evidence[],
};

export const suggestedAction = {
  question: "Przygotować szkic odpowiedzi z kontrpropozycją limitu indeksacji?",
  reason: "Powód: decyzja wymagana do 28.08 · szkic pozostaje lokalny",
  confirm: "Przygotuj szkic",
  dismiss: "Odrzuć",
};

/** The evidence inspector — one document, opened from a citation. */
export const evidenceDocument = {
  cite: 5,
  position: "DOWÓD 5 z 7",
  title: "Aneks SLA.pdf",
  meta: ["str. 2 z 4", "18.11.2025", "wiadomość: „Aneks do umowy — podpisy”", "nadawca uwierzytelniony"],
  before:
    "§3 ust. 1 Strony potwierdzają zakres usług objętych umową ramową z dnia 12 kwietnia 2021 r. oraz dotychczasowy sposób rozliczania wynagrodzenia miesięcznego.",
  highlight:
    "§3 ust. 2 Czas reakcji na zgłoszenie krytyczne skraca się z 4 godzin do 2 godzin w dni robocze. Z tytułu podwyższonego poziomu usług wynagrodzenie ulega zwiększeniu o 8%.",
  after:
    "§3 ust. 3 Pozostałe postanowienia umowy, w tym zasady waloryzacji wynagrodzenia wskaźnikiem CPI, pozostają bez zmian.",
  supports: ["SLA 2 h", "+8% ceny"],
  supportsTail: " · brak zmiany indeksacji",
  trail: [
    "retrieval semantyczny → 41 kandydatów",
    "reguła deterministyczna: sortowanie po dacie aneksu",
    "model: streszczenie i wybór PresentationPlan",
  ],
};

export type Folder = { name: string; count?: number };

export const folders: Folder[] = [
  { name: "Wszystkie", count: 12 },
  { name: "Odebrane", count: 8 },
  { name: "Ważne", count: 3 },
  { name: "Wysłane" },
  { name: "Szkice", count: 2 },
  { name: "Archiwum" },
];

export const aiFilters = ["Wymaga decyzji", "Zobowiązania", "Terminy w tym tygodniu"];

export type Message = {
  id: string;
  from: string;
  org?: string;
  subject: string;
  time: string;
  hint?: string;
  /** A strong hint carries the accent treatment the mockup gives the selected thread. */
  hintStrong?: boolean;
};

export const messages: Message[] = [
  {
    id: "aneks",
    from: "Anna Kowalska",
    org: "Contoso",
    subject: "Aneks do umowy — podpisy",
    time: "09:14",
    hint: "Wymaga decyzji do piątku · zmiana SLA",
    hintStrong: true,
  },
  { id: "faktura", from: "Finanse", subject: "Faktura 08/2026", time: "08:02", hint: "Termin płatności: 29.08" },
  {
    id: "harmonogram",
    from: "Piotr Zieliński",
    subject: "Re: harmonogram wdrożenia",
    time: "wcz.",
    hint: "Potwierdził etap drugi",
  },
  { id: "cpi", from: "Marta Nowak", subject: "Kalkulacja CPI 2027", time: "wcz.", hint: "Zobowiązanie: odpowiedź do 27.08" },
  { id: "opinia", from: "Jacek Wrona", subject: "Opinia prawna — limit waloryzacji", time: "24.08" },
];

export type Attachment = { kind: string; name: string; size: string };

export type Thread = {
  id: string;
  subject: string;
  sender: string;
  meta: string;
  state: { label: string; value: string }[];
  intro: string;
  highlight?: string;
  citation?: number;
  citationNote?: string;
  attachments: Attachment[];
};

export const threads: Record<string, Thread> = {
  aneks: {
    id: "aneks",
    subject: "Aneks do umowy — podpisy",
    sender: "Anna Kowalska <a.kowalska@contoso.example>",
    meta: "dzisiaj 09:14 · wątek: 6 wiadomości",
    state: [
      { label: "Ustalenia", value: "SLA 2 h · cena +8% od 2027" },
      { label: "Otwarte pytanie", value: "Górny limit indeksacji CPI" },
      { label: "Zobowiązanie", value: "Decyzja po naszej stronie do 28.08" },
    ],
    intro:
      "Dzień dobry,\nprzesyłam aneks w wersji do podpisu. Względem umowy z 2021 roku zmienia się poziom usług i wynagrodzenie:",
    highlight:
      "Czas reakcji na zgłoszenie krytyczne skraca się z 4 godzin do 2 godzin w dni robocze. Z tytułu podwyższonego poziomu usług wynagrodzenie ulega zwiększeniu o 8%.",
    citation: 5,
    citationNote: "fragment przywołany w wyniku „Odkrywaj”",
    attachments: [
      { kind: "PDF", name: "Aneks SLA.pdf", size: "248 kB" },
      { kind: "PDF", name: "Umowa ramowa.pdf", size: "1,2 MB" },
    ],
  },
  faktura: {
    id: "faktura",
    subject: "Faktura 08/2026",
    sender: "Finanse <finanse@firma.pl>",
    meta: "dzisiaj 08:02 · wątek: 1 wiadomość",
    state: [
      { label: "Ustalenia", value: "Kwota 1 452 zł netto" },
      { label: "Otwarte pytanie", value: "Brak" },
      { label: "Zobowiązanie", value: "Płatność do 29.08" },
    ],
    intro:
      "Dzień dobry,\nw załączeniu faktura za sierpień. Termin płatności mija 29 sierpnia; numer rachunku bez zmian.",
    attachments: [{ kind: "PDF", name: "FV-08-2026.pdf", size: "96 kB" }],
  },
  harmonogram: {
    id: "harmonogram",
    subject: "Re: harmonogram wdrożenia",
    sender: "Piotr Zieliński <p.zielinski@contoso.example>",
    meta: "wczoraj 16:41 · wątek: 4 wiadomości",
    state: [
      { label: "Ustalenia", value: "Etap drugi potwierdzony" },
      { label: "Otwarte pytanie", value: "Termin etapu trzeciego" },
      { label: "Zobowiązanie", value: "Harmonogram do 05.09" },
    ],
    intro:
      "Potwierdzam zakończenie etapu drugiego. Etap trzeci zaczynamy po akceptacji aneksu — proszę o sygnał, gdy będzie podpisany.",
    attachments: [],
  },
  cpi: {
    id: "cpi",
    subject: "Kalkulacja CPI 2027",
    sender: "Marta Nowak <m.nowak@firma.pl>",
    meta: "wczoraj 11:20 · wątek: 2 wiadomości",
    state: [
      { label: "Ustalenia", value: "CPI 2026 szacowany na 4,1%" },
      { label: "Otwarte pytanie", value: "Czy proponujemy limit 3%?" },
      { label: "Zobowiązanie", value: "Odpowiedź do 27.08" },
    ],
    intro:
      "Przy obecnym zapisie waloryzacja za 2027 wychodzi 1 568 zł. Z limitem 3% byłoby 1 496 zł — różnica roczna to ok. 864 zł.",
    attachments: [{ kind: "XLSX", name: "CPI-2027.xlsx", size: "42 kB" }],
  },
  opinia: {
    id: "opinia",
    subject: "Opinia prawna — limit waloryzacji",
    sender: "Jacek Wrona <j.wrona@kancelaria.example>",
    meta: "24.08 14:05 · wątek: 3 wiadomości",
    state: [
      { label: "Ustalenia", value: "Limit waloryzacji jest dopuszczalny" },
      { label: "Otwarte pytanie", value: "Forma aneksu" },
      { label: "Zobowiązanie", value: "Brak" },
    ],
    intro:
      "Wprowadzenie górnego limitu waloryzacji nie wymaga zmiany umowy ramowej — wystarczy aneks o treści analogicznej do aneksu z 2025 roku.",
    attachments: [{ kind: "PDF", name: "Opinia.pdf", size: "310 kB" }],
  },
};

export const threadComposer = {
  placeholder: "Zapytaj o wątek albo przygotuj odpowiedź…",
  scope: "Zakres: ten wątek",
  extra: "+ zaznaczony fragment",
  note: "Szkic pozostaje lokalny do potwierdzenia",
};
