# Accessibility Analyzer

.NET 10 + Angular 22 developer tool that checks the HTML components of any project against **Section 508 / WCAG**
rules, applies only the corrections that can be made safely, and shows the project structure with a before and after
of exactly the files it modified. In script files only the markup (component templates and JSX) is corrected; code,
C#, Markdown and every other file are never modified: their warnings are reported.

The results can be exported as JSON and SARIF. They help find and fix defects; conformance also needs a manual review.

## How to use it

You can use it in three ways:

- **Paste HTML:** paste any HTML or component template and get the corrected HTML, a before and after, and the
  warnings that remain.
- **Load a project:** a folder, single files or a ZIP. It is read in the browser, and you download the whole project
  corrected or only the files that changed.
- **Analyze a URL:** it opens the page with Chrome, Edge or Chromium and checks the page as it is rendered.

## How to run it

**Requirements:** .NET SDK 10, Node.js 22.22.3+ or 24.15+, and the ASP.NET Core development certificate
(`dotnet dev-certs https --trust`). Analyzing a URL also needs Chrome, Edge or Chromium installed.

Open `AccessibilityAnalyzer.slnx` in Visual Studio, set **AccessibilityAnalyzer.Server** as the startup project and
press **F5**. The application opens at `https://localhost:49230`.

From the command line:

```cmd
cd src\accessibilityanalyzer.client && npm install && cd ..\..
dotnet run --project src\AccessibilityAnalyzer.Server --launch-profile https
```

## Configuration

It needs no configuration to run. To analyze a URL with a browser other than Edge or Chrome, set the
`ACCESSIBILITY_ANALYZER_BROWSER` environment variable to the full path of a Chromium-based browser.

## Tests

```cmd
scripts\build-and-test.cmd
```

It runs the build, the .NET tests, the Angular tests, the Angular production build and the real-server smoke test.

## License and disclaimer

Licensed under the [Apache License 2.0](LICENSE): anyone may use, modify and distribute it, including commercially.

It is provided as an example, "as is", without warranty of any kind. Review and test it before using it in
production; the authors are not liable for any damage that results from its use.
