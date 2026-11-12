# Date version rules shared by packaging and Actions (Beijing time).
function Read-GamePadT9Version([string]$Name) {
    $match = [regex]::Match($Name, '^(?<date>[0-9]{4}\.[0-9]{2}\.[0-9]{2})(?:p(?<patch>[1-9][0-9]*))?$')
    if (!$match.Success) { return }
    $date = [datetime]::MinValue
    if (![datetime]::TryParseExact($match.Groups['date'].Value, 'yyyy.MM.dd',
        [Globalization.CultureInfo]::InvariantCulture,
        [Globalization.DateTimeStyles]::None, [ref]$date)) { return }
    $patch = 0L
    if ($match.Groups['patch'].Success -and ![long]::TryParse($match.Groups['patch'].Value, [ref]$patch)) {
        throw 'Version patch number is too large.'
    }
    [pscustomobject]@{
        Name = $Name
        Date = $date
        Patch = $patch
        Numeric = '{0}.{1}.{2}.{3}' -f $date.Year, $date.Month, $date.Day, $patch
    }
}

function Get-GamePadT9BuildVersion([string]$Version) {
    $parsed = Read-GamePadT9Version $Version
    if (!$parsed) { throw 'Package version must be a valid yyyy.MM.dd or yyyy.MM.ddpN date.' }
    if ($parsed.Patch -gt 65535) { throw 'Windows version patch must not exceed 65535.' }
    return $parsed
}
