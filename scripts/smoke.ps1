$ErrorActionPreference = 'Stop'
$base = 'http://127.0.0.1:5186/api'
$session = New-Object Microsoft.PowerShell.Commands.WebRequestSession
$checks = 0
function Assert($condition, $label) {
    if (-not $condition) { throw "FAIL: $label" }
    $script:checks++
    Write-Host "PASS: $label"
}
function Post($path, $data) {
    $json = $data | ConvertTo-Json -Compress
    Invoke-RestMethod "$base/$path" -Method Post -WebSession $session -ContentType 'application/json; charset=utf-8' -Body ([System.Text.Encoding]::UTF8.GetBytes($json))
}
function Answer($value, $reason = '') {
    $script:state = Post 'answer' @{answer=$value; reasoning=$reason; apiConsent=$false; revision=$script:state.revision}
}
$health = Invoke-RestMethod "$base/health"
Assert ($health.ok -and $health.trainingExamples -eq 60) 'Server and local model ready'
$script:state = Post 'start' @{}
Assert ($state.stage -eq 0) 'Create session'
$cookie = $session.Cookies.GetCookies([uri]$base) | Where-Object Name -eq 'drib-session'
Assert ($cookie.HttpOnly) 'HttpOnly session cookie'
$other = New-Object Microsoft.PowerShell.Commands.WebRequestSession
try { Invoke-RestMethod "$base/state" -WebSession $other | Out-Null; throw 'Session unexpectedly shared' }
catch { Assert ([int]$_.Exception.Response.StatusCode -eq 404) 'Sessions isolated' }
$state = Post 'next' @{revision=$state.revision}
Assert ($state.round -eq 0) 'Cannot skip via HTTP'
function Solve {
    $e=$script:state.exercise
    $a=[int]$e.b; $b=[int]$e.d
    while ($b -ne 0) { $r=$a % $b; $a=$b; $b=$r }
    $den=[int]($e.b / $a * $e.d)
    $n1=[int]($e.a * ($den / $e.b)); $n2=[int]($e.c * ($den / $e.d))
    if (-not $script:state.transfer) { Answer "$den"; Answer "$n1 $n2" }
    Answer "$($n1+$n2)/$den" 'I converted both fractions and added signed numerators.'
}
Answer '0'
Assert ($state.stage -eq 0) 'Wrong denominator stays on step'
$oldRevision=$state.revision
$state=Post 'hint' @{revision=$state.revision}
try { Post 'answer' @{answer='12'; reasoning=''; apiConsent=$false; revision=$oldRevision} | Out-Null; throw 'Duplicate accepted' }
catch { Assert ([int]$_.Exception.Response.StatusCode -eq 409) 'Stale revision rejected' }
for ($round=0; $round -lt 3; $round++) {
    Solve
    Assert $state.complete "Exercise $round complete"
    if ($round -lt 2) { $state=Post 'next' @{revision=$state.revision} }
}
$state=Post 'next' @{revision=$state.revision}
Assert ($state.finished -and $state.correctSteps -eq 7) 'Summary and seven correct steps'
$plan = Invoke-WebRequest "$base/plan" -WebSession $session -UseBasicParsing
Assert ($plan.Headers['Content-Disposition'] -match 'DribKrok-plan.txt' -and $plan.RawContentLength -gt 100) 'Download learning plan'
try { Invoke-RestMethod "$base/start" -Method Post -WebSession $session -ContentType 'application/json' -Headers @{Origin='https://example.org'} -Body '{}' | Out-Null; throw 'Foreign origin accepted' }
catch { Assert ([int]$_.Exception.Response.StatusCode -eq 403) 'Foreign origin rejected' }
[void]$session.Headers.Remove('Origin')
Post 'reset' @{} | Out-Null
try { Invoke-RestMethod "$base/state" -WebSession $session | Out-Null; throw 'Session not deleted' }
catch { Assert ([int]$_.Exception.Response.StatusCode -eq 404) 'Reset removes session' }
foreach ($grade in @(5,7,8,9)) {
    $state=Post 'start' @{grade=$grade}
    Assert ($state.grade -eq $grade) "Selected grade $grade"
    Solve
    Assert $state.complete "Web arithmetic grade $grade"
}
try { Post 'start' @{grade=10} | Out-Null; throw 'Invalid grade accepted' }
catch { Assert ([int]$_.Exception.Response.StatusCode -eq 400) 'Invalid grade rejected' }
Write-Host "ALL $checks HTTP CHECKS PASSED"
