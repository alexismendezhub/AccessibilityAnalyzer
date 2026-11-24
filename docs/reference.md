# Accessibility Analyzer: reference

Everything beyond the README: how it is built, how to use and configure it, the API and the tests.

## 1. What it does

- **Paste HTML**: paste any HTML or component template (Angular, Razor, Blazor, Vue, plain HTML) and get the
  corrected HTML, a side-by-side before and after of the changes, and the remaining warnings with their source lines.
- **Project / files**: load a folder, individual files or a project ZIP. Everything is read **in the browser**; only
  the text of the files to analyze is sent to the API. No local path is typed or sent to the server.
- **Only HTML components are modified**: `.html`, `.htm`, `.xhtml`, `.cshtml`, `.vbhtml`, `.razor`, `.aspx`, `.ascx`,
  `.master`, `.vue`, `.svelte`. Inside them, `<script>` and `<style>` bodies are never changed.
- **Markup inside scripts is corrected too, and only the markup**:
  - Angular (and Vue / AngularJS) component templates in a `template:` string of `.ts` and `.js` files;
  - React JSX in `.jsx`, `.tsx` and `.js` / `.mjs` / `.cjs` files, including JSX nested in `{…}` expressions
    (`{items.map(i => <li>…</li>)}`, `{ready && <img …/>}`).

  The code around it never changes: other strings, comments, regular expressions and the JavaScript inside `{…}`
  expressions are kept byte for byte, a value written as an expression (`title={t('save')}`) is never copied, a
  template with `${…}` interpolation is left as is, and a file the scanner cannot read with certainty is left
  unchanged. Attributes added inside a double-quoted string use single quotes.
- **Everything else is report-only**: `.cs`, Markdown and other text that contains markup (and HTML in plain code
  strings such as `innerHTML = "…"`) are analyzed and their warnings listed, but their bytes never change. Binary files
  are kept byte-for-byte.
- **Project structure**: a folder tree that marks every modified file, folder counts, "Show only modified files",
  and Previous/Next modified navigation.
- **Before and after**: side-by-side line diff of each modified file with the changed text marked.
- **Downloads**: the complete project ZIP (corrected files replaced, every other file unchanged) or only the modified
  files in their folders.
- **Runtime URL**: analyzes the rendered DOM of a page with an installed Chrome, Edge or Chromium.
  The browser runs headless and is driven over the DevTools protocol. If a page keeps loading (ads, trackers), loading
  is stopped after 20 seconds and the DOM rendered so far is analyzed; after 45 seconds in total the API answers 504.
  The captured DOM is shown with the same side-by-side before and after as the other tabs.
- **Exports**: JSON and SARIF 2.1.0.

Automated results help find and fix defects; they do not by themselves establish Section 508 / WCAG conformance,
which also needs manual and assistive-technology review.

## 2. How to run it

**Requirements:** .NET SDK 10, Node.js 22.22.3+ or 24.15+, the ASP.NET Core development certificate
(`dotnet dev-certs https --trust`). Runtime URL analysis also needs Chrome, Edge or Chromium installed.

### 2.1 Visual Studio

Open `AccessibilityAnalyzer.slnx`, set **AccessibilityAnalyzer.Server** as the startup project and press **F5**
(profile `https`). SpaProxy starts the Angular dev server at `https://localhost:49230`; the API listens on
`https://localhost:7037` and `http://localhost:5206`. `npm start` runs `ng serve --ssl`; its `prestart` script
(`aspnetcore-https.js`) exports the development certificate to the git-ignored `.aspnetcore-https/` folder.
`src/accessibilityanalyzer.client/src/proxy.conf.js` forwards `/api` and `/health` to the server. Do not start a
second Angular process while SpaProxy is active.

### 2.2 CLI

```cmd
cd src\accessibilityanalyzer.client && npm install && cd ..\..
dotnet run --project src\AccessibilityAnalyzer.Server --launch-profile https
```

## 3. Project structure

