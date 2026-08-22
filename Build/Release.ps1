[CmdletBinding()]
param(
    [ValidateSet('Debug','Release')]
    [string]$Configuration = 'Release',

    [string]$Runtime = 'win-x64',

    [bool]$SelfContained = $true,

    [switch]$KeepPublish
)

# Interne Version des Build-Skripts (unabhaengig von der RedShot-Programmversion)
$ReleaseScriptVersion = '1.4.1'

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if ($PSVersionTable.PSVersion.Major -lt 7) {
    throw "Dieser Build benoetigt PowerShell 7 oder neuer. Bitte mit pwsh.exe starten. Aktuell: $($PSVersionTable.PSVersion)"
}

Write-Host "PowerShell: $($PSVersionTable.PSVersion) ($($PSVersionTable.PSEdition))"

$BuildDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$Root = Split-Path -Parent $BuildDir
$MsiDir = Join-Path $Root 'MSI'
$SetupProject = Join-Path $MsiDir 'RedShot.Setup.wixproj'
$GeneratedWxs = Join-Path $MsiDir 'PublishedFiles.wxs'
$Artifacts = Join-Path $Root 'artifacts'
$PublishDir = Join-Path $Artifacts "publish\$Runtime"
$ReleaseRoot = Join-Path $Root 'Release'
$ReleaseSolution = Join-Path $Root 'RedShot.Release.slnx'
$VersionSyncScript = Join-Path $BuildDir 'Sync-InstallerVersions.ps1'
$BuildLogPath = Join-Path $BuildDir 'BuildRelease.log'
$TranscriptActive = $false


function Start-BuildTranscript([switch]$Append) {
    if ($script:TranscriptActive) { return }

    if ($Append) {
        Start-Transcript -LiteralPath $BuildLogPath -Append | Out-Null
    }
    else {
        Start-Transcript -LiteralPath $BuildLogPath | Out-Null
    }

    $script:TranscriptActive = $true
}

function Stop-BuildTranscript {
    if (-not $script:TranscriptActive) { return }

    Stop-Transcript | Out-Null
    $script:TranscriptActive = $false
}

function Write-Step([string]$Text) {
    Write-Host ''
    Write-Host ('=' * 60)
    Write-Host $Text
    Write-Host ('=' * 60)
}

function Escape-Xml([string]$Value) {
    return [System.Security.SecurityElement]::Escape($Value)
}

function Get-RedShotProject {
    $projects = Get-ChildItem -Path $Root -Recurse -Filter *.csproj -File |
        Where-Object {
            $_.FullName -notmatch '[\\/](bin|obj|RedShot\.Setup)[\\/]'
        }

    $preferred = $projects | Where-Object { $_.Name -ieq 'RedShot.csproj' } | Select-Object -First 1
    if ($preferred) { return $preferred }

    if ($projects.Count -eq 1) { return $projects[0] }

    if ($projects.Count -eq 0) {
        throw "Kein WPF-Projekt gefunden. Erwartet wird RedShot.csproj unterhalb von: $Root"
    }

    $names = ($projects.FullName -join [Environment]::NewLine)
    throw "Mehrere C#-Projekte gefunden, aber kein eindeutiges RedShot.csproj:`n$names"
}

function Get-ProjectMetadata([System.IO.FileInfo]$Project) {
    [xml]$xml = Get-Content -LiteralPath $Project.FullName -Raw

    $assemblyNameNode = $xml.SelectSingleNode('/Project/PropertyGroup/AssemblyName')
    $versionNode = $xml.SelectSingleNode('/Project/PropertyGroup/Version')

    $assemblyName = if ($null -ne $assemblyNameNode -and -not [string]::IsNullOrWhiteSpace($assemblyNameNode.InnerText)) {
        $assemblyNameNode.InnerText.Trim()
    }
    else {
        [IO.Path]::GetFileNameWithoutExtension($Project.Name)
    }

    if ($null -eq $versionNode -or [string]::IsNullOrWhiteSpace($versionNode.InnerText)) {
        throw "In '$($Project.FullName)' wurde kein <Version>-Element gefunden."
    }

    $version = $versionNode.InnerText.Trim()
    if ($version -notmatch '^\d+\.\d+\.\d+$') {
        throw "Die zentrale Version '$version' muss das Format Major.Minor.Build besitzen."
    }

    [pscustomobject]@{
        AssemblyName = $assemblyName
        MainExe = "$assemblyName.exe"
        ProductVersion = $version
    }
}

