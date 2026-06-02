# Spotlight Setup and Installer Script
# Installs Spotlight to local user directories and configures startup shortcuts.

$ErrorActionPreference = "Stop"

# Colorful terminal logging helper
function Write-Log {
    param (
        [string]$Message,
        [string]$Level = "Info"
    )
    $colors = @{
        "Info" = "Cyan"
        "Success" = "Green"
        "Warning" = "Yellow"
        "Error" = "Red"
    }
    $color = $colors[$Level]
    if (-not $color) { $color = "White" }
    
    Write-Host "[*] " -NoNewline -ForegroundColor Gray
    Write-Host "$Message" -ForegroundColor $color
}

Write-Log "=========================================" "Info"
Write-Log "        Spotlight Installation Setup      " "Info"
Write-Log "=========================================" "Info"

# 1. Locate publish artifacts
$PublishPath = Join-Path $PSScriptRoot "src\Spotlight.App\bin\Release\net9.0-windows\win-x64\publish"
if (-not (Test-Path $PublishPath)) {
    Write-Log "Publish folder not found at: $PublishPath" "Error"
    Write-Log "Please compile and publish the project in Release configuration first." "Error"
    exit 1
}

# 2. Stop any running instances
Write-Log "Terminating existing instances of Spotlight..." "Info"
try {
    Stop-Process -Name "Spotlight.App" -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 1
    Write-Log "Terminated running instances." "Success"
} catch {
    # Process not running
}

# 3. Define target installation directory
$InstallDir = Join-Path $env:LOCALAPPDATA "Spotlight"
Write-Log "Installing Spotlight to: $InstallDir" "Info"

if (-not (Test-Path $InstallDir)) {
    New-Item -ItemType Directory -Path $InstallDir | Out-Null
    Write-Log "Created installation directory." "Success"
}

# 4. Copy published files to target directory
Write-Log "Copying published files..." "Info"
Copy-Item -Path (Join-Path $PublishPath "*") -Destination $InstallDir -Recurse -Force
Write-Log "Files copied successfully." "Success"

# 4.5 Ensure fd is installed (download and extract fd.exe if not present in the install folder)
$FdExePath = Join-Path $InstallDir "fd.exe"
if (-not (Test-Path $FdExePath)) {
    Write-Log "fd.exe not found in install directory. Downloading official fd binary..." "Info"
    try {
        $FdVersion = "10.2.0" # Stable known version
        $FdUrl = "https://github.com/sharkdp/fd/releases/download/v$FdVersion/fd-v$FdVersion-x86_64-pc-windows-msvc.zip"
        $TempZip = Join-Path $env:TEMP "fd_temp.zip"
        $TempExtract = Join-Path $env:TEMP "fd_temp_extract"

        # Clean old temp files
        if (Test-Path $TempZip) { Remove-Item $TempZip -Force }
        if (Test-Path $TempExtract) { Remove-Item $TempExtract -Recurse -Force }

        # Download zip
        Invoke-WebRequest -Uri $FdUrl -OutFile $TempZip -UseBasicParsing
        
        # Extract files
        Expand-Archive -Path $TempZip -DestinationPath $TempExtract
        
        # Locate fd.exe in the extracted folder and copy to install directory
        $ExtractedFdExe = Get-ChildItem -Path $TempExtract -Filter "fd.exe" -Recurse | Select-Object -First 1
        if ($ExtractedFdExe) {
            Copy-Item -Path $ExtractedFdExe.FullName -Destination $FdExePath -Force
            Write-Log "Successfully installed fd.exe locally to: $FdExePath" "Success"
        } else {
            throw "fd.exe was not found inside the downloaded archive."
        }

        # Cleanup temp
        Remove-Item $TempZip -Force
        Remove-Item $TempExtract -Recurse -Force
    } catch {
        Write-Log "Failed to install fd.exe locally: $_" "Warning"
        Write-Log "Please ensure fd is installed globally on your system (e.g. via 'winget install sharkdp.fd')." "Warning"
    }
} else {
    Write-Log "fd.exe is already installed locally." "Success"
}

# 5. Create Start Menu Shortcut
Write-Log "Creating Start Menu shortcut..." "Info"
$StartMenuPath = [System.IO.Path]::Combine($env:APPDATA, "Microsoft\Windows\Start Menu\Programs")
$ShortcutPath = Join-Path $StartMenuPath "Spotlight.lnk"
$TargetExe = Join-Path $InstallDir "Spotlight.App.exe"

try {
    $WshShell = New-Object -ComObject WScript.Shell
    $Shortcut = $WshShell.CreateShortcut($ShortcutPath)
    $Shortcut.TargetPath = $TargetExe
    $Shortcut.WorkingDirectory = $InstallDir
    $Shortcut.Description = "Fast keyboard-driven search and launcher for Windows"
    $Shortcut.IconLocation = "$TargetExe,0"
    $Shortcut.Save()
    Write-Log "Start Menu shortcut created at: $ShortcutPath" "Success"
} catch {
    Write-Log "Failed to create Start Menu shortcut: $_" "Warning"
}

# 6. Configure Auto-run on Startup (Registry HKCU)
Write-Log "Configuring registry startup keys..." "Info"
$RegistryKeyPath = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run"
$RegistryValueName = "Spotlight"

try {
    # Set the value to run the installed executable
    Set-ItemProperty -Path $RegistryKeyPath -Name $RegistryValueName -Value "`"$TargetExe`""
    Write-Log "Configured Spotlight to run automatically on Windows startup." "Success"
} catch {
    Write-Log "Failed to configure registry startup key: $_" "Warning"
}

# 7. Start the application
Write-Log "Launching Spotlight..." "Info"
try {
    Start-Process -FilePath $TargetExe -WorkingDirectory $InstallDir
    Write-Log "Spotlight is now running in the background." "Success"
} catch {
    Write-Log "Failed to launch installed executable: $_" "Error"
}

Write-Log "=========================================" "Success"
Write-Log "   Spotlight has been successfully setup! " "Success"
Write-Log " Press Alt + Space to open Search.       " "Success"
Write-Log " Press Alt + C to open Clipboard History. " "Success"
Write-Log "=========================================" "Success"
