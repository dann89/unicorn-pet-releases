# Measures CPU (% of one core) and memory of the pet in a pinned state. Diagnostics only.
# Usage: .\measure.ps1 -Exe <path> -State idle|walk|sleep [-Seconds 120]
param([string]$Exe, [ValidateSet('idle','walk','sleep')][string]$State = 'idle', [int]$Seconds = 120)
$p = Start-Process $Exe -ArgumentList '--hold', $State -PassThru
Start-Sleep 8
$p.Refresh(); $c1 = $p.TotalProcessorTime; $t1 = Get-Date
Start-Sleep $Seconds
$p.Refresh(); $c2 = $p.TotalProcessorTime; $t2 = Get-Date
$cpu = ($c2 - $c1).TotalSeconds / ($t2 - $t1).TotalSeconds * 100
$r = [pscustomobject]@{ State = $State; CpuPctOfOneCore = [math]::Round($cpu, 2); WorkingSetMB = [math]::Round($p.WorkingSet64 / 1MB, 1); PrivateMB = [math]::Round($p.PrivateMemorySize64 / 1MB, 1); Threads = $p.Threads.Count }
Stop-Process -Id $p.Id -Force
$r
