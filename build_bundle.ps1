# Builds pap_hideout.bundle headlessly and copies it into the BUTTERSTATE plugin folder.
# Requires an activated Unity licence (sign in to Unity Hub once and activate Personal).
$ErrorActionPreference = 'Stop'

$unity   = 'E:\Unity\2022.3.43f1\Editor\Unity.exe'
$here    = $PSScriptRoot
$project = Join-Path $here 'UnityProject'
$logDir  = Join-Path $here 'logs'
New-Item -ItemType Directory -Force $logDir | Out-Null

function Invoke-Unity([string[]]$UnityArgs, [string]$LogName) {
    $log = Join-Path $logDir $LogName
    $p = Start-Process -FilePath $unity -ArgumentList ($UnityArgs + @('-logFile', "`"$log`"")) -Wait -PassThru -NoNewWindow
    if ($p.ExitCode -ne 0) {
        Get-Content $log -Tail 40
        throw "Unity exited with $($p.ExitCode). Full log: $log"
    }
}

if (-not (Test-Path (Join-Path $project 'Assets'))) {
    Write-Host 'Creating Unity project...'
    Invoke-Unity @('-batchmode', '-quit', '-createProject', "`"$project`"") 'create_project.log'
}

Write-Host 'Syncing UnityAssets -> project Assets...'
Copy-Item (Join-Path $here 'UnityAssets\PaP') (Join-Path $project 'Assets') -Recurse -Force

Write-Host 'Importing BO3 assets and building bundle...'
Invoke-Unity @('-batchmode', '-quit', '-projectPath', "`"$project`"", '-executeMethod', 'PaPBundleTools.RunAllBatch') 'build_bundle.log'

Select-String -Path (Join-Path $logDir 'build_bundle.log') -Pattern '\[PaP\]' | ForEach-Object { $_.Line }
Write-Host 'Done.'
