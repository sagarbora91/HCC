<#
.SYNOPSIS
  Runs ONE heavy command at a time across every agent and worktree on this PC (dotnet build / dotnet test /
  npm test / npm run build / Gradle). Ported from ETP (E:\ETP\Code\Worktrees\_tools\Invoke-Serialized.ps1, DEC-154).
  Parallel agents may write code concurrently but must run builds and tests through this script.

.EXAMPLE
  pwsh -NoProfile -File "E:\Projects\HCC\worktrees\acc1\tools\Invoke-Serialized.ps1" -WorkingDirectory "E:\Projects\HCC\worktrees\acc1-chart\03 Prototype" -Label acc1-chart -Command "npm test"
  pwsh -NoProfile -File "E:\Projects\HCC\worktrees\acc1\tools\Invoke-Serialized.ps1" -WorkingDirectory "E:\Projects\HCC\worktrees\acc1-chart\08 Production" -Label acc1-chart -Command "dotnet test tests/Hcc.Accounting.Tests -maxcpucount:1 --logger trx"

  Exit code 75 means the lock was still busy after -WaitMinutes: wait a little and run the same call again.
  Any other exit code is the command's own. Long commands go in a .ps1 in your lane's scratch folder and are called by path.
#>
param(
    [Parameter(Mandatory)][string]$Command,
    [string]$WorkingDirectory = (Get-Location).Path,
    [int]$WaitMinutes = 8,
    [string]$Label = ''
)

$ErrorActionPreference = 'Stop'
$toolsDir = 'E:\Projects\HCC\worktrees\_tools'
if (-not (Test-Path $toolsDir)) { New-Item -ItemType Directory -Path $toolsDir | Out-Null }
# Priority pause: while _tools\PAUSE-LANE-BUILDS exists, callers whose -Label is not listed in it wait here (without taking
# the lock) so a priority job (the wave gate, an emulator run) gets the lock next. Delete the file to resume.
$pauseFile = Join-Path $toolsDir 'PAUSE-LANE-BUILDS'
$pauseDeadline = (Get-Date).AddMinutes($WaitMinutes)
while ((Test-Path $pauseFile) -and -not (@(Get-Content $pauseFile -ErrorAction SilentlyContinue) -contains $Label)) {
    if ((Get-Date) -gt $pauseDeadline) { Write-Output "LOCK BUSY: lane builds are paused for a priority job. Retry the same call."; exit 75 }
    Start-Sleep -Seconds 15
}
$mutex = New-Object System.Threading.Mutex($false, 'Global\HccHeavyJobLock')
$acquired = $false
try {
    try { $acquired = $mutex.WaitOne([TimeSpan]::FromMinutes($WaitMinutes)) }
    catch [System.Threading.AbandonedMutexException] { $acquired = $true }
    if (-not $acquired) {
        Write-Output "LOCK BUSY: another heavy job held the lock for $WaitMinutes minutes. Retry the same call."
        exit 75
    }
    $log = Join-Path $toolsDir 'heavy-jobs.log'
    $start = Get-Date
    Add-Content -Path $log -Value ("{0:u} START [{1}] {2} :: {3}" -f $start, $Label, $WorkingDirectory, $Command) -ErrorAction SilentlyContinue
    # Work PC toolchain (CLAUDE.md): Node 25 at E:\Tools\nodejs must come before the v24 in E:\Program Files\nodejs;
    # Android builds need JDK 21; pwsh 7 at E:\Tools\PowerShell\7.
    $env:PATH = 'E:\Tools\nodejs;E:\Tools\PowerShell\7;C:\Program Files\dotnet;' + $env:PATH
    $env:JAVA_HOME = 'C:\Program Files\Microsoft\jdk-21.0.12.101-hotspot'
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    $env:MSBUILDDISABLENODEREUSE = '1'
    $env:DOTNET_CLI_USE_MSBUILD_SERVER = '0'
    $env:PYTHON_BASIC_REPL = '1'
    # Load control: the job and every process it starts inherit at most 8 logical CPUs and below-normal priority,
    # so the agents writing code and the owner's own work stay responsive.
    $self = [System.Diagnostics.Process]::GetCurrentProcess()
    $cpus = [Math]::Min(8, [Environment]::ProcessorCount)
    $self.ProcessorAffinity = [IntPtr]([Math]::Pow(2, $cpus) - 1)
    $self.PriorityClass = 'BelowNormal'
    $env:DOTNET_PROCESSOR_COUNT = "$cpus"
    Push-Location $WorkingDirectory
    try {
        & pwsh -NoProfile -Command $Command
        $code = $LASTEXITCODE
    }
    finally { Pop-Location }
    Add-Content -Path $log -Value ("{0:u} END   [{1}] exit {2} after {3:n0}s" -f (Get-Date), $Label, $code, ((Get-Date) - $start).TotalSeconds) -ErrorAction SilentlyContinue
    # Cool-down while still holding the lock, so heavy jobs never run back to back.
    Start-Sleep -Seconds 20
    exit $code
}
finally {
    if ($acquired) { $mutex.ReleaseMutex() }
    $mutex.Dispose()
}
