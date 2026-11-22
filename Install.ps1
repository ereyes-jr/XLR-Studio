$ErrorActionPreference = 'Stop'

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$installDir = Join-Path $env:LOCALAPPDATA 'Programs\Project XLR'
$installedExe = Join-Path $installDir 'ProjectXLR.exe'

Write-Host '============================================================' -ForegroundColor Cyan
Write-Host '  Project XLR Installer' -ForegroundColor Cyan
Write-Host '============================================================' -ForegroundColor Cyan
Write-Host ''

# 1. Check for the official TC-Helicon GoXLR Audio Driver
function Test-GoXlrDriver {
    $fallbackDll = 'C:\Program Files\TC-Helicon\GoXLR_Audio_Driver\W10_x64\goxlr_audioapi_x64.dll'
    if (Test-Path $fallbackDll) {
        return $true
    }

    $clsidKey = 'CLSID\{024D0372-641F-4B7B-8140-F4DFE458C982}\InprocServer32'
    try {
        $subKey = [Microsoft.Win32.Registry]::ClassesRoot.OpenSubKey($clsidKey)
        if ($null -ne $subKey) {
            $dllPath = $subKey.GetValue('')
            $subKey.Close()
            if (-not [string]::IsNullOrWhiteSpace($dllPath) -and (Test-Path $dllPath)) {
                return $true
            }
        }
    }
    catch {
        # Ignore registry lookup failures and fall through to warning
    }
    return $false
}

Write-Host '[1/5] Checking for TC-Helicon GoXLR Audio Driver...'
if (Test-GoXlrDriver) {
    Write-Host '  Found official TC-Helicon GoXLR driver.' -ForegroundColor Green
}
else {
    Write-Warning 'TC-Helicon GoXLR Audio Driver (goxlr_audioapi_x64.dll) was not detected.'
    Write-Host '  Project XLR requires the official TC-Helicon GoXLR driver to communicate with your hardware.' -ForegroundColor Yellow
    Write-Host '  Download and install the TC-Helicon GoXLR drivers from TC-Helicon before connecting your device.' -ForegroundColor Yellow
}
Write-Host ''

# 2. Stop conflicting running processes
Write-Host '[2/5] Stopping any running GoXLR App or Project XLR instances...'
foreach ($procName in @('GoXLR App', 'ProjectXLR')) {
    Get-Process -Name $procName -ErrorAction SilentlyContinue | ForEach-Object {
        try {
            $_.Kill()
            $_.WaitForExit(5000) | Out-Null
            Write-Host "  Stopped $procName (PID $($_.Id))."
        }
        catch {
            Write-Warning "  Could not stop $procName (PID $($_.Id)): $_"
        }
    }
}
Write-Host ''

# 3. Locate ProjectXLR.exe (release bundle layout or source checkout layout)
Write-Host '[3/5] Locating Project XLR binaries...'
$releaseBundleExe = Join-Path $scriptDir 'ProjectXLR.exe'
$sourceBuildDir = Join-Path $scriptDir 'src\ProjectXLR\bin\Release\net9.0-windows'
$sourceBuildExe = Join-Path $sourceBuildDir 'ProjectXLR.exe'
$sourceCsproj = Join-Path $scriptDir 'src\ProjectXLR\ProjectXLR.csproj'

$payloadDir = $null
$isReleaseBundle = $false

if (Test-Path $releaseBundleExe) {
    $payloadDir = $scriptDir
    $isReleaseBundle = $true
    Write-Host "  Found release binary at: $releaseBundleExe"
}
elseif (Test-Path $sourceBuildExe) {
    $payloadDir = $sourceBuildDir
    Write-Host "  Found Release build at: $sourceBuildExe"
}
elseif (Test-Path $sourceCsproj) {
    $dotnetCmd = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($null -ne $dotnetCmd) {
        $buildChoice = Read-Host 'ProjectXLR.exe is not built yet. Build Release now? (Y/n)'
        if ([string]::IsNullOrWhiteSpace($buildChoice) -or $buildChoice -match '^[Yy]') {
            Write-Host '  Building Release configuration...'
            & dotnet build $sourceCsproj -c Release
            if ($LASTEXITCODE -ne 0 -or -not (Test-Path $sourceBuildExe)) {
                Write-Error 'Release build failed. Please fix build errors and re-run Install.bat.'
                exit 1
            }
            $payloadDir = $sourceBuildDir
        }
        else {
            Write-Error 'Cannot install without ProjectXLR.exe. Build Release or download a release zip first.'
            exit 1
        }
    }
    else {
        Write-Error 'ProjectXLR.exe was not found and the .NET SDK is not installed. Download a release zip from GitHub Releases or install the .NET 9 SDK.'
        exit 1
    }
}
else {
    Write-Error 'Could not locate ProjectXLR.exe or source project files.'
    exit 1
}
Write-Host ''

# 4. Copy files to %LOCALAPPDATA%\Programs\Project XLR\
Write-Host "[4/5] Installing to $installDir..."
New-Item -ItemType Directory -Force -Path $installDir | Out-Null

