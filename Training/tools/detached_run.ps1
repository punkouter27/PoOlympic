<#
One training run + its checkpoint watcher, meant to be started OUTSIDE the Claude session's process tree so it survives
the session ending (rs_v1, rs_v4 and rs_v5 all died with their sessions). Launch it through WMI:

  Invoke-CimMethod Win32_Process -MethodName Create -Arguments @{ CurrentDirectory = '<repo>\training';
      CommandLine = 'pwsh -NoProfile -WindowStyle Hidden -File tools\detached_run.ps1 -Body mattbio -Task <id> ...' }

WMI's PATH may not resolve 'pwsh' (Store install -> ReturnValue 9 or a silent exit): use the full
C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe -ExecutionPolicy Bypass, and put -TrainArgs / -WatchArgs in
DOUBLE quotes (-File does not group single-quoted words). Wrap in 'cmd /c ... > runs\<Name>_launcher.log 2>&1' to see
launcher errors (rs_v6, 2026-09-30).

Logs: runs/<Name>.log (training), runs/<Name>_watch.log (watcher). Stop: kill the python processes (preflight.ps1 lists them).
#>
param(
    [Parameter(Mandatory)] [string] $Body,
    [Parameter(Mandatory)] [string] $Task,
    [Parameter(Mandatory)] [string] $Name,          # run name (dir suffix)
    [Parameter(Mandatory)] [string] $Experiment,    # e.g. mattbio_stance
    [string] $LoadRun = '',                         # dir under runs/<Experiment>; empty = train from scratch
    [string] $Checkpoint = '',                      # e.g. model_1050.pt
    [Parameter(Mandatory)] [int] $Iterations,
    [string] $TrainArgs = '',                       # extra train flags, e.g. '--agent.algorithm.entropy-coef 0.0025'
    [string] $WatchPrefix = '',                     # parity/watch_<prefix>.jsonl
    [string] $WatchArgs = ''                        # e.g. '--gates S,2 --its 1150,1350,1549'
)
$ErrorActionPreference = 'Continue'
Set-Location (Split-Path -Parent $PSScriptRoot)
$env:POOLYMPIC_BODY = $Body
$uv = (Get-Command uv -ErrorAction SilentlyContinue).Source
if (-not $uv) { $uv = "$env:LOCALAPPDATA\Microsoft\WinGet\Links\uv.exe" }

# benchmark/runs.py: note this machine and how the run ended in <run dir>/run_meta.json and rebuild runs/runs.html (the
# training runs grid). Never fatal; does not change the training run.
function Get-NewRunDir {
    $d = Get-ChildItem "runs\$Experiment" -Directory -Filter "*_$Name" -ErrorAction SilentlyContinue | Sort-Object Name | Select-Object -Last 1
    if ($d -and $d.FullName -ne $before) { $d.FullName } else { $null }
}
function Write-RunRecord($dir, $ended) {
    try {
        $py = if (Test-Path .venv\Scripts\python.exe) { '.venv\Scripts\python.exe' } else { 'python' }
        $a = @('..\benchmark\runs.py')
        if ($dir) {
            $e = ("$ended" -replace '"', "'"); if ($e.Length -gt 200) { $e = $e.Substring(0, 200) }
            $a += @('--record', $dir, '--ended', $e, '--launcher', 'tools/detached_run.ps1', '--task', $Task, '--body', $Body)
        }
        & $py @a 2>&1 | Out-String | Write-Output
    } catch { Write-Output "runs grid not updated: $_" }
}
$before = Get-ChildItem "runs\$Experiment" -Directory -Filter "*_$Name" -ErrorAction SilentlyContinue | Sort-Object Name | Select-Object -Last 1
$before = if ($before) { $before.FullName } else { $null }
$ended = 'interrupted (Ctrl+C) or launcher stopped'
try {
    $resume = if ($LoadRun) { @('--agent.resume', 'True', '--agent.load-run', $LoadRun, '--agent.load-checkpoint', $Checkpoint) } else { @() }
    $trainArgv = @('run', 'train', $Task, '--log-root', 'runs') + $resume + @('--agent.run-name', $Name, '--agent.max-iterations', "$Iterations") +
        ($TrainArgs -split '\s+' | Where-Object { $_ })
    $train = Start-Process $uv -ArgumentList $trainArgv -NoNewWindow -PassThru `
        -RedirectStandardOutput "runs\$Name.log" -RedirectStandardError "runs\$Name.err.log"

    if ($WatchArgs) {
        $runDir = $null
        for ($i = 0; $i -lt 120 -and -not $runDir; $i++) {
            Start-Sleep 5
            $runDir = Get-ChildItem "runs\$Experiment" -Directory -Filter "*_$Name" -ErrorAction SilentlyContinue |
                Sort-Object Name | Select-Object -Last 1
        }
        if ($runDir) {
            $prefix = if ($WatchPrefix) { $WatchPrefix } else { $Name }
            $watchArgv = @('run', 'python', 'tools/watch_gate.py', $runDir.FullName, $prefix) + ($WatchArgs -split '\s+' | Where-Object { $_ })
            Start-Process $uv -ArgumentList $watchArgv -NoNewWindow `
                -RedirectStandardOutput "runs\${Name}_watch.log" -RedirectStandardError "runs\${Name}_watch.err.log" | Out-Null
        }
    }
    $train.WaitForExit()
    $m = Select-String -Path "runs\$Name.log" -Pattern 'Learning iteration (\d+)/(\d+)' -ErrorAction SilentlyContinue | Select-Object -Last 1
    $last, $max = if ($m) { [int]$m.Matches[0].Groups[1].Value, [int]$m.Matches[0].Groups[2].Value } else { -1, -1 }
    $exc = if (Test-Path "runs\$Name.err.log") { Select-String -Path "runs\$Name.err.log" -Pattern '^(\w+\.)*\w*(Error|Exception|Interrupt|Exit)\b' -ErrorAction SilentlyContinue | Select-Object -Last 1 }
    $ended = if ($last -ge 0 -and $last -ge $max - 1) { 'finished' }
        elseif ($exc -and $exc.Line -like 'KeyboardInterrupt*') { 'interrupted (Ctrl+C)' }
        elseif ($exc) { "crashed: $($exc.Line)" }
        else { "ended early at iteration $last/$max (killed?)" }
} catch { $ended = "launcher crashed: $_"; throw }
finally { Write-RunRecord (Get-NewRunDir) $ended }
