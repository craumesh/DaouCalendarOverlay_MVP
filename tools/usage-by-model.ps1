# Sums Claude Code token usage by model from local transcripts (~/.claude/projects).
# Usage: powershell -ExecutionPolicy Bypass -File usage-by-model.ps1 [-Minutes 180]
#   -Minutes 10080 = last 7 days (same range as the 7d chart)
# Sections:
#   [1] totals per model: main = main session, sub = subagents / workflow agents
#   [2] per local day, input + output only (same units as the 7d chart, cache excluded)
#   [3] where each model ran: main, or the subagent's agentType
#   [4] implementation ratio: implementer + senior-implementer tokens by model (target Opus 30%, max 40%)
#   [!] any model other than the allowed ones (default: claude-opus-5-5, claude-sonnet-5-5)
param([int]$Minutes = 180, [string[]]$Allowed = @('claude-opus-5-5', 'claude-sonnet-5-5'))

$root = if ($env:CLAUDE_CONFIG_DIR) { Join-Path $env:CLAUDE_CONFIG_DIR 'projects' } else { Join-Path (Join-Path $HOME '.claude') 'projects' }
if (-not (Test-Path $root)) { Write-Output "Not found: $root"; return }

$inv = [Globalization.CultureInfo]::InvariantCulture
$cutUtc = (Get-Date).ToUniversalTime().AddMinutes(-$Minutes)
$cut = $cutUtc.ToString('yyyy-MM-ddTHH:mm:ss', $inv)
$msgs = @{}

$files = Get-ChildItem -Path $root -Recurse -Filter '*.jsonl' -File -ErrorAction SilentlyContinue |
  Where-Object { $_.LastWriteTimeUtc -ge $cutUtc }

foreach ($f in $files) {
  $isAgentFile = $f.Name.StartsWith('agent-')
  $agentType = 'sub'
  if ($isAgentFile) {
    $meta = $f.FullName -replace '\.jsonl$', '.meta.json'
    if (Test-Path $meta) {
      try {
        $mt = [System.IO.File]::ReadAllText($meta, [System.Text.Encoding]::UTF8)
        $mm = [regex]::Match($mt, '"agentType"\s*:\s*"([^"]+)"')
        if ($mm.Success) { $agentType = $mm.Groups[1].Value }
      } catch { }
    }
  }
  try {
    $fs = New-Object System.IO.FileStream($f.FullName, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::ReadWrite)
    $sr = New-Object System.IO.StreamReader($fs, [System.Text.Encoding]::UTF8)
  } catch { continue }
  try {
    $n = 0
    while ($null -ne ($line = $sr.ReadLine())) {
      $n++
      if (-not $line.Contains('"usage"') -or -not $line.Contains('claude-')) { continue }
      # the top-level timestamp is the last one on the line
      $tsm = [regex]::Matches($line, '"timestamp":"(\d{4}-\d\d-\d\dT\d\d:\d\d:\d\d)')
      if ($tsm.Count -eq 0) { continue }
      $ts = $tsm[$tsm.Count - 1].Groups[1].Value
      if ([string]::CompareOrdinal($ts, $cut) -lt 0) { continue }
      try { $d = ConvertFrom-Json -InputObject $line } catch { continue }
      $m = $d.message
      if ($null -eq $m) { continue }
      $model = [string]$m.model
      if ($model -notlike '*claude-*') { continue }
      $u = $m.usage
      $v = [long[]]@([long]$u.input_tokens, [long]$u.output_tokens, [long]$u.cache_read_input_tokens, [long]$u.cache_creation_input_tokens)
      $sub = $isAgentFile -or $d.isSidechain -eq $true
      $src = if ($sub) { 'sub' } else { 'main' }
      $who = if ($isAgentFile) { $agentType } elseif ($sub) { 'sub' } else { 'main' }
      $day = [DateTime]::ParseExact($ts, 'yyyy-MM-ddTHH:mm:ss', $inv, [Globalization.DateTimeStyles]::AssumeUniversal).ToString('MM-dd', $inv)
      $k = if ($m.id) { [string]$m.id } else { "$($f.FullName):$n" }
      if ($msgs.ContainsKey($k)) {
        # the same response is written on several lines; count it once
        $old = $msgs[$k]
        for ($i = 0; $i -lt 4; $i++) { if ($old.v[$i] -gt $v[$i]) { $v[$i] = $old.v[$i] } }
        if ($old.src -eq 'sub') { $src = 'sub'; $who = $old.who }
        $day = $old.day
      }
      $msgs[$k] = @{ model = $model; src = $src; who = $who; day = $day; v = $v }
    }
  } finally { $sr.Close() }
}

if ($msgs.Count -eq 0) { Write-Output "No usage found in the last $Minutes minutes under $root"; return }

$tot = @{}; $byDay = @{}; $byWho = @{}
foreach ($e in $msgs.Values) {
  $k1 = "$($e.model)|$($e.src)"
  if (-not $tot.ContainsKey($k1)) { $tot[$k1] = [long[]]@(0, 0, 0, 0, 0) }
  $t = $tot[$k1]; $t[0]++
  for ($i = 0; $i -lt 4; $i++) { $t[$i + 1] += $e.v[$i] }

  $k2 = "$($e.day)|$($e.model)"
  if (-not $byDay.ContainsKey($k2)) { $byDay[$k2] = [long[]]@(0, 0) }
  $byDay[$k2][0] += $e.v[0]; $byDay[$k2][1] += $e.v[1]

  $k3 = "$($e.model)|$($e.who)"
  if (-not $byWho.ContainsKey($k3)) { $byWho[$k3] = [long[]]@(0, 0) }
  $byWho[$k3][0]++; $byWho[$k3][1] += $e.v[0] + $e.v[1]
}

