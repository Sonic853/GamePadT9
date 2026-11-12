param(
    [string]$ReleaseDate = ([DateTimeOffset]::UtcNow.ToOffset([TimeSpan]::FromHours(8)).ToString(
        'yyyy.MM.dd', [Globalization.CultureInfo]::InvariantCulture)),
    [string]$Repository,
    [switch]$ReleaseRequested
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'version.ps1')
$base = Get-GamePadT9BuildVersion $ReleaseDate
if ($base.Patch -ne 0) { throw 'Expected a Beijing build date without a patch suffix.' }
$version = $base.Name
$previous = ''
if ($ReleaseRequested) {
    if ($Repository -notmatch '^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$') { throw 'Missing or invalid repository.' }
    $tags = @(gh api "repos/$Repository/tags?per_page=100" --paginate --jq '.[].name')
    if ($LASTEXITCODE -ne 0) { throw 'Could not read repository tags.' }
    # Draft releases may reserve a version even before their tag exists.
    $releaseTags = @(gh api "repos/$Repository/releases?per_page=100" --paginate --jq '.[].tag_name')
    if ($LASTEXITCODE -ne 0) { throw 'Could not read existing releases.' }
    $versions = @($tags | ForEach-Object { Read-GamePadT9Version $_ })
    $occupied = @($versions) + @($releaseTags | ForEach-Object { Read-GamePadT9Version $_ })
    $sameDay = @($occupied | Where-Object { $_.Date -eq $base.Date } | Sort-Object Patch -Descending)
    if ($sameDay.Count) {
        if ($sameDay[0].Patch -ge 65535) { throw 'Windows version patch number is exhausted.' }
        $version += 'p' + ($sameDay[0].Patch + 1).ToString([Globalization.CultureInfo]::InvariantCulture)
    }
    $latest = $versions | Sort-Object Date, Patch -Descending | Select-Object -First 1
    if ($latest) { $previous = $latest.Name }
}
# The workflow holds one release concurrency group from allocation to publication.
if ($env:GITHUB_OUTPUT) {
    "version=$version`nprevious=$previous" | Out-File -FilePath $env:GITHUB_OUTPUT -Encoding utf8 -Append
}
Write-Output "Selected GamePad T9 build version $version"
