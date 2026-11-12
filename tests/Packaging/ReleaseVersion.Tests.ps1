$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
. (Join-Path $projectRoot 'scripts/version.ps1')
$resolver = Join-Path $projectRoot 'scripts/resolve-release-version.ps1'
$testRoot = Join-Path $projectRoot ('artifacts/release-version-tests-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testRoot -Force | Out-Null
$checks = [Collections.Generic.List[string]]::new()
function Check([bool]$Condition, [string]$Message) {
    if (!$Condition) { throw $Message }
    $checks.Add($Message)
}
function Reject([scriptblock]$Action, [string]$Expected, [string]$Message) {
    $rejected = $false
    try { & $Action | Out-Null }
    catch { $rejected = $_.Exception.Message -like $Expected }
    Check $rejected $Message
}

$base = Get-GamePadT9BuildVersion '2026.11.12'
$patch = Get-GamePadT9BuildVersion '2026.11.12p10'
Check ($base.Numeric -eq '2026.11.12.0' -and $patch.Numeric -eq '2026.11.12.10') 'Date versions map to valid four-part Windows versions.'
Check ((Get-GamePadT9BuildVersion '2028.02.29').Numeric -eq '2028.2.29.0') 'Leap dates are accepted and Windows version components are normalized.'
foreach ($invalid in @('', 'v2026.11.12', '2026.2.03', '2026.02.29', '2026.11.12p0', '2026.11.12p01', '../2026.11.12')) {
    Reject { Get-GamePadT9BuildVersion $invalid } '*valid yyyy.MM.dd*' "Reject invalid package version '$invalid'."
}
Reject { Get-GamePadT9BuildVersion '2026.11.12p65536' } '*must not exceed*' 'Reject Windows version overflow.'
Reject { & (Join-Path $projectRoot 'scripts/package.ps1') -PackageVersion 'not-a-date' } '*valid yyyy.MM.dd*' 'Invalid package versions fail before compiling or staging files.'

# Mock GitHub CLI so these tests never contact GitHub or create a tag/release.
$github = @{ Tags = @(); ReleaseTags = @(); Failure = ''; Calls = [Collections.Generic.List[string]]::new() }
function gh {
    $Arguments = @($args)
    $github.Calls.Add(($Arguments -join ' '))
    Set-Variable -Name LASTEXITCODE -Value 0 -Scope 1
    if ($Arguments[1].Contains('/tags?')) {
        if ($github.Failure -eq 'tags') { Set-Variable -Name LASTEXITCODE -Value 1 -Scope 1; return }
        return $github.Tags
    }
    if ($Arguments[1].Contains('/releases?')) {
        if ($github.Failure -eq 'releases') { Set-Variable -Name LASTEXITCODE -Value 1 -Scope 1; return }
        return $github.ReleaseTags
    }
    throw 'Unexpected GitHub CLI request.'
}
function Resolve-Version([switch]$Publish) {
    Clear-Content -LiteralPath $env:GITHUB_OUTPUT
    & $resolver -ReleaseDate '2026.11.12' -Repository 'owner/repo' -ReleaseRequested:$Publish | Out-Null
    return (Get-Content -LiteralPath $env:GITHUB_OUTPUT -Raw | ConvertFrom-StringData)
}
$savedOutput = $env:GITHUB_OUTPUT
try {
    $env:GITHUB_OUTPUT = Join-Path $testRoot 'github-output.txt'
    New-Item -ItemType File -Path $env:GITHUB_OUTPUT | Out-Null
    $selected = Resolve-Version
    Check ($selected.version -eq '2026.11.12' -and $github.Calls.Count -eq 0) 'Build-only runs use the date without calling GitHub.'
    $selected = Resolve-Version -Publish
    Check ($selected.version -eq '2026.11.12' -and !$selected.previous) 'The first release uses the bare Beijing date.'
    Check (@($github.Calls | Where-Object { $_ -notlike '*--paginate*' }).Count -eq 0) 'Tag and release queries include every API page.'
    $github.Tags = @('v1.0', '2026.11.11p20', '2026.11.12')
    $selected = Resolve-Version -Publish
    Check ($selected.version -eq '2026.11.12p1' -and $selected.previous -eq '2026.11.12') 'A second release on the same date gets p1.'
    $github.Tags += '2026.11.12p2', '2026.11.12p10'
    $github.ReleaseTags = @('2026.11.12p11')
    $selected = Resolve-Version -Publish
    Check ($selected.version -eq '2026.11.12p12' -and $selected.previous -eq '2026.11.12p10') 'Patch ordering is numeric, draft releases reserve names, and changelogs use existing tags.'
    $github.Tags = @('2026.11.11p20', '2026.02.29', 'v1.0')
    $github.ReleaseTags = @()
    $selected = Resolve-Version -Publish
    Check ($selected.version -eq '2026.11.12' -and $selected.previous -eq '2026.11.11p20') 'A new date resets the patch and ignores unrelated or invalid tags.'
    $github.Tags = @('2026.11.12p65535')
    Reject { Resolve-Version -Publish } '*exhausted*' 'Exhausted patch numbers stop publication.'
    $github.Tags = @()
    $github.Failure = 'tags'
    Reject { Resolve-Version -Publish } '*Could not read repository tags*' 'Tag API failures stop allocation instead of reusing a version.'
    $github.Failure = 'releases'
    Reject { Resolve-Version -Publish } '*Could not read existing releases*' 'Release API failures stop allocation instead of ignoring drafts.'
    Reject { & $resolver -ReleaseDate '2026.11.12p1' } '*without a patch suffix*' 'The allocator requires an unpatched date.'
    Reject { & $resolver -ReleaseDate '2026.11.12' -Repository 'invalid' -ReleaseRequested } '*invalid repository*' 'Publication requires a valid repository name.'
} finally { $env:GITHUB_OUTPUT = $savedOutput }
@{ passed = $true; checks = $checks } | ConvertTo-Json -Depth 3