# [1] Rough relative weights: output x5, cache read x0.1, cache write x1.25 (price differences between models not included)
$rows = foreach ($key in $tot.Keys) {
  $t = $tot[$key]; $p = $key.Split('|')
  [pscustomobject]@{ Model = $p[0]; Src = $p[1]; Req = $t[0]; Input = $t[1]; Output = $t[2]; CacheRead = $t[3]; CacheWrite = $t[4]
                     W = $t[1] + 5 * $t[2] + 0.1 * $t[3] + 1.25 * $t[4] }
}
$sum = ($rows | Measure-Object -Property W -Sum).Sum
if ($sum -le 0) { $sum = 1 }
$fmt = '{0,-27}{1,-5}{2,6}{3,12}{4,12}{5,14}{6,13}{7,8}'
Write-Output "[1] Totals, last $Minutes minutes"
Write-Output ($fmt -f 'model', 'src', 'req', 'input', 'output', 'cache_rd', 'cache_wr', 'share')
foreach ($r in ($rows | Sort-Object -Property W -Descending)) {
  Write-Output ($fmt -f $r.Model, $r.Src, $r.Req, $r.Input, $r.Output, $r.CacheRead, $r.CacheWrite, ('{0:0.0}%' -f (100 * $r.W / $sum)))
}

# [2] Same units as the 7d chart: input + output, cache excluded
Write-Output ''
Write-Output '[2] Per day (input + output, same units as the 7d chart)'
$fmt2 = '{0,-7}{1,-27}{2,12}{3,12}{4,14}'
Write-Output ($fmt2 -f 'day', 'model', 'input', 'output', 'input+output')
$dayRows = foreach ($key in $byDay.Keys) {
  $p = $key.Split('|'); $t = $byDay[$key]
  [pscustomobject]@{ Day = $p[0]; Model = $p[1]; In = $t[0]; Out = $t[1]; Both = $t[0] + $t[1] }
}
foreach ($r in ($dayRows | Sort-Object -Property @{ Expression = 'Day' }, @{ Expression = 'Both'; Descending = $true })) {
  Write-Output ($fmt2 -f $r.Day, $r.Model, $r.In, $r.Out, $r.Both)
}

# [3] Where each model ran
Write-Output ''
Write-Output '[3] Where each model ran (main session, or subagent agentType)'
$fmt3 = '{0,-27}{1,-22}{2,6}{3,14}'
Write-Output ($fmt3 -f 'model', 'who', 'req', 'input+output')
$whoRows = foreach ($key in $byWho.Keys) {
  $p = $key.Split('|'); $t = $byWho[$key]
  [pscustomobject]@{ Model = $p[0]; Who = $p[1]; Req = $t[0]; Both = $t[1] }
}
foreach ($r in ($whoRows | Sort-Object -Property @{ Expression = 'Model' }, @{ Expression = 'Both'; Descending = $true })) {
  Write-Output ($fmt3 -f $r.Model, $r.Who, $r.Req, $r.Both)
}

# [4] Implementation ratio (input + output of implementer and senior-implementer only)
Write-Output ''
Write-Output '[4] Implementation ratio (implementer + senior-implementer, input + output)'
$impl = @{ opus = [long]0; sonnet = [long]0; other = [long]0 }
foreach ($r in $whoRows) {
  if ($r.Who -ne 'implementer' -and $r.Who -ne 'senior-implementer') { continue }
  if ($r.Model -like '*opus*') { $impl.opus += $r.Both }
  elseif ($r.Model -like '*sonnet*') { $impl.sonnet += $r.Both }
  else { $impl.other += $r.Both }
}
$implSum = $impl.opus + $impl.sonnet + $impl.other
if ($implSum -eq 0) {
  Write-Output 'no implementer / senior-implementer usage in this range'
} else {
  $share = 100.0 * $impl.opus / $implSum
  $verdict = if ($share -le 30) { 'within target (<= 30%)' } elseif ($share -le 40) { 'above target, within max (30-40%)' } else { 'OVER MAX (> 40%)' }
  Write-Output ('Opus   {0,14}' -f $impl.opus)
  Write-Output ('Sonnet {0,14}' -f $impl.sonnet)
  if ($impl.other -gt 0) { Write-Output ('Other  {0,14}' -f $impl.other) }
  Write-Output ('Opus share: {0:0.0}%  -> {1}' -f $share, $verdict)
}

# [!] Models outside the allowed set
$unexpected = @($whoRows | Where-Object { $Allowed -notcontains $_.Model })
if ($unexpected.Count -gt 0) {
  Write-Output ''
  Write-Output ('[!] Models outside the allowed set (' + ($Allowed -join ', ') + '):')
  foreach ($r in ($unexpected | Sort-Object -Property Both -Descending)) {
    Write-Output ($fmt3 -f $r.Model, $r.Who, $r.Req, $r.Both)
  }
  Write-Output '    Runs before the kit was installed, other projects, or a safety-classifier fallback can cause this.'
}