function Ensure-RuntimeIdentifier([System.IO.FileInfo]$Project, [string]$RuntimeIdentifier) {
    [xml]$xml = Get-Content -LiteralPath $Project.FullName -Raw

    $runtimeNode = $xml.SelectSingleNode('/Project/PropertyGroup/RuntimeIdentifiers')
    if ($null -eq $runtimeNode) {
        $propertyGroup = $xml.SelectSingleNode('/Project/PropertyGroup')
        if ($null -eq $propertyGroup) {
            throw "In '$($Project.FullName)' wurde keine PropertyGroup gefunden."
        }

        $runtimeNode = $xml.CreateElement('RuntimeIdentifiers')
        $runtimeNode.InnerText = $RuntimeIdentifier
        [void]$propertyGroup.AppendChild($runtimeNode)

        $xml.Save($Project.FullName)
        Write-Host "RuntimeIdentifiers angelegt: $RuntimeIdentifier"
        return
    }

    $items = @(
        $runtimeNode.InnerText -split ';' |
            ForEach-Object { $_.Trim() } |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
    )

    if ($items -notcontains $RuntimeIdentifier) {
        $items += $RuntimeIdentifier
        $runtimeNode.InnerText = ($items -join ';')
        $xml.Save($Project.FullName)
        Write-Host "RuntimeIdentifiers erweitert: $($runtimeNode.InnerText)"
    }
    else {
        Write-Host "RuntimeIdentifiers: $($runtimeNode.InnerText)"
    }
}

function New-PublishedFilesWxs([string]$SourceRoot, [string]$DestinationFile) {
    $files = Get-ChildItem -LiteralPath $SourceRoot -File -Recurse | Sort-Object FullName
    if (-not $files) { throw "Publish-Verzeichnis ist leer: $SourceRoot" }

    $dirMap = @{}
    $dirMap[''] = 'INSTALLFOLDER'

    $relativeDirs = $files | ForEach-Object {
        $rel = [IO.Path]::GetRelativePath($SourceRoot, $_.DirectoryName)
        if ($rel -eq '.') { '' } else { $rel }
    } | Sort-Object -Unique

    $allDirs = New-Object System.Collections.Generic.HashSet[string]
    foreach ($d in $relativeDirs) {
        if ([string]::IsNullOrWhiteSpace($d)) { continue }
        $parts = $d -split '[\\/]'
        $current = ''
        foreach ($part in $parts) {
            $current = if ($current) { Join-Path $current $part } else { $part }
            [void]$allDirs.Add($current)
        }
    }

    foreach ($d in ($allDirs | Sort-Object { ($_ -split '[\\/]').Count }, { $_ })) {
        $bytes = [Text.Encoding]::UTF8.GetBytes($d.ToLowerInvariant())
        $sha = [Security.Cryptography.SHA1]::Create()
        try { $hash = [BitConverter]::ToString($sha.ComputeHash($bytes)).Replace('-','').Substring(0,16) }
        finally { $sha.Dispose() }
        $dirMap[$d] = "dir_$hash"
    }

    $sb = [Text.StringBuilder]::new()
    [void]$sb.AppendLine('<Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">')
    [void]$sb.AppendLine('  <Fragment>')
    [void]$sb.AppendLine('    <DirectoryRef Id="INSTALLFOLDER">')

    function Add-Directories([string]$ParentRel, [int]$Indent) {
        $children = $allDirs | Where-Object {
            $parent = Split-Path $_ -Parent
            if ($parent -eq '.') { $parent = '' }
            $parent -eq $ParentRel
        } | Sort-Object
        foreach ($child in $children) {
            $name = Split-Path $child -Leaf
            $spaces = ' ' * $Indent
            [void]$sb.AppendLine(('{0}<Directory Id="{1}" Name="{2}">' -f $spaces, $dirMap[$child], (Escape-Xml $name)))
            Add-Directories $child ($Indent + 2)
            [void]$sb.AppendLine("$spaces</Directory>")
        }
    }

    Add-Directories '' 6
    [void]$sb.AppendLine('    </DirectoryRef>')
    [void]$sb.AppendLine('  </Fragment>')
    [void]$sb.AppendLine('')
    [void]$sb.AppendLine('  <Fragment>')
    [void]$sb.AppendLine('    <ComponentGroup Id="PublishedFiles">')

    foreach ($f in $files) {
        $rel = [IO.Path]::GetRelativePath($SourceRoot, $f.FullName)
        $relDir = [IO.Path]::GetDirectoryName($rel)
        if ([string]::IsNullOrWhiteSpace($relDir)) { $relDir = '' }
        $dirId = $dirMap[$relDir]

        $bytes = [Text.Encoding]::UTF8.GetBytes($rel.ToLowerInvariant())
        $sha = [Security.Cryptography.SHA1]::Create()
        try { $hash = [BitConverter]::ToString($sha.ComputeHash($bytes)).Replace('-','') }
        finally { $sha.Dispose() }

        $componentId = 'cmp_' + $hash.Substring(0,20)
        $fileId = 'fil_' + $hash.Substring(0,20)
        $source = Escape-Xml $f.FullName

        [void]$sb.AppendLine(('      <Component Id="{0}" Directory="{1}" Guid="*">' -f $componentId, $dirId))
        [void]$sb.AppendLine(('        <File Id="{0}" Source="{1}" KeyPath="yes" />' -f $fileId, $source))
        [void]$sb.AppendLine('      </Component>')
    }

    [void]$sb.AppendLine('    </ComponentGroup>')
    [void]$sb.AppendLine('  </Fragment>')
    [void]$sb.AppendLine('</Wix>')

    [IO.File]::WriteAllText($DestinationFile, $sb.ToString(), [Text.UTF8Encoding]::new($false))
}

