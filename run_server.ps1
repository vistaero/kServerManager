# run_server.ps1 - Minecraft Server Launcher (PowerShell)
Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $PSCommandPath
Set-Location $scriptDir

$configFile = Join-Path $scriptDir "java_config.json"

# ================================
# Global String Variables
# ================================
$HELP_HEADER = "======================================"
$HELP_TITLE = "   Minecraft Server Launcher"
$HELP_SEPARATOR = "======================================"
$ERROR_NO_JAVA = "[X] No Java installations detected."
$ERROR_INSTALL_JDK = "  Install a JDK before continuing."
$MSG_JAVA_IN_PATH = "JAVA in PATH"
$MSG_DETECTED_JDKS = "Detected the following JDKs:`n"
$MSG_SELECT_JDK = "Select the JDK to use by default: "
$ERROR_INVALID_INPUT = "[X] Invalid input."
$ERROR_OUT_OF_RANGE = "[X] Number out of range."
$MSG_JAVA_SAVED = "[OK] Java saved."
$MSG_JAVA_SELECTED = "[OK] Java selected:"
$ERROR_NO_JAR = "[X] No .jar files found in this folder."
$MSG_DETECTED_JARS = "Detected several .jar files:`n"
$MSG_SELECT_JAR = "Enter the number of the server to use: "
$MSG_RUNNING_SERVER = "[>>] Running {0} ..."
$MSG_SERVER_STOPPED = "[INFO] Server stopped correctly."
$MSG_SYNC_QUESTION = "Do you want to sync with Pi now? (y/n): "
$MSG_SYNCING = "  Syncing..."
$ERROR_SECURITY = "[X] Security: local path points to {0}. Aborting sync."
$MSG_LOCAL = "  Local : {0}"
$MSG_REMOTE = "  Remote: {0}"
$MSG_EXECUTING = "  Executing: {0}"
$ERROR_SYNC_FAILED = "[X] Error syncing files. Code: {0}"
$MSG_SYNC_COMPLETE = "[OK] Sync completed."
$MSG_CLOSING = "  Closing launcher."
$MSG_MEMORY_SAVED = "[OK] Maximum memory saved."
$MSG_SELECT_MEMORY = "Enter maximum memory in GB (example: 8): "
$MSG_NO_VALID_CONFIG = "[INFO] Some settings are missing or invalid. You may need to configure them before starting."
$MSG_PRESS_ENTER_MENU = "Press Enter to return to the menu"
$MSG_MENU_AUTOSTART = "[INFO] Option 1 will run automatically in 5 seconds. Press any key to cancel."
$MSG_MENU_AUTOSTART_GO = "[AUTO] Starting server..."
$MSG_MENU_AUTOSTART_CANCELLED = "[INFO] Automatic start canceled."
$MSG_RETURN_TIMER = "[INFO] Returning to menu in 5 seconds. Press any key to cancel return."
$MSG_RETURN_TIMER_CANCELLED = "[INFO] Auto-return canceled. You can review the log. Press any key to return to menu."

# ================================
# Runtime Config
# ================================
$script:config = [ordered]@{
    JavaPath    = $null
    JarPath     = $null
    MaxMemoryGB = 8
}

function Save-Config {
    $script:config | ConvertTo-Json | Set-Content -Path $configFile -Encoding UTF8
}

function Load-Config {
    if (Test-Path $configFile) {
        try {
            $loaded = Get-Content $configFile -Raw | ConvertFrom-Json

            if ($loaded.JavaPath) {
                $script:config.JavaPath = [string]$loaded.JavaPath
            }
            if ($loaded.JarPath) {
                $script:config.JarPath = [string]$loaded.JarPath
            }
            if ($loaded.MaxMemoryGB) {
                $mem = 0
                if ([int]::TryParse([string]$loaded.MaxMemoryGB, [ref]$mem) -and $mem -gt 0) {
                    $script:config.MaxMemoryGB = $mem
                }
            }
        } catch {
            # Ignore broken config and continue with defaults
        }
    }
}

