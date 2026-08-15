# The analyzer's language, and what a second one requires

<!-- describes: src/Infrastructure/SensitiveContent/PersonalData/**, src/Host/Configuration/SensitiveContent/PersonalDataAnalyzerOptions.cs, deploy/compose/compose.yaml, deploy/quadlet/mailfathom-presidio.container, deploy/helm/mailfathom/values.yaml -->

> [!WARNING]
> Some of the steps on this page are performed in a product this project does not control. Any screen, menu, or field
> named here can be renamed or moved there at any time. Where this page and that product's own documentation disagree,
> the product's documentation is right.

The personal-data scanner asks its analyzer in exactly one language, named once for the whole deployment by
[`SensitiveContent:PersonalDataAnalyzer:Language`](configuration-reference.md#sensitivecontent). A mailbox is not
single-language — a Polish deployment receives English mail and an English one receives Polish mail — so this page
states what that one language buys, what it leaves unreachable, and what it actually takes to change it.
[The personal-data scanner](../features/sensitive-content-scanning.md#the-personal-data-scanner) records what the
feature does with what it finds; this page is about what it is able to find at all.

## What the shipped analyzer is configured with

The analyzer image the three deployment shapes pin reads **three** configuration files, each named by an environment
variable of its own and each baked into the image at build time:

| Variable | Default file in the image | What it decides |
| --- | --- | --- |
| `NLP_CONF_FILE` | `presidio_analyzer/conf/default.yaml` | Which NLP framework runs and which model is loaded per language |
| `RECOGNIZER_REGISTRY_CONF_FILE` | `presidio_analyzer/conf/default_recognizers.yaml` | Which recognizers exist, which are switched on, and which languages each serves |
| `ANALYZER_CONF_FILE` | `presidio_analyzer/conf/default_analyzer.yaml` | Which languages the engine answers for at all |

As shipped, all three say English and only English: the NLP configuration loads one spaCy model, and both the registry
and the engine declare `supported_languages: [en]`. Nineteen entities are reachable that way, which is what a deployment
that never touches this page is scanning with.

**A model is installed while the image is built, not while it starts.** The image's build step reads the NLP
configuration and downloads the models it names, so a configuration file mounted over the running container names a
model that is not on disk and the analyzer fails to load rather than fetching it. That single fact is what makes every
route below either a derived image or a mount *plus* an install step, and it is why no environment variable on the
analyzer container enables a language.

## One language per deployment, and what the probe does about it

`Language` is a scalar validated to two lowercase letters. It is written into the `language` argument of every
`/analyze` call and of the entity probe MailFathom's readiness check makes, and there is no per-account, per-folder, or
per-message language and no detection. It is also part of the detector revision every finding carries, so changing it
marks derived text written under the old one stale — [derived
data](../features/sensitive-content-scanning.md#derived-data-is-written-redacted-and-stamped) records what that costs.

The readiness probe asks the analyzer which entities it recognises **in that language** and requires each switched-on
category to have **at least one** of them. That rule is right — a narrower registry costing recall inside a category
that still works should not refuse a start — but it means a category can be half unreachable while the deployment reads
healthy. `NationalIdentifier` is the worked example: it covers 27 analyzer entities, of which the shipped image
registers `US_SSN` and `US_ITIN` under `en`. `PL_PESEL` is not among them, so a PESEL in Polish correspondence is not
found, no log line says so, and the probe passes on the strength of the two it does know.

What the probe does catch is a category with *nothing* behind it, and that is the ordinary result of naming a language
the image was not built for. Two failures, in the order an operator meets them:

- **The language is not in the analyzer's registry at all.** The entity probe answers an empty list, and MailFathom
  reports `81002` naming `SensitiveContent:PersonalDataAnalyzer:Endpoint` on its readiness log. This is what setting
  `Language: pl` against the unmodified image does — not a degraded scan, and not a fall back to English.
- **The language is registered, but a switched-on category has no entity in it.** The probe names that category in the
  `81002`. Under `pl` with the default category set, `IdentityDocument` is that category, for the reason the next
  section gives.

Either way the host comes up, reports itself unready, and stays out of traffic until the analyzer answers — it is not a
startup failure. [Health endpoints](health-endpoints.md#the-three-probes) records what each probe consults.

## What each language can actually find

The registry the image ships names a recognizer for eleven languages, and most of the locale-specific entries in it are
`enabled: false` — every German, Korean, Australian, Indian, Nigerian, Philippine, Canadian, Singaporean, South
African, Swedish, Thai, and Turkish one, and the British ones other than the NHS number. Nine recognizers declare no
language at all, so they are instantiated for whatever language list the registry is given: the ones behind
`IBAN_CODE`, `EMAIL_ADDRESS`, `PHONE_NUMBER`, `DATE_TIME`, `IP_ADDRESS`, `MAC_ADDRESS`, and `MEDICAL_LICENSE`, plus two
MailFathom maps to nothing.

The table is what remains once both filters have been applied, assuming a model for the language has been installed and
nothing in the registry has been switched on by hand. **Empty** names a default category with no entity behind it,
which is the category the readiness probe refuses on.

| Language | Beyond the language-agnostic entities | Default categories left empty |
| --- | --- | --- |
| `en` | `CREDIT_CARD`, `US_SSN`, `US_ITIN`, `US_BANK_NUMBER`, `US_PASSPORT`, `US_DRIVER_LICENSE`, `UK_NHS` | none |
| `it` | `CREDIT_CARD`, `IT_FISCAL_CODE`, `IT_VAT_CODE`, `IT_PASSPORT`, `IT_IDENTITY_CARD`, `IT_DRIVER_LICENSE` | none |
| `es` | `CREDIT_CARD`, `ES_NIF`, `ES_NIE` | `IdentityDocument` |
| `pl` | `CREDIT_CARD`, `PL_PESEL` | `IdentityDocument` |
| `de`, `fr`, `ko`, `kr`, `sv`, `th`, `tr` | nothing | `PaymentCard`, `NationalIdentifier`, `IdentityDocument` |

**Polish gets one recognizer, and it is not the one most mail carries.** `PL_PESEL` is the only Polish entry the
registry declares; there is no Polish passport recognizer and no Polish identity-card one, while Germany, Spain, India,
Italy, Korea, the United Kingdom, and the United States each have a passport recognizer somewhere in the registry, and
Germany and Italy an identity-card one. So the two identifiers
Polish correspondence carries most are exactly the two with nothing to switch on, and `IdentityDocument` — a category on
by default — has nothing behind it in that language. A Polish deployment either adds a recognizer for one of them, by
the route below, or drops `IdentityDocument` from `SensitiveContent:Pii:Categories` and accepts that those numbers are
not redacted.

The other direction holds too, and it is not a defect: MailFathom's corpus names entities no shipped registry produces
in any language, `FI_PERSONAL_IDENTITY_CODE` among them. The corpus is written against what the analyzer's recognizer
set can be configured to report, not against one image's defaults — [the
mapping](../features/sensitive-content-scanning.md#the-categories-and-which-are-on-by-default) is what an operator
configures categories against, and an entity nothing reports simply never arrives.

## Running the analyzer in another language

Five steps, and only the last of them is on MailFathom's side.

1. **Name the model.** Add a `lang_code` / `model_name` pair to the `models:` list in the NLP configuration. It is a
   list rather than a scalar, so a second language is declared *beside* English rather than in place of it — which is
   what a mixed mailbox wants, even though MailFathom asks in one of them at a time.

   ```yaml
   nlp_engine_name: spacy
   models:
     - lang_code: en
       model_name: en_core_web_lg
     - lang_code: pl
       model_name: pl_core_news_md
   ```

2. **Get the model into the image.** Build a derived image from the pinned one with the file above supplied as the
   build's NLP configuration, which is what installs the model. Mounting the file over a running container instead is a
   half of that step rather than an alternative to it: the container then names a model nothing put on its disk, and it
   fails to load. A mount is useful only on an image that already carries every model the mounted file names.

3. **Widen the registry.** Add the language to `supported_languages:` in the recognizer-registry configuration and set
   `enabled: true` on the entries you want, since most locale-specific ones ship off. A recognizer whose own
   `supported_languages` does not list the language is dropped whatever else is configured, which is why widening the
   list is not by itself enough for a locale-specific entry.

4. **Widen the engine.** The analyzer configuration's own `supported_languages:` has to carry the same list as the
   registry's; upstream states that requirement outright, and an engine narrower than its registry answers for a
   language whose recognizers it will never reach.

5. **Then tell MailFathom.** `SensitiveContent:PersonalDataAnalyzer:Language` names the new code last, once an analyzer
   that answers for it is running. Setting the key first produces the unready deployment the previous section
   describes.

Where the image is named differs per deployment shape, and nothing else about them changes:

| Shape | What names the analyzer image |
| --- | --- |
| Compose | `MAILFATHOM_PRESIDIO_IMAGE` in `.env`, which the analyzer service reads — see [Docker Compose](deployment-compose.md#personal-data-scanning) |
| Quadlet | The `Image=` line in the copy of [`mailfathom-presidio.container`](https://github.com/Krzysztof318/MailFathom/blob/main/deploy/quadlet/mailfathom-presidio.container) that was installed |
| Kubernetes | `personalDataScanning.analyzer.image`, or `analyzer.deploy: false` with an `endpoint` naming an analyzer you operate — see [Kubernetes](deployment-kubernetes.md#personal-data-scanning) |

The chart's analyzer Deployment mounts no configuration and takes no analyzer environment of its own, deliberately: a
chart value per Presidio setting would be this repository publishing a second, partial copy of a third party's
configuration schema. A derived image carries its own configuration, and an analyzer that needs more than that is one
the deployment operates itself.

Whichever route is taken, **the image is no longer the pinned one every other MailFathom deployment runs**. Findings
carry the analyzer profile they were produced under rather than the image tag, so nothing detects the substitution for
you; treat the derived image as a dependency of your own, rebuild it when the pin here moves, and record it wherever
your deployment records the images it runs. A derived image is also yours to distribute rather than MailFathom's, so
its notices are yours to preserve — the analyzer and the models it loads are permissively licensed and
[the third-party register](https://github.com/Krzysztof318/MailFathom/blob/main/THIRD_PARTY_LICENSES.md) names the terms
this project reviewed them under.

## Adding a recognizer MailFathom can use

A recognizer the analyzer gains is not a recognizer MailFathom asks about. Every `/analyze` request names the entities
it wants, computed from the switched-on categories and their suppressions, and an entity absent from that list is
neither requested nor mapped when it arrives. So the analyzer-side half below is necessary and never sufficient: the
last two steps are a code change in this repository.

The Polish identity card is the worked case, because it exercises every step.

**On the analyzer.** Declare a custom entry in the recognizer-registry configuration. A pattern recognizer needs a
name, the entity it reports, one or more patterns with a base score, and the language it serves; context words lift the
score of a match found near one of them, and they are per language, so they are translated rather than reused.

```yaml
  - name: PlIdCardRecognizer
    type: custom
    supported_entity: PL_ID_CARD
    patterns:
      - name: "Polish identity card"
        regex: "\\b[A-Z]{3} ?\\d{6}\\b"
        score: 0.5
    supported_languages:
      - language: pl
        context: [dowód, osobisty, dowodu, tożsamości]
```

**Clear the floor.** The confidence floor travels on the request and the analyzer applies it, so a finding scored below
`SensitiveContent:PersonalDataAnalyzer:MinimumConfidence` never crosses the process boundary. A base score under the
floor — `0.4` by default — means the recognizer reports nothing unless a context word lifts it, which is a recognizer
that works on some sentences and not others. Choose the base score against the floor the deployment runs, and remember
that lowering the floor to accommodate one recognizer lowers it for every other one too.

**In this repository.** Add the entity to
[`PresidioEntityCorpus`](https://github.com/Krzysztof318/MailFathom/blob/main/src/Infrastructure/SensitiveContent/PersonalData/PresidioEntityCorpus.cs),
spelled exactly as the analyzer spells it, inside the category it belongs to — `IdentityDocument` here. That is what
puts it in the request, what makes it a rule an operator can suppress by name, and what maps the offsets back onto the
text. Then move `MappingRevision` in the same file, because a changed mapping is a changed detector.

**Then pay for the revision.** Moving it changes the detector revision every finding carries, and the derivation stamp
computed from it, so every message already indexed reads as derived under a different configuration. The startup report
counts them and
[`SensitiveContent:RebuildStaleDerivedData`](configuration-reference.md#sensitivecontent) is what re-derives them — one
full re-extraction, re-chunking, and re-embedding of the affected messages, billed again on a hosted embedding
endpoint. That is the honest price of a new recognizer, and it is the reason to add the ones a deployment needs in one
change rather than one at a time.

## Verifying it took

Ask the analyzer directly, from somewhere inside the deployment's own network, before restarting MailFathom against it:

```bash
curl --silent 'http://presidio-analyzer:3000/supportedentities?language=pl'
curl --silent 'http://presidio-analyzer:3000/recognizers?language=pl'
```

An empty array from the first is the analyzer saying it has nothing for that language, which is exactly what MailFathom
reports as unready. A list that omits an entity you configured is a recognizer that loaded for a different language or
did not load at all, and the second call names the recognizers rather than their entities, which is what separates the
two. Once the entities are there, the readiness probe is the last check: `/health` answers `Healthy` when every
switched-on category has at least one of them, and the log names the category when one does not.

## What this page does not cover

**Asking in more than one language at a time.** `Language` is a scalar, one request states one language, and nothing
detects a message's own. A deployment whose mail is genuinely mixed chooses the language its regulated identifiers are
written in and accepts that the other language's locale-specific entities are not found. What survives the choice is
the row of entities registered against no language — IBANs, email addresses, phone numbers, dates, network addresses,
and medical licence numbers — and the named-entity ones, which are found as well as a model for the configured
language reads the other language's text.

**Recognizers MailFathom ships for a language.** The corpus above is the analyzer's vocabulary as this build maps it,
and MailFathom neither carries recognizer definitions nor installs them. What a deployment adds, it adds to its own
analyzer image by the route on this page.
