[CmdletBinding()]
param(
    [string]$ProjectPath
)

$ScriptVersion = '1.1.1'

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if ($PSVersionTable.PSVersion.Major -lt 7) {
    throw "Dieses Skript benoetigt PowerShell 7 oder neuer."
}

Write-Host "RedShot Increment-Version.ps1 V$ScriptVersion"

$BuildDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$Root = Split-Path -Parent $BuildDir
$BuildLogPath = Join-Path $BuildDir 'BuildRelease.log'

$logLines = @(
    "RedShot Increment-Version.ps1 V$ScriptVersion",
    "Start: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss.fff')",
    ""
)
[System.IO.File]::WriteAllLines($BuildLogPath, $logLines, [System.Text.UTF8Encoding]::new($false))


if ([string]::IsNullOrWhiteSpace($ProjectPath)) {
    $ProjectPath = Join-Path $Root 'RedShot.csproj'
}

if (-not (Test-Path -LiteralPath $ProjectPath -PathType Leaf)) {
    throw "RedShot.csproj wurde nicht gefunden: $ProjectPath"
}

$project = [System.Xml.XmlDocument]::new()
$project.PreserveWhitespace = $true
$project.Load($ProjectPath)

$versionNode = $project.SelectSingleNode('/Project/PropertyGroup/Version')

if ($null -eq $versionNode -or [string]::IsNullOrWhiteSpace($versionNode.InnerText)) {
    throw "In '$ProjectPath' wurde kein <Version>-Element gefunden."
}

$oldVersion = $versionNode.InnerText.Trim()

if ($oldVersion -notmatch '^(\d+)\.(\d+)\.(\d+)$') {
    throw "Die Version '$oldVersion' muss das Format Major.Minor.Build besitzen."
}

$major = [int]$Matches[1]
$minor = [int]$Matches[2]
$build = [int]$Matches[3] + 1

$newVersion = "$major.$minor.$build"
$versionNode.InnerText = $newVersion

# Preserve the existing XML structure as far as XmlDocument allows.
$settings = [System.Xml.XmlWriterSettings]::new()
$settings.Indent = $true
$settings.IndentChars = '  '
$settings.NewLineChars = "`r`n"
$settings.NewLineHandling = [System.Xml.NewLineHandling]::Replace
$settings.Encoding = [System.Text.UTF8Encoding]::new($false)
$settings.OmitXmlDeclaration = $project.FirstChild -isnot [System.Xml.XmlDeclaration]

$writer = [System.Xml.XmlWriter]::Create($ProjectPath, $settings)
try {
    $project.Save($writer)
}
finally {
    $writer.Dispose()
}

Write-Host ''
Write-Host 'Version erhoeht:'
Write-Host "  Alt: $oldVersion"
Write-Host "  Neu: $newVersion"
Write-Host ''

$logText = @(
    "Version erhoeht:",
    "  Alt: $oldVersion",
    "  Neu: $newVersion",
    ""
) -join [Environment]::NewLine

$logText += [Environment]::NewLine
[System.IO.File]::AppendAllText(
    $BuildLogPath,
    $logText,
    [System.Text.UTF8Encoding]::new($false))