# ================================
# Detect Java Installations
# ================================
function Find-JavaInstalls {
    $paths = @(
        "$env:ProgramFiles\Java",
        "$env:ProgramFiles\Eclipse Adoptium",
        "$env:ProgramFiles\Adoptium",
        "$env:ProgramFiles\Microsoft",
        "$env:ProgramFiles\Zulu",
        "$env:ProgramFiles\Amazon Corretto",
        "$env:LocalAppData\Programs",
        "$env:USERPROFILE\scoop\apps",
        "$env:APPDATA\ModrinthApp\meta\java_versions"
    )

    $found = @()

    foreach ($base in $paths) {
        if (Test-Path $base) {
            Get-ChildItem -Path $base -Directory -ErrorAction SilentlyContinue | ForEach-Object {
                $javaExe = Join-Path $_.FullName "bin\java.exe"
                if (Test-Path $javaExe) {
                    $found += [PSCustomObject]@{
                        Name = $_.Name
                        Path = $javaExe
                    }
                }
            }
        }
    }

    # Detect java in PATH
    try {
        $pathJava = (Get-Command java.exe -ErrorAction Stop).Source
        $found += [PSCustomObject]@{
            Name = $MSG_JAVA_IN_PATH
            Path = $pathJava
        }
    } catch {}

    return $found | Sort-Object Path -Unique
}

function Read-InstantSelection {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Prompt,
        [Parameter(Mandatory = $true)]
        [int]$MaxChoice
    )

    try {
        Write-Host -NoNewline $Prompt

        while ([Console]::KeyAvailable) {
            $null = [Console]::ReadKey($true)
        }

        $keyInfo = [Console]::ReadKey($true)
        $keyChar = [string]$keyInfo.KeyChar

        if ($keyChar -match '^\d$') {
            Write-Host $keyChar
            $choice = 0

            if ([int]::TryParse($keyChar, [ref]$choice) -and $choice -ge 1 -and $choice -le $MaxChoice) {
                return $choice
            }
        } else {
            Write-Host ""
        }

        return $null
    } catch {
        # Fallback for hosts without direct key read support
        $sel = Read-Host $Prompt
        $choice = 0
        if ([int]::TryParse($sel, [ref]$choice) -and $choice -ge 1 -and $choice -le $MaxChoice) {
            return $choice
        }

        return $null
    }
}

function Select-Java {
    $javaInstalls = Find-JavaInstalls

    if (@($javaInstalls).Count -eq 0) {
        Write-Host $ERROR_NO_JAVA
        Write-Host $ERROR_INSTALL_JDK
        return $false
    }

    Write-Host $MSG_DETECTED_JDKS

    for ($i = 0; $i -lt @($javaInstalls).Count; $i++) {
        Write-Host (" [{0}] {1} -> {2}" -f ($i + 1), $javaInstalls[$i].Name, $javaInstalls[$i].Path)
    }

    ""
    $choice = Read-InstantSelection -Prompt $MSG_SELECT_JDK -MaxChoice @($javaInstalls).Count
    if ($null -eq $choice) {
        Write-Host $ERROR_INVALID_INPUT
        return $false
    }

    $idx = $choice - 1
    if ($idx -lt 0 -or $idx -ge @($javaInstalls).Count) {
        Write-Host $ERROR_OUT_OF_RANGE
        return $false
    }

    $script:config.JavaPath = $javaInstalls[$idx].Path
    Save-Config

    Write-Host $MSG_JAVA_SAVED
    Write-Host ($MSG_JAVA_SELECTED)
    Write-Host (" {0}" -f $script:config.JavaPath)
    return $true
}

# ================================
# Detect Server JAR
# ================================
function Select-Jar {
    $jarFiles = @(Get-ChildItem -Path $scriptDir -Filter '*.jar' -File | Sort-Object Name)

    if ($jarFiles.Count -eq 0) {
        Write-Host $ERROR_NO_JAR
        return $false
    }

    if ($jarFiles.Count -eq 1) {
        $script:config.JarPath = $jarFiles[0].FullName
        Save-Config
        Write-Host ("[OK] Using server jar: {0}" -f $jarFiles[0].Name)
        return $true
    }

    Write-Host $MSG_DETECTED_JARS
    for ($i = 0; $i -lt $jarFiles.Count; $i++) {
        Write-Host ("  [{0}] {1}" -f ($i + 1), $jarFiles[$i].Name)
    }

    ""
    $choice = Read-InstantSelection -Prompt $MSG_SELECT_JAR -MaxChoice $jarFiles.Count
    if ($null -eq $choice) {
        Write-Host $ERROR_INVALID_INPUT
        return $false
    }

    $idx = $choice - 1
    if ($idx -lt 0 -or $idx -ge $jarFiles.Count) {
        Write-Host $ERROR_OUT_OF_RANGE
        return $false
    }

    $script:config.JarPath = $jarFiles[$idx].FullName
    Save-Config

    Write-Host ("[OK] Server jar saved: {0}" -f $jarFiles[$idx].Name)
    return $true
}

