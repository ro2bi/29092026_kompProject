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
Assert ($health.ok -and $health.trainingExamples -eq 50) 'Server and local model ready'
$script:state = Post 'start' @{}
Assert ($state.stage -eq 0) 'Create session'
$cookie = $session.Cookies.GetCookies([uri]$base) | Where-Object Name -eq 'drib-session'
Assert ($cookie.HttpOnly) 'HttpOnly session cookie'
$other = New-Object Microsoft.PowerShell.Commands.WebRequestSession
try { Invoke-RestMethod "$base/state" -WebSession $other | Out-Null; throw 'Session unexpectedly shared' }
catch { Assert ([int]$_.Exception.Response.StatusCode -eq 404) 'Sessions isolated' }
$state = Post 'next' @{revision=$state.revision}
Assert ($state.round -eq 0) 'Cannot skip via HTTP'
Answer '7' 'I add the denominators'
Assert ($state.stage -eq 0 -and $state.gaps.count -gt 0) 'Wrong denominator stays on step'
$oldRevision=$state.revision
Answer '12'
try { Post 'answer' @{answer='12'; reasoning=''; apiConsent=$false; revision=$oldRevision} | Out-Null; throw 'Duplicate accepted' }
catch { Assert ([int]$_.Exception.Response.StatusCode -eq 409) 'Stale revision rejected' }
Answer '4 3'
Answer '7/12'
Assert $state.complete 'First exercise complete'
$state=Post 'next' @{revision=$state.revision}
Answer '12'
Answer '3 2'
Answer '5/12'
Assert $state.complete 'Second exercise complete'
$state=Post 'next' @{revision=$state.revision}
Answer '11/15' 'I converted both fractions to equal-sized parts.'
Assert ($state.complete -and $state.transfer) 'Independent exercise complete'
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
Write-Host "ALL $checks HTTP CHECKS PASSED"