# Increment-Version.ps1 legt das Log fuer jeden Release-Lauf neu an.
# Release.ps1 haengt seinen Teil daran an, damit auch die Versionserhoehung
# im Build-Log enthalten bleibt.
Start-BuildTranscript -Append
Write-Host "RedShot Release.ps1 V$ReleaseScriptVersion"
Write-Host "PowerShell $($PSVersionTable.PSVersion) ($($PSVersionTable.PSEdition))"
Write-Host "Build-Log: $BuildLogPath"
Write-Host ""

Write-Step '1. RedShot-Projekt ermitteln'
$AppProject = Get-RedShotProject
Write-Host "Projekt: $($AppProject.FullName)"

Write-Step '2. Version synchronisieren'
if (-not (Test-Path -LiteralPath $VersionSyncScript -PathType Leaf)) {
    throw "Versionsskript wurde nicht gefunden: $VersionSyncScript"
}

& powershell.exe `
    -NoLogo `
    -NoProfile `
    -ExecutionPolicy Bypass `
    -File $VersionSyncScript `
    -ProjectPath $AppProject.FullName
if ($LASTEXITCODE -ne 0) { throw 'Sync-InstallerVersions.ps1 ist fehlgeschlagen.' }

$Meta = Get-ProjectMetadata $AppProject
Write-Host "Zentrale Version: $($Meta.ProductVersion)"
Write-Host "MSI-Version:      $($Meta.ProductVersion)"
Write-Host "Hauptdatei:       $($Meta.MainExe)"

$ReleaseDir = Join-Path $ReleaseRoot $Meta.ProductVersion
New-Item -ItemType Directory -Path $ReleaseDir -Force | Out-Null
Write-Host "Release-Ordner:   $ReleaseDir"

Write-Step '3. Alte Build-Ausgaben bereinigen'
if (Test-Path $PublishDir) { Remove-Item $PublishDir -Recurse -Force }
if (Test-Path $GeneratedWxs) { Remove-Item $GeneratedWxs -Force }
New-Item -ItemType Directory -Path $PublishDir -Force | Out-Null

Write-Step '4. WPF wiederherstellen und veröffentlichen'
Ensure-RuntimeIdentifier -Project $AppProject -RuntimeIdentifier $Runtime
$selfContainedText = if ($SelfContained) { 'true' } else { 'false' }

Write-Host "Restore fuer Runtime: $Runtime"
& dotnet restore $AppProject.FullName `
    -r $Runtime `
    --force
if ($LASTEXITCODE -ne 0) { throw "dotnet restore fehlgeschlagen ($LASTEXITCODE)." }

Write-Host ''
Write-Host "Publish fuer Runtime: $Runtime"
& dotnet publish $AppProject.FullName `
    -c $Configuration `
    -r $Runtime `
    --self-contained $selfContainedText `
    --no-restore `
    -o $PublishDir `
    /p:Version=$($Meta.ProductVersion) `
    /p:PublishSingleFile=false