# ================================
# Memory
# ================================
function Set-MaxMemory {
    ""
    $sel = Read-Host $MSG_SELECT_MEMORY
    $mem = 0

    if (-not [int]::TryParse($sel, [ref]$mem) -or $mem -lt 1) {
        Write-Host $ERROR_INVALID_INPUT
        return $false
    }

    $script:config.MaxMemoryGB = $mem
    Save-Config
    Write-Host $MSG_MEMORY_SAVED
    Write-Host ("[OK] Max memory set to {0}G" -f $script:config.MaxMemoryGB)
    return $true
}

function Ensure-LaunchReady {
    if (-not $script:config.JavaPath -or -not (Test-Path $script:config.JavaPath)) {
        Write-Host "[INFO] Java not configured or invalid."
        if (-not (Select-Java)) { return $false }
    }

    if (-not $script:config.JarPath -or -not (Test-Path $script:config.JarPath)) {
        Write-Host "[INFO] Server .jar not configured or invalid."
        if (-not (Select-Jar)) { return $false }
    }

    if (-not $script:config.MaxMemoryGB -or $script:config.MaxMemoryGB -lt 1) {
        $script:config.MaxMemoryGB = 8
        Save-Config
    }

    return $true
}

function Wait-CountdownOrKey {
    param(
        [Parameter(Mandatory = $true)]
        [int]$Seconds,
        [Parameter(Mandatory = $true)]
        [string]$Message
    )

    Write-Host $Message

    try {
        # Flush any pending key presses to avoid accidental instant cancel
        while ([Console]::KeyAvailable) {
            $null = [Console]::ReadKey($true)
        }

        for ($remaining = $Seconds; $remaining -gt 0; $remaining--) {
            Write-Host ("  {0}..." -f $remaining)

            for ($i = 0; $i -lt 10; $i++) {
                if ([Console]::KeyAvailable) {
                    $null = [Console]::ReadKey($true)
                    return $false
                }
                Start-Sleep -Milliseconds 100
            }
        }

        return $true
    } catch {
        # Fallback if host does not support direct key reading
        Start-Sleep -Seconds $Seconds
        return $true
    }
}

function Wait-MenuSelectionOrTimeout {
    param(
        [Parameter(Mandatory = $true)]
        [int]$Seconds,
        [Parameter(Mandatory = $true)]
        [string]$Message
    )

    Write-Host $Message

    try {
        # Flush any pending key presses to avoid accidental instant cancel
        while ([Console]::KeyAvailable) {
            $null = [Console]::ReadKey($true)
        }

        for ($remaining = $Seconds; $remaining -gt 0; $remaining--) {
            Write-Host ("  {0}..." -f $remaining)

            for ($i = 0; $i -lt 10; $i++) {
                if ([Console]::KeyAvailable) {
                    $keyInfo = [Console]::ReadKey($true)
                    $keyChar = [string]$keyInfo.KeyChar

                    if ($keyChar -match '^\d$') {
                        Write-Host ("[INPUT] {0}" -f $keyChar)
                        return [PSCustomObject]@{
                            TimedOut  = $false
                            Selection = $keyChar
                        }
                    }

                    return [PSCustomObject]@{
                        TimedOut  = $false
                        Selection = $null
                    }
                }
                Start-Sleep -Milliseconds 100
            }
        }

        return [PSCustomObject]@{
            TimedOut  = $true
            Selection = $null
        }
    } catch {
        # Fallback if host does not support direct key reading
        Start-Sleep -Seconds $Seconds
        return [PSCustomObject]@{
            TimedOut  = $true
            Selection = $null
        }
    }
}

function Wait-AnyKey {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Message
    )

    Write-Host $Message

    try {
        $null = [Console]::ReadKey($true)
    } catch {
        Read-Host "Press Enter to return to the menu" | Out-Null
    }
}

function Start-Server {
    if (-not (Ensure-LaunchReady)) {
        Read-Host "Press Enter to return to the menu"
        return
    }

    Clear-Host
    Write-Host $HELP_HEADER
    Write-Host $HELP_TITLE
    Write-Host $HELP_SEPARATOR
    ""
    Write-Host (" Java : {0}" -f $script:config.JavaPath)
    Write-Host (" Jar  : {0}" -f (Split-Path $script:config.JarPath -Leaf))
    Write-Host (" RAM  : {0}G" -f $script:config.MaxMemoryGB)
    ""
    Write-Host ($MSG_RUNNING_SERVER -f (Split-Path $script:config.JarPath -Leaf))
    Write-Host ""

    $memArg = "{0}G" -f $script:config.MaxMemoryGB
    $javaArgs = @("-Xmx$memArg", "-Xms$memArg", "-jar", $script:config.JarPath, "nogui")

    try {
        $null = Start-Process -FilePath $script:config.JavaPath -ArgumentList $javaArgs -NoNewWindow -Wait -PassThru
    } catch {
        Write-Host ("[X] Failed to start server: {0}" -f $_.Exception.Message)
        Read-Host "Press Enter to return to the menu"
        return
    }

    Write-Host ""
    Write-Host $MSG_SERVER_STOPPED
    Write-Host ""

    $waitDone = Wait-CountdownOrKey -Seconds 5 -Message $MSG_RETURN_TIMER
    if (-not $waitDone) {
        Wait-AnyKey -Message $MSG_RETURN_TIMER_CANCELLED
    }
}

