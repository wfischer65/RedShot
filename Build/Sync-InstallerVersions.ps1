[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$ProjectPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $ProjectPath -PathType Leaf)) {
    throw "Projektdatei wurde nicht gefunden: $ProjectPath"
}

$content = Get-Content -LiteralPath $ProjectPath -Raw
[xml]$xml = $content
$versionNode = $xml.SelectSingleNode('/Project/PropertyGroup/Version')

if ($null -eq $versionNode) {
    $propertyGroup = $xml.SelectSingleNode('/Project/PropertyGroup')
    if ($null -eq $propertyGroup) {
        throw "In '$ProjectPath' wurde keine PropertyGroup gefunden."
    }

    $version = '1.0.0'

    # Nur beim ersten Lauf: zentrale Version in der csproj anlegen,
    # ohne die komplette Projektdatei durch XmlDocument neu zu formatieren.
    $pattern = '(?s)(<PropertyGroup(?:\s[^>]*)?>)'
    $replacement = '$1' + [Environment]::NewLine + '    <Version>' + $version + '</Version>'
    $updated = [regex]::Replace($content, $pattern, $replacement, 1)
    Set-Content -LiteralPath $ProjectPath -Value $updated -Encoding UTF8

    Write-Host "Zentrale Version wurde angelegt: $version"
}
else {
    $version = $versionNode.InnerText.Trim()
}

if ($version -notmatch '^\d+\.\d+\.\d+$') {
    throw "Die zentrale Version '$version' muss das Format Major.Minor.Build besitzen."
}

Write-Host ''
Write-Host 'Versionssynchronisierung:'
Write-Host "  Projektversion: $version"
Write-Host "  MSI-Version:     $version"
Write-Host ''
Write-Host 'Versionssynchronisierung erfolgreich abgeschlossen.'
