# AccessibilityAnalyzer smoke test (Windows PowerShell 5+ / pwsh): publishes the app, runs the real server (Kestrel over
# HTTPS) and sends real HTTP requests to every API.
# Requirements: .NET 10 SDK, trusted ASP.NET Core development certificate (dotnet dev-certs https --trust).
# Usage (from the repo root):  powershell -ExecutionPolicy Bypass -File scripts\smoke-test.ps1
$ErrorActionPreference = 'Continue'   # native stderr must not abort the script on Windows PowerShell 5
$repo = Join-Path $PSScriptRoot '..' | Resolve-Path
$work = Join-Path $repo 'artifacts\smoke'
$pub  = Join-Path $work 'publish'
New-Item -ItemType Directory -Force -Path $work | Out-Null
$fail = 0
function Check($name, $cond) { if ($cond) { "PASS $name" } else { "FAIL $name"; $script:fail++ } }

"==== publish ===="
dotnet publish (Join-Path $repo 'src\AccessibilityAnalyzer.Server') -c Release -o $pub -nologo -v:minimal 2>&1 | Select-Object -Last 3
Check 'publish copied Angular into wwwroot' (Test-Path (Join-Path $pub 'wwwroot\index.html'))

"==== Real server (HTTPS) ===="
dotnet dev-certs https --check --trust 2>&1 | Out-Null
Check 'ASP.NET Core development certificate is trusted' ($LASTEXITCODE -eq 0)
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$log = Join-Path $work 'server.log'
$p = Start-Process dotnet -ArgumentList "`"$(Join-Path $pub 'AccessibilityAnalyzer.Server.dll')`" --urls https://localhost:5397" -WorkingDirectory $pub -PassThru -WindowStyle Hidden -RedirectStandardOutput $log -RedirectStandardError (Join-Path $work 'server.err')
try {
  $base = 'https://localhost:5397'
  Add-Type -AssemblyName System.Net.Http
  $client = New-Object System.Net.Http.HttpClient
  $client.Timeout = [TimeSpan]::FromSeconds(60)
  function Send($method, $path, $json = $null) {
    $req = New-Object System.Net.Http.HttpRequestMessage([System.Net.Http.HttpMethod]::new($method), "$base$path")
    if ($null -ne $json) { $req.Content = New-Object System.Net.Http.StringContent($json, [Text.Encoding]::UTF8, 'application/json') }
    $res = $client.SendAsync($req).GetAwaiter().GetResult()
    $text = $res.Content.ReadAsStringAsync().GetAwaiter().GetResult()
    $body = $null; try { $body = $text | ConvertFrom-Json } catch { }
    return [pscustomobject]@{ Status = [int]$res.StatusCode; Text = $text; Body = $body }
  }
  function Json($value) { $value | ConvertTo-Json -Depth 6 -Compress }
  $ready = $false
  for ($i = 0; $i -lt 60 -and -not $ready; $i++) { try { $ready = (Send 'GET' '/health').Status -eq 200 } catch { Start-Sleep 1 } }

  $r = Send 'GET' '/health';      Check '/health ok' ($r.Status -eq 200 -and $r.Body.status -eq 'ok')
  $r = Send 'GET' '/';            Check 'index.html served' ($r.Status -eq 200 -and $r.Text -match 'app-root')
  $r = Send 'GET' '/project';     Check 'SPA route served' ($r.Status -eq 200 -and $r.Text -match 'app-root')
  $r = Send 'GET' '/api/rules';   Check 'rule catalog' ($r.Status -eq 200 -and @($r.Body).Count -gt 10 -and (@($r.Body).ruleId -contains 'BTN-NAME-001'))

  $r = Send 'POST' '/api/analyze-html' (Json @{ html = '<button></button>'; sourceName = 'smoke.html' })
  Check 'analyze-html: unnamed button is an error' ($r.Status -eq 200 -and ($r.Body.findings | Where-Object { $_.ruleId -eq 'BTN-NAME-001' -and $_.status -eq 'Error' }))

  $r = Send 'POST' '/api/analyze-and-fix-html' (Json @{ html = '<html><body><button title="Save"><i class="icon"></i></button></body></html>'; sourceName = 'pasted.html' })
  Check 'analyze-and-fix-html: aria-label copied from title' ($r.Status -eq 200 -and $r.Body.correctedContent -match 'aria-label="Save"' -and @($r.Body.appliedFixes).Count -ge 1)

  $r = Send 'POST' '/api/apply-safe-fixes' (Json @{ html = '<button title="Save"></button>' })
  Check 'apply-safe-fixes' ($r.Status -eq 200 -and $r.Body.html -match 'aria-label="Save"')

  $sources = @(
    @{ name = 'app/Pages/Index.razor'; content = '<button title="Save"></button>' },
    @{ name = 'app/src/page.ts'; content = 'const t = `<button title="Save"></button>`;' },
    @{ name = 'app/src/toolbar.component.ts'; content = "@Component({ selector: 'app-toolbar', template: ``<button title=`"Print`"><i class=`"icon`"></i></button>`` })`nexport class Toolbar { html = '<img title=`"x`">'; }" },
    @{ name = 'app/src/Logo.jsx'; content = "export const Logo = () => <img src={logo} title=`"Company`" />;`nconst s = `"<img title='code'>`";" },
    @{ name = 'app/wwwroot/index.html'; content = '<html><body><script>var s = "<button title=\"x\"></button>";</script><button title="Go"></button></body></html>' }
  )
  $r = Send 'POST' '/api/analyze-sources' (Json @{ sources = $sources })
  $byPath = @{}; foreach ($x in @($r.Body)) { $byPath[$x.artifact.path] = $x }
  Check 'analyze-sources answers every file' ($r.Status -eq 200 -and $byPath.Count -eq 5)
  $ng = $byPath['app/src/toolbar.component.ts']
  Check 'Angular inline template corrected, class code unchanged' ($ng.correctedContent -eq $sources[2].content.Replace('<button title="Print">', '<button aria-label="Print" title="Print">') -and @($ng.appliedFixes).Count -eq 1)
  $jsx = $byPath['app/src/Logo.jsx']
  Check 'React JSX corrected, JavaScript string unchanged' ($jsx.correctedContent -eq $sources[3].content.Replace('<img src={logo}', '<img alt="Company" src={logo}') -and @($jsx.appliedFixes).Count -eq 1)
  Check 'HTML component (.razor) corrected' ($byPath['app/Pages/Index.razor'].correctedContent -match 'aria-label="Save"')
  Check 'HTML in a plain code string (.ts) is report-only: never modified' ($byPath['app/src/page.ts'].correctedContent -eq $sources[1].content -and @($byPath['app/src/page.ts'].appliedFixes).Count -eq 0)
  $html = $byPath['app/wwwroot/index.html'].correctedContent
  Check '<script> body untouched, markup corrected' ($html -match [regex]::Escape('var s = "<button title=\"x\"></button>";') -and $html -match 'aria-label="Go"')

  $r = Send 'POST' '/api/sarif-sources' (Json @{ sources = @($sources[0]) })
  Check 'SARIF 2.1.0 export' ($r.Status -eq 200 -and $r.Body.version -eq '2.1.0')

  $many = 1..2001 | ForEach-Object { @{ name = "f$_.html"; content = '' } }
  $r = Send 'POST' '/api/analyze-sources' (Json @{ sources = $many })
  Check 'more than 2,000 sources in one request refused (400)' ($r.Status -eq 400)
  $r = Send 'POST' '/api/analyze-sources' (Json @{ sources = @(@{ name = 'big.html'; content = ('x' * (2 * 1024 * 1024 + 1)) }) })
  Check 'source over 2 MiB refused (400)' ($r.Status -eq 400)
  $r = Send 'POST' '/api/runtime' (Json @{ url = 'file:///C:/Windows/win.ini' })
  Check 'runtime refuses non-http URLs (400)' ($r.Status -eq 400)
}
finally {
  Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
}
Check 'server logged no unhandled exception' (-not ((Get-Content $log -Raw) -match 'Unhandled exception|fail: '))
"==== $fail failure(s) ===="
exit $fail