function Sync-With-Pi {
    $resp = Read-Host $MSG_SYNC_QUESTION
    if ($resp -notmatch '^[sSyY]') {
        return
    }

    Write-Host $MSG_SYNCING

    $local = Split-Path -Parent $PSCommandPath

    # Basic safety checks
    if ($local -match '^[A-Za-z]:\\?$' -or $local -match '^[A-Za-z]:\\Windows($|\\)') {
        Write-Host ($ERROR_SECURITY -f $local)
        return
    }

    $remote = 'vistaero@192.168.18.22:/home/vistaero/MinecraftServer'
    $sshKey = Join-Path $env:USERPROFILE '.ssh\id_ed25519'

    Write-Host ($MSG_LOCAL -f $local)
    Write-Host ($MSG_REMOTE -f $remote)
    Write-Host ""

    $source = Join-Path $local '*'
    $scpArgs = @("-i", $sshKey, "-r", $source, $remote)

    Write-Host ($MSG_EXECUTING -f ("scp " + ($scpArgs -join " ")))
    Write-Host ""

    try {
        & scp @scpArgs
        if ($LASTEXITCODE -ne 0) {
            Write-Host ($ERROR_SYNC_FAILED -f $LASTEXITCODE)
        } else {
            Write-Host $MSG_SYNC_COMPLETE
        }
    } catch {
        Write-Host ($ERROR_SYNC_FAILED -f 1)
    }
}

function Show-Menu {
    Clear-Host
    Write-Host $HELP_HEADER
    Write-Host $HELP_TITLE
    Write-Host $HELP_SEPARATOR
    ""
    Write-Host (" Current Java : {0}" -f ($(if ($script:config.JavaPath) { Split-Path $script:config.JavaPath -Leaf } else { "<not set>" })))
    Write-Host (" Current Jar  : {0}" -f ($(if ($script:config.JarPath) { Split-Path $script:config.JarPath -Leaf } else { "<not set>" })))
    Write-Host (" Max Memory   : {0}G" -f $script:config.MaxMemoryGB)
    ""
    Write-Host "1. Start Server"
    Write-Host "2. Change server .jar"
    Write-Host "3. Change Java version"
    Write-Host "4. Change maximum memory"
    Write-Host "5. Sync with Pi"
    Write-Host ""
}

# ================================
# Startup
# ================================
Load-Config

$javaInstalls = Find-JavaInstalls
if (@($javaInstalls).Count -eq 0) {
    Write-Host $ERROR_NO_JAVA
    Write-Host $ERROR_INSTALL_JDK
    exit 1
}

while ($true) {
    Show-Menu

    $menuWaitResult = Wait-MenuSelectionOrTimeout -Seconds 5 -Message $MSG_MENU_AUTOSTART
    if ($menuWaitResult.TimedOut) {
        Write-Host $MSG_MENU_AUTOSTART_GO
        $sel = '1'
    } elseif ($menuWaitResult.Selection) {
        $sel = $menuWaitResult.Selection
    } else {
        Write-Host $MSG_MENU_AUTOSTART_CANCELLED
        $sel = Read-Host "Select an option"
    }

    switch ($sel) {
        '1' {
            Start-Server
        }
        '2' {
            if (-not (Select-Jar)) {
                Read-Host "Press Enter to return to the menu"
            }
        }
        '3' {
            if (-not (Select-Java)) {
                Read-Host "Press Enter to return to the menu"
            }
        }
        '4' {
            if (-not (Set-MaxMemory)) {
                Read-Host "Press Enter to return to the menu"
            }
        }
        '5' {
            Sync-With-Pi
            Read-Host "Press Enter to return to the menu"
        }
        default {
            Write-Host $ERROR_INVALID_INPUT
            Start-Sleep -Seconds 1
        }
    }
}