if ($LASTEXITCODE -ne 0) { throw "dotnet publish fehlgeschlagen ($LASTEXITCODE)." }

$mainExePath = Join-Path $PublishDir $Meta.MainExe
if (-not (Test-Path $mainExePath)) {
    throw "Erwartete Hauptdatei fehlt nach Publish: $mainExePath"
}
Write-Host "Publish erfolgreich: $PublishDir"

Write-Step '5. MSI-Dateiliste erzeugen'
New-PublishedFilesWxs -SourceRoot $PublishDir -DestinationFile $GeneratedWxs
Write-Host "Erzeugt: $GeneratedWxs"

Write-Step '6. Release-Projektmappe erzeugen'
if (Test-Path $ReleaseSolution) { Remove-Item $ReleaseSolution -Force }
Push-Location $Root
try {
    & dotnet new sln --name RedShot.Release --format slnx --force | Out-Host
    if ($LASTEXITCODE -ne 0) { throw 'RedShot.Release.slnx konnte nicht erzeugt werden.' }

    & dotnet sln $ReleaseSolution add $AppProject.FullName $SetupProject | Out-Host
    if ($LASTEXITCODE -ne 0) { throw 'Projekte konnten nicht zur Release-Projektmappe hinzugefügt werden.' }
}
finally { Pop-Location }

Write-Host "Release-Projektmappe: $ReleaseSolution"
Write-Host '  WPF enthalten:      True'
Write-Host '  MSI-Setup enthalten: True'
Write-Host '  UWP enthalten:      False'