```text
AccessibilityAnalyzer.slnx                               ← solution (.slnx format)
├─ src/
│  ├─ AccessibilityAnalyzer.Domain/                      ← rule catalog, SimpleHtmlParser, HtmlContentDetector, SafeFixEngine,
│  │                                                       EmbeddedMarkupFixer, DynamicHtmlAnalyzer (no framework dependencies)
│  ├─ AccessibilityAnalyzer.Application/                 ← AccessibilityAnalysisService, contracts, ports (IRuntimeDomProvider, ISarifReportWriter)
│  │  └─ DependencyInjection.cs                          ← AddApplication
│  ├─ AccessibilityAnalyzer.Infrastructure/              ← implementations of the ports
│  │  ├─ Browser/                                        ← ChromiumRuntimeDomProvider + CdpConnection (headless browser over DevTools)
│  │  ├─ Reporting/                                      ← SarifReportWriter
│  │  └─ DependencyInjection.cs                          ← AddInfrastructure
│  ├─ AccessibilityAnalyzer.Server/                      ← ASP.NET Core (Minimal APIs) + SpaProxy
│  │  ├─ Endpoints/AccessibilityEndpoints.cs             ← every /api endpoint
│  │  ├─ Contracts/, Validation/                         ← request records and limits
│  │  └─ Program.cs
│  ├─ accessibilityanalyzer.client/                      ← Angular 22 (.esproj, aspnetcore-https.js, proxy.conf.js)
│  │  ├─ src/app/components/                             ← analyzer-shell, paste-html, project-scan, source-diff, runtime-scan, findings tables
│  │  ├─ src/app/core/                                   ← files (classifier, folder filter, ZIP), diff (side-by-side before and after), API client
│  │  └─ tests/                                          ← frontend tests
│  └─ tests/
│     ├─ AccessibilityAnalyzer.Domain.UnitTests/         ← rules, parser, classification, safe fixes, templates and JSX in scripts
│     ├─ AccessibilityAnalyzer.Application.UnitTests/    ← which files are corrected or report-only
│     ├─ AccessibilityAnalyzer.Infrastructure.IntegrationTests/ ← SARIF, the real headless browser
│     └─ AccessibilityAnalyzer.Server.IntegrationTests/  ← the endpoints over HTTP
├─ samples/                                              ← sample markup to try in Paste HTML
├─ deploy/github/ci.yml                                  ← CI workflow template
├─ docs/                                                 ← Word and PDF copies of this README
└─ scripts/                                              ← build-and-test.cmd (every test on Windows), smoke-test.ps1
```

## 4. Safe corrections

A correction is applied only when the meaning already exists in the markup; nothing is invented.

| Rule | Correction |
|---|---|
| `BTN-NAME-001` | A button with no accessible name gets `aria-label` from its existing `title` (icon-only buttons included) |
| `IMG-ALT-001` | An image without `alt` gets the text of its existing `title` |
| `IFRAME-TITLE-001` | A frame gets `title` from its existing `aria-label` |
| `DOC-TITLE-001` | A missing or empty `<title>` is taken from the page's single non-empty `<h1>` |
| `DOC-LANG-001` | `lang` is added only when you give the primary language (it is never guessed) |

Every other finding (form labels, headings, tables, links, media, positive `tabindex`, duplicate ids, dynamic
markup that needs runtime review…) is reported with the exact source lines and what to validate. The **Rule catalog**
tab lists all rules with their WCAG criterion and level.

## 5. Loading large projects

- **Folders to skip** (Project / files): checkboxes for `node_modules`, `bin`, `obj`, `.git`, `.vs`, `.vscode`,
  `.idea`, `dist`, `build`, `.angular`, `coverage`, `TestResults`, `.terraform`, `.next`, `__pycache__`, `.venv`
  (checked by default) and `out`, `packages` (unchecked: they are often source folders), plus a field for other
  folder names separated by commas. Files inside skipped folders are not loaded at all: not analyzed, not shown and
  not included in the downloads. The choice is remembered in the browser.
- **Binaries are never read as text**: known binary types are decided by extension; every other file is confirmed as
  text from its first 8 KiB (no NUL bytes, few control characters). Certificates, model weights (`.onnx`) and tool
  executables without an extension stay out of the analysis and are kept byte-for-byte.