if ($isReleaseBundle) {
    Copy-Item -Path $releaseBundleExe -Destination $installedExe -Force
    foreach ($ext in @('*.dll', '*.deps.json', '*.runtimeconfig.json')) {
        Get-ChildItem -Path $payloadDir -Filter $ext -File -ErrorAction SilentlyContinue | ForEach-Object {
            Copy-Item -Path $_.FullName -Destination $installDir -Force
        }
    }
    $assetsSrc = Join-Path $payloadDir 'Assets'
    if (Test-Path $assetsSrc) {
        Copy-Item -Path $assetsSrc -Destination (Join-Path $installDir 'Assets') -Recurse -Force
    }
}
else {
    Get-ChildItem -Path $payloadDir -Force | Where-Object {
        $_.Name -ne 'ui_audit' -and $_.Extension -ne '.log'
    } | ForEach-Object {
        $dest = Join-Path $installDir $_.Name
        if ($_.PSIsContainer) {
            Copy-Item -Path $_.FullName -Destination $dest -Recurse -Force
        }
        else {
            Copy-Item -Path $_.FullName -Destination $dest -Force
        }
    }
}

foreach ($docFile in @('LICENSE', 'README.md', 'FAQ.md')) {
    $docPath = Join-Path $scriptDir $docFile
    if (Test-Path $docPath) {
        Copy-Item -Path $docPath -Destination (Join-Path $installDir $docFile) -Force
    }
}

$setupDfnScript = Join-Path $scriptDir 'tools\Setup-DeepFilterNet.bat'
if (Test-Path $setupDfnScript) {
    $installToolsDir = Join-Path $installDir 'tools'
    New-Item -ItemType Directory -Force -Path $installToolsDir | Out-Null
    Copy-Item -Path $setupDfnScript -Destination (Join-Path $installToolsDir 'Setup-DeepFilterNet.bat') -Force
}

# Create Start Menu and Desktop shortcuts
$wsh = New-Object -ComObject WScript.Shell

$startMenuShortcut = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\Project XLR.lnk'
$scStart = $wsh.CreateShortcut($startMenuShortcut)
$scStart.TargetPath = $installedExe
$scStart.WorkingDirectory = $installDir
$scStart.IconLocation = "$installedExe,0"
$scStart.Description = 'Project XLR - GoXLR Control Software'
$scStart.Save()

$desktopShortcut = Join-Path $env:USERPROFILE 'Desktop\Project XLR.lnk'
$scDesktop = $wsh.CreateShortcut($desktopShortcut)
$scDesktop.TargetPath = $installedExe
$scDesktop.WorkingDirectory = $installDir
$scDesktop.IconLocation = "$installedExe,0"
$scDesktop.Description = 'Project XLR - GoXLR Control Software'
$scDesktop.Save()

Write-Host '  Installed executable and created Start Menu & Desktop shortcuts.' -ForegroundColor Green
Write-Host ''

# 5. Optional DeepFilterNet 3 AI Noise Suppression setup
Write-Host '[5/5] Optional: DeepFilterNet 3 AI Noise Suppression'
$localEqApo = Join-Path $scriptDir 'tools\EqualizerAPO'
$localAltDenoiser = Join-Path $scriptDir 'tools\Alt-Denoiser'

$dfnChoice = Read-Host 'Would you like to set up DeepFilterNet 3 AI noise removal on Chat Mic now? (y/N)'
if ($dfnChoice -match '^[Yy]') {
    if ((Test-Path $localEqApo) -and (Test-Path $localAltDenoiser) -and (Test-Path $setupDfnScript)) {
        Write-Host '  Running local DeepFilterNet 3 setup script...'
        & cmd.exe /c "`"$setupDfnScript`""
    }
    else {
        Write-Host ''
        Write-Host '  DeepFilterNet 3 requires two third-party components (not bundled in GitHub source/releases):' -ForegroundColor Yellow
        Write-Host '    1. Equalizer APO (x64): https://sourceforge.net/projects/equalizerapo/'
        Write-Host '    2. Alt-Denoiser (Windows): https://github.com/Altinus/Alt-Denoiser/releases'
        Write-Host ''
        Write-Host '  Setup steps:'
        Write-Host '    - Install Equalizer APO and enable it on Capture device "Chat Mic (TC-Helicon GoXLR Mini)".'
        Write-Host '    - Copy "Alt Denoiser.dll" to "C:\Program Files\EqualizerAPO\VSTPlugins\".'
        Write-Host '    - Or place both downloads into the tools\ folder and re-run tools\Setup-DeepFilterNet.bat.'
        Write-Host ''

        $wingetCmd = Get-Command winget -ErrorAction SilentlyContinue
        if ($null -ne $wingetCmd) {
            $wgChoice = Read-Host 'Try installing Equalizer APO via winget? (y/N)'
            if ($wgChoice -match '^[Yy]') {
                & winget install --id EqualizerAPO.EqualizerAPO
            }
        }

        $openChoice = Read-Host 'Open the Equalizer APO and Alt-Denoiser download pages in your browser? (y/N)'
        if ($openChoice -match '^[Yy]') {
            Start-Process 'https://sourceforge.net/projects/equalizerapo/'
            Start-Process 'https://github.com/Altinus/Alt-Denoiser/releases'
        }
    }
}
Write-Host ''

Write-Host 'Installation complete!' -ForegroundColor Green
$launchChoice = Read-Host 'Launch Project XLR now? (Y/n)'
if ([string]::IsNullOrWhiteSpace($launchChoice) -or $launchChoice -match '^[Yy]') {
    Start-Process -FilePath $installedExe -WorkingDirectory $installDir
}