Write-Step '7. MSI bauen'
& dotnet build $SetupProject `
    -c $Configuration `
    -p:ProductVersion=$($Meta.ProductVersion) `
    -p:MainExe=$($Meta.MainExe)
if ($LASTEXITCODE -ne 0) { throw "MSI-Build fehlgeschlagen ($LASTEXITCODE)." }

$builtMsi = Get-ChildItem -Path $MsiDir -Filter 'RedShot.msi' -File -Recurse |
    Where-Object {
        $_.FullName -match '[\\/](bin|obj)[\\/]'
    } |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1

if (-not $builtMsi) {
    throw "MSI wurde gebaut, konnte aber unter '$MsiDir\bin' oder '$MsiDir\obj' nicht gefunden werden."
}

Write-Host "Gebautes MSI:    $($builtMsi.FullName)"

$finalName = 'RedShot.msi'
$finalMsi = Join-Path $ReleaseDir $finalName
Copy-Item $builtMsi.FullName $finalMsi -Force

Write-Step '8. Build-Log abschliessen'
Write-Host "Build-Log wird fuer Sources.zip geschlossen."
Stop-BuildTranscript

$releaseLog = Join-Path $ReleaseDir 'BuildRelease.log'
Copy-Item -LiteralPath $BuildLogPath -Destination $releaseLog -Force
Write-Host "Build-Log:       $releaseLog"

Write-Step '9. Sources.zip erzeugen'

$sourceZip = Join-Path $ReleaseDir 'Sources.zip'
if (Test-Path -LiteralPath $sourceZip) {
    Remove-Item -LiteralPath $sourceZip -Force
}

# Vollstaendiger, reproduzierbarer Quellstand. Nur erzeugte/temporäre
# Verzeichnisse werden ausgeschlossen. Damit sind auch Build-, MSI-,
# Resource-, Interop- und sonstige Projektdateien enthalten.
$excludeTopLevel = @(
    '.git',
    '.vs',
    'bin',
    'obj',
    'artifacts',
    'Release'
)

$sourceFiles = Get-ChildItem -LiteralPath $Root -File -Recurse | Where-Object {
    $relative = [IO.Path]::GetRelativePath($Root, $_.FullName)
    $topLevel = ($relative -split '[\\/]')[0]
    $excludeTopLevel -notcontains $topLevel -and
    $_.FullName -notmatch '[\\/](bin|obj)[\\/]'
}

if (-not $sourceFiles) {
    throw 'Keine Dateien fuer Sources.zip gefunden.'
}

$stagingDir = Join-Path ([IO.Path]::GetTempPath()) ("RedShot-Sources-" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stagingDir -Force | Out-Null

try {
    foreach ($file in $sourceFiles) {
        $relative = [IO.Path]::GetRelativePath($Root, $file.FullName)
        $target = Join-Path $stagingDir $relative
        $targetDir = Split-Path -Parent $target
        if (-not (Test-Path -LiteralPath $targetDir)) {
            New-Item -ItemType Directory -Path $targetDir -Force | Out-Null
        }
        Copy-Item -LiteralPath $file.FullName -Destination $target -Force
    }

    Compress-Archive -Path (Join-Path $stagingDir '*') -DestinationPath $sourceZip -CompressionLevel Optimal -Force
}
finally {
    if (Test-Path -LiteralPath $stagingDir) {
        Remove-Item -LiteralPath $stagingDir -Recurse -Force
    }
}

Write-Host "Sources:         $sourceZip"
Write-Host "Dateien:         $($sourceFiles.Count)"
Write-Host ''

Write-Step '10. Git Commit und Push'

if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
    throw 'git wurde nicht gefunden.'
}

$gitDir = Join-Path $Root '.git'
if (-not (Test-Path -LiteralPath $gitDir)) {
    throw "Kein Git-Repository gefunden: $Root"
}

Push-Location $Root
try {
    & git add -A
    if ($LASTEXITCODE -ne 0) {
        throw "git add fehlgeschlagen ($LASTEXITCODE)."
    }

    $changes = & git status --porcelain
    if ($LASTEXITCODE -ne 0) {
        throw "git status fehlgeschlagen ($LASTEXITCODE)."
    }

    if ($changes) {
        Write-Host ''
        $gitNote = Read-Host 'Git-Note (optional)'

        $commitMessage = "Release $($Meta.ProductVersion)"
        if (-not [string]::IsNullOrWhiteSpace($gitNote)) {
            $commitMessage += " - $($gitNote.Trim())"
        }

        Write-Host "Commit:          $commitMessage"

        & git commit -m $commitMessage
        if ($LASTEXITCODE -ne 0) {
            throw "git commit fehlgeschlagen ($LASTEXITCODE)."
        }
    }
    else {
        Write-Host 'Keine Aenderungen fuer einen Commit vorhanden.'
    }

    Write-Host 'Push:            origin main'
    & git push origin main
    if ($LASTEXITCODE -ne 0) {
        throw "git push fehlgeschlagen ($LASTEXITCODE)."
    }
}
finally {
    Pop-Location
}

Write-Host 'Git erfolgreich abgeschlossen.'
Write-Host ''

Write-Step 'Build erfolgreich'
Write-Host "Release-Ordner:  $ReleaseDir"
Write-Host "MSI:             $finalMsi"
Write-Host "Sources:         $sourceZip"
Write-Host "Build-Log:       $releaseLog"
Write-Host "Release-Solution: $ReleaseSolution"
Write-Host "Publish:         $PublishDir"

if (-not $KeepPublish) {
    Write-Host ''
    Write-Host 'Hinweis: Publish bleibt absichtlich unter artifacts\publish erhalten,'
    Write-Host 'damit sich der Inhalt des MSI leicht kontrollieren lässt.'
}