- **Limits**: files above 6 MiB are not read (the API analyzes sources up to 2 MiB characters); up to 10,000 text
  sources per project; sources are sent in batches of 40 files / 5 MiB.

## 6. Configuration reference

The analyzer needs no configuration to run. These values decide how it behaves:

| Setting | Default | Notes |
|---|---|---|
| `ACCESSIBILITY_ANALYZER_BROWSER` (environment variable) | empty | Full path of the Chromium-based browser for Runtime URL. Empty = Edge, then Chrome, from their standard install folders (Chromium/Chrome on Linux). |
| Runtime load time | 20 s | After this, loading is stopped and the DOM rendered so far is analyzed. |
| Runtime total time | 45 s | After this, the browser is closed and the API answers 504. |
| Sources per request | 2,000 | `/api/analyze-sources` and `/api/sarif-sources`; more answers 400. |
| Characters per source | 2 MiB | Larger sources answer 400. |
| Folders to skip | see [section 5](#5-loading-large-projects) | Chosen in the browser and remembered there (`localStorage`). |

## 7. API

| Method and path | Body | Answer |
|---|---|---|
| `GET /health` | — | `{ status: "ok", product: "Accessibility Analyzer" }` |
| `GET /api/rules` | — | Rule catalog |
| `POST /api/analyze-html` | `{ html, sourceName }` | Findings of one HTML text |
| `POST /api/analyze-and-fix-html` | `{ html, sourceName, preferredLanguage }` | Findings, corrected content and applied fixes |
| `POST /api/apply-safe-fixes` | `{ html, preferredLanguage }` | `{ html, appliedFixes }` |
| `POST /api/analyze-sources` | `{ sources: [{ name, content }] }` | One result per source (corrected only for HTML components) |
| `POST /api/sarif-sources` | `{ sources: [...] }` | SARIF 2.1.0 |
| `POST /api/runtime` | `{ url }` | Rendered-DOM analysis (absolute http/https URLs only); 504 if the page does not render in 45 s, 503 if it cannot be loaded |

Limits: 2,000 sources per request and 2 MiB characters per source (larger requests answer 400).

## 8. Tests

`scripts\build-and-test.cmd` runs everything on Windows: the build (warnings as errors), the .NET tests, the Angular
tests, the Angular production build and the real-server smoke test.

| Project | Command | Covers |
|---|---|---|
| `AccessibilityAnalyzer.Domain.UnitTests` (74) | `dotnet test AccessibilityAnalyzer.slnx` | Rules, parser, source classification, safe fixes, templates and JSX inside scripts, dynamic markup. |
| `AccessibilityAnalyzer.Application.UnitTests` (18) | same | Which files are corrected and which are report-only, line numbers of the warnings. |
| `AccessibilityAnalyzer.Infrastructure.IntegrationTests` (6) | same | SARIF; the real headless browser: a page whose image never answers is captured after the load time, a hanging browser ends at the total time, a cancelled request is not reported as a timeout. |
| `AccessibilityAnalyzer.Server.IntegrationTests` (9) | same | The endpoints over HTTP: runtime before and after, 504 / 503 / 400 answers, enums as text, SARIF media type, unknown `/api` paths answer 404; request validation. |
| `accessibilityanalyzer.client` (16) | `npm test` | `tests/frontend-contract-tests.mjs`: the file classifier and folder filter against real `File` objects, plus the UI, ZIP, diff and runtime contracts. |
| Real server (20 checks) | `scripts\smoke-test.ps1` | Publishes and runs the real server over HTTPS: health, the Angular app, every API, Angular templates and JSX corrected without touching the code, report-only code strings, untouched `<script>` bodies, SARIF, and the request limits. |

## 9. Production and deployment

- `dotnet publish src/AccessibilityAnalyzer.Server -c Release -o publish` builds the Angular app and copies it into
  `wwwroot`; the result runs with `dotnet AccessibilityAnalyzer.Server.dll`.
- `deploy/github/ci.yml` is a GitHub Actions template: copy it to `.github/workflows/`.
- The tool has no authentication: run it on a developer machine or an internal network, not on the public Internet.
