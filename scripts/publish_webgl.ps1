# Builds the PoOlympics WebGL player on THIS machine and publishes it as a GitHub Release. Publishing the
# release triggers .github/workflows/webgl.yml, which deploys the release's webgl.zip to the game's Azure
# Static Web App on a GitHub-hosted runner (Unity cannot be licensed in CI).
#
#   pwsh -File scripts\publish_webgl.ps1               # EditMode tests, build, zip, publish release, prune old ones
#   pwsh -File scripts\publish_webgl.ps1 -NoPublish    # tests, build and zip only
#   pwsh -File scripts\publish_webgl.ps1 -SkipBuild    # publish the existing Builds/WebGL (stamp from its index.html)
#
# Close the Unity editor on this project first (a batch-mode Unity cannot open a project that is already open).
# The release is tagged at HEAD, so HEAD must already be pushed to origin. Settings: scripts/webgl.json
# (build method, Static Web App, the /diag checks).
#
# One version stamp: UTC yyMMdd, computed here once. It goes to the player (PO_BUILD_STAMP -> WebGLBuild sets
# PlayerSettings.bundleVersion, so Application.version and the page's {{{ PRODUCT_VERSION }}} carry it), to
# diag.html, and to the release tag.

param(
    [switch] $SkipBuild,
    [switch] $SkipTests,
    [switch] $NoPublish,
    [int] $Keep = 3          # webgl-* releases to keep, the new one included
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root
$cfg = Get-Content (Join-Path $PSScriptRoot 'webgl.json') -Raw | ConvertFrom-Json
$buildDir = Join-Path $root 'Builds\WebGL'
$zip = Join-Path $root 'Builds\webgl.zip'
$now = (Get-Date).ToUniversalTime()
$stamp = $now.ToString('yyMMdd')

$sha = (git rev-parse HEAD).Trim()
if (-not $NoPublish) {
    git fetch --quiet origin
    if (-not (git branch -r --contains $sha)) {
        throw "HEAD $($sha.Substring(0,7)) is not on origin. Push it first: the release is tagged at HEAD."
    }
    if (git status --porcelain --untracked-files=no) {
        Write-Warning 'Uncommitted changes: the build will include them, but the release is tagged at HEAD.'
    }
}

function Find-Unity {
    $want = ((Get-Content 'ProjectSettings\ProjectVersion.txt' | Select-String 'm_EditorVersion:') -split ':\s*')[1].Trim()
    $exe = Join-Path 'C:\Program Files\Unity\Hub\Editor' "$want\Editor\Unity.exe"
    if (-not (Test-Path $exe)) { throw "Unity $want is not installed. Install exactly $want (with the WebGL module) in Unity Hub." }
    return $exe
}

function Invoke-Unity([string[]] $arguments) {
    $p = Start-Process -FilePath $script:unity -PassThru -Wait -NoNewWindow -ArgumentList $arguments
    return $p.ExitCode
}

New-Item -ItemType Directory -Force (Split-Path $buildDir) | Out-Null

if (-not $SkipBuild) {
    $script:unity = Find-Unity

    if (-not $SkipTests) {
        $results = Join-Path $root 'Builds\editmode-results.xml'
        $log = Join-Path $root 'Builds\editmode-tests.log'
        if (Test-Path $results) { Remove-Item $results -Force }
        "EditMode tests; log: $log"
        $code = Invoke-Unity @('-batchmode', '-nographics', '-projectPath', "`"$root`"", '-runTests', '-testPlatform', 'EditMode',
                               '-testResults', "`"$results`"", '-logFile', "`"$log`"")
        if (-not (Test-Path $results)) { Get-Content $log -Tail 40; throw "EditMode tests did not run (exit $code). Log: $log" }
        $run = ([xml](Get-Content $results -Raw)).'test-run'
        "EditMode: $($run.passed) passed, $($run.failed) failed, $($run.skipped) skipped of $($run.total)"
        if ([int]$run.failed -gt 0 -or $code -ne 0) {
            ([xml](Get-Content $results -Raw)).SelectNodes("//test-case[@result='Failed']") | ForEach-Object { "  FAILED $($_.fullname)" }
            throw "EditMode tests failed: nothing was built or published. Results: $results"
        }
    }

    $log = Join-Path $root 'Builds\webgl-build.log'
    "Building WebGL v$stamp ($($cfg.buildMethod)); log: $log"
    $env:PO_BUILD_STAMP = $stamp
    try {
        $code = Invoke-Unity @('-batchmode', '-quit', '-projectPath', "`"$root`"", '-buildTarget', 'WebGL',
                               '-executeMethod', $cfg.buildMethod, '-logFile', "`"$log`"")
    }
    finally { Remove-Item Env:PO_BUILD_STAMP -ErrorAction SilentlyContinue }
    if ($code -ne 0) {
        Get-Content $log -Tail 40
        throw "Unity build failed (exit $code). Full log: $log"
    }
}

$index = Join-Path $buildDir 'index.html'
if (-not (Test-Path $index)) { throw "No WebGL build at $buildDir." }
$page = Get-Content $index -Raw
if ($SkipBuild) {
    $m = [regex]::Match($page, 'class="po-version">v(\d{6})<')
    if (-not $m.Success) { throw "index.html of the existing build carries no vYYMMDD stamp." }
    $stamp = $m.Groups[1].Value
}
elseif ($page -notmatch "v$stamp") { throw "index.html does not carry v$stamp: the build did not take the stamp." }

# Static Web Apps 404s .unityweb without this MIME config.
Copy-Item (Join-Path $PSScriptRoot 'webgl\staticwebapp.config.json') $buildDir -Force

# /diag: the configured checks plus every file of the build bundle, all probed live by the visitor's browser.
$checks = [System.Collections.Generic.List[object]]::new()
foreach ($c in $cfg.diagChecks) { $checks.Add($c) }
foreach ($f in Get-ChildItem (Join-Path $buildDir 'Build') -File | Sort-Object Name) {
    $checks.Add([pscustomobject]@{
        group = 'data'; label = "Build bundle: $($f.Name)"; purpose = 'Part of the Unity player the page loads'
        url = "Build/$($f.Name)"; method = 'HEAD' })
}
$product = [regex]::Match($page, '<title>([^<]+)</title>').Groups[1].Value
$diag = (Get-Content (Join-Path $PSScriptRoot 'webgl\diag.html') -Raw).
    Replace('{{NAME}}', [System.Net.WebUtility]::HtmlEncode($product)).
    Replace('{{VERSION}}', $stamp).
    Replace('{{CHECKS}}', ($checks | ConvertTo-Json -Depth 4 -AsArray -Compress))
[IO.File]::WriteAllText((Join-Path $buildDir 'diag.html'), $diag)

if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $buildDir '*') -DestinationPath $zip -CompressionLevel Optimal
'{0}: {1:N1} MB, v{2}' -f $zip, ((Get-Item $zip).Length / 1MB), $stamp

if ($NoPublish) { 'Built and zipped; not published (-NoPublish).'; return }

$repo = (gh repo view --json nameWithOwner --jq .nameWithOwner).Trim()
$tag = "webgl-$stamp-$($now.ToString('HHmm'))-$($sha.Substring(0,7))"
gh release create $tag $zip -R $repo --target $sha --title "WebGL v$stamp" --latest=false `
    --notes "Local WebGL build v$stamp of $($sha.Substring(0,7)). Publishing this release deploys it to Azure (.github/workflows/webgl.yml)."
if ($LASTEXITCODE -ne 0) { throw "gh release create failed ($LASTEXITCODE)." }
"PUBLISHED $tag  ->  deploy: https://github.com/$repo/actions/workflows/webgl.yml"

# Keep only the newest $Keep webgl-* releases (and their tags).
$old = gh release list -R $repo --limit 100 --json tagName,createdAt |
    ConvertFrom-Json | Where-Object { $_.tagName -like 'webgl-*' } |
    Sort-Object createdAt -Descending | Select-Object -Skip $Keep
foreach ($r in $old) { gh release delete $r.tagName -R $repo --yes --cleanup-tag; "pruned $($r.tagName)" }
