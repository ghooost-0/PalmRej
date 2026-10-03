#requires -Version 5.1
<#
PalmRej 제거

  설정 > 앱 에서 "PalmRej (Wacom 손날 인식)" 을 제거하면 이 스크립트가 돕니다.

  1. 드라이버가 설치돼 있으면 드라이버 OFF 를 돌립니다 (드라이버 제거, 테스트 모드 끔)
  2. 설치 때 이 컴퓨터에 등록한 서명 인증서를 뺍니다
     (설치 전부터 있던 것은 건드리지 않습니다 - 설치정보.json 에 적힌 것만)
  3. 바로가기, 앱 목록 항목, 이 폴더(C:\Program Files\PalmRej)를 지웁니다

  -DryRun  무엇을 할지 보여 주기만 하고 아무것도 바꾸지 않습니다.
  -Gui     설치 창(PalmRej 설치.exe /uninstall)이 붙일 때 씁니다. 창을 띄우지 않고 진행 단계와
           질문·결과를 창에 넘깁니다. 0.1.13 부터 설정 > 앱 의 "제거" 가 이 길로 옵니다.
#>
[CmdletBinding()]
param(
    [switch]$DryRun,
    [switch]$Quiet,
    [switch]$Gui
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Windows.Forms | Out-Null

# 설치 창과 주고받기 - install.ps1 과 같은 방식 (자세한 설명은 그쪽에).
function Send-Gui {
    param([string]$Kind, [string[]]$Fields = @())
    if (-not $Gui) { return }
    $enc = @($Fields | ForEach-Object { [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes([string]$_)) })
    [Console]::Out.WriteLine("@@PALMREJ|" + $Kind + "|" + ($enc -join "|"))
    [Console]::Out.Flush()
}
function Write-Step {
    param([int]$N, [int]$Of, [string]$Text)
    Write-Host ""
    Write-Host ("[{0}/{1}] {2}" -f $N, $Of, $Text)
    Send-Gui STEP @([string]$N)
}
function Read-GuiAnswer {
    param([string]$Icon, [string]$Caption, [string]$Text)
    Send-Gui ASK @($Icon, $Caption, $Text)
    while ($true) {
        $l = [Console]::In.ReadLine()
        if ($null -eq $l) { return $false }
        if ($l -match '^\W*YES\s*$') { return $true }
        if ($l -match '^\W*NO\s*$') { return $false }
    }
}
Send-Gui ENC @([string][Console]::OutputEncoding.CodePage)
Send-Gui PLAN @('드라이버 끄기', '서명 인증서 빼기', '관리 앱 지우기')

$Here = $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($Here)) { $Here = Split-Path -Parent $MyInvocation.MyCommand.Path }
$Title = "PalmRej 제거"
$ArpKey = "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PalmRej"

function Box([string]$Text, [string]$Icon = "Information") {
    if ($Gui) { Send-Gui BOX @($Icon, $Title, $Text); return }
    if ($Quiet) { return }
    [void][System.Windows.Forms.MessageBox]::Show($Text, $Title,
        [System.Windows.Forms.MessageBoxButtons]::OK, [System.Windows.Forms.MessageBoxIcon]::$Icon)
}
function Stop-Here([string]$Text) {
    Write-Host ""
    Write-Host "중단: $Text" -ForegroundColor Red
    Box ("제거하지 못했습니다.`n`n" + $Text) "Error"
    exit 1
}
# 설치 창에서 예상하지 못한 오류로 멈추면 오류 문장을 창에 넘긴다 (install.ps1 과 같음).
# 창이 없을 때는 예전과 같다.
trap {
    if (-not $Gui) { break }
    $err = $_
    Write-Host ("오류: " + ($err | Out-String).Trim())
    Send-Gui BOX @('Error', $Title, ("제거 중 예상하지 못한 오류로 멈췄습니다.`n`n" + $err.Exception.Message +
                   "`n`n어디까지 됐는지는 '자세히 보기'에 있습니다."))
    exit 1
}
function Read-TestSigning {
    foreach ($cmd in @(@("/enum","{current}"), @("/enum","ACTIVE"), @())) {
        $t = (& bcdedit.exe @cmd 2>&1 | Out-String)
        if ($t -match '(?im)^\s*testsigning\s+(\S+)') { return $Matches[1] }
    }
    return $null
}
# OFF.ps1 을 돌려 종료 코드를 돌려준다. 0 끝남 / 1 드라이버를 못 내림 /
# 3 드라이버는 내렸지만 테스트 모드를 못 끔 / 4 BitLocker 때문에 테스트 모드를 안 끔 /
# 5 확인에서 멈춤 (등록이나 패키지가 남음 - 지금 재부팅하면 터치가 멈출 수 있음, 1.0.0 부터).
function Invoke-Off([string]$Off) {
    # 작업 폴더를 지울 폴더 밖에 둔다 - 안에 있으면 그 폴더가 지워지지 않는다.
    $p = Start-Process -FilePath "powershell.exe" -Wait -PassThru -WindowStyle Hidden -WorkingDirectory $env:SystemRoot `
             -ArgumentList "-NoProfile -ExecutionPolicy Bypass -File `"$Off`" -Quiet"
    return $p.ExitCode
}
function Stop-TestModeLeftOn([int]$Code) {
    # 드라이버는 이미 빠졌다. 관리 앱과 인증서는 남겨 두어서, 테스트 모드를 마저 끈 다음
    # 다시 제거할 수 있게 한다 (다시 제거하면 아래 [1/3] 에서 테스트 모드부터 끈다).
    Send-Gui REBOOT @('1')
    Stop-Here ("PalmRej 드라이버는 내렸지만 Windows 테스트 모드를 끄지 못했습니다" +
               $(if ($Code -eq 4) { " (BitLocker 보호를 잠시 멈추지 못함)" } else { "" }) + ".`n" +
               "관리 앱과 인증서는 지우지 않았습니다.`n`n" +
               $(if ($Code -eq 4) { "Windows 설정에서 BitLocker 보호를 일시 중단한 다음 다시 제거하세요." }
                 else { "재부팅한 다음 다시 제거하세요." }))
}
# OFF 가 드라이버를 다 내리지 못하고 끝났을 때 (코드 1, 5). 드라이버가 일부만 내려간 채다.
# 이때 재부팅하면 남은 등록이 지워진 서비스를 찾다가 터치가 멈출 수 있다 (코드 5 가 그 경우).
# 재부팅하지 말고 바로 다시 하라고 한다. REBOOT 줄은 보내지 않는다 - 설치 창에 '닫기' 만 나온다.
function Stop-OffUnfinished([int]$Code) {
    Stop-Here ("드라이버를 다 내리지 못했습니다 (코드 $Code). 관리 앱과 인증서는 지우지 않았습니다.`n`n" +
               $(if ($Code -eq 5) { "지금 재부팅하면 터치가 멈출 수 있습니다.`n" } else { "" }) +
               "재부팅하지 말고 바로 다시 제거하거나, 관리 앱에서 'PalmRej 끄기'를 한 번 더 누르세요.`n" +
               "두 번째에도 안 되면 재부팅하지 말고 기록을 보내 주세요.`n" +
               "기록: " + (Join-Path (Join-Path $Here "드라이버 ON-OFF") "실행기록"))
}
# Palm 등록을 두 가지 형태 모두에서 찾는다 (OFF.ps1 의 Find-PalmRegistrations 와 같음).
#   형태 A  UpperFilters / LowerFilters  (REG_MULTI_SZ 값)
#   형태 B  ...\Filters\*Lower 또는 *Upper 하위 키의 값 이름 (PalmRawUsbTap 의 확장 INF)
function Get-RegValueOrNull {
    param([string]$KeyPath, [string]$ValueName)
    $props = Get-ItemProperty -LiteralPath $KeyPath -ErrorAction SilentlyContinue
    if ($null -eq $props) { return $null }
    $m = $props.PSObject.Properties[$ValueName]
    if ($null -eq $m) { return $null }
    return $m.Value
}
function Find-PalmRegistrations {
    $hits = @()
    foreach ($root in @("HKLM:\SYSTEM\CurrentControlSet\Enum\HID",
                        "HKLM:\SYSTEM\CurrentControlSet\Enum\USB",
                        "HKLM:\SYSTEM\CurrentControlSet\Control\Class")) {
        if (-not (Test-Path -LiteralPath $root)) { continue }
        foreach ($key in @(Get-ChildItem -LiteralPath $root -Recurse -ErrorAction SilentlyContinue)) {
            $clean = ($key.PSPath -replace 'Microsoft\.PowerShell\.Core\\Registry::','')
            foreach ($vn in @("UpperFilters","LowerFilters")) {
                $data = Get-RegValueOrNull -KeyPath $key.PSPath -ValueName $vn
                if ($null -eq $data) { continue }
                if ((@($data) -join ';') -notmatch 'Palm') { continue }
                $hits += [pscustomobject]@{ Form="MULTISZ"; Key=$clean; ValueName=$vn }
            }
            if ($key.PSChildName -eq '*Lower' -or $key.PSChildName -eq '*Upper') {
                $props = Get-ItemProperty -LiteralPath $key.PSPath -ErrorAction SilentlyContinue
                if ($null -eq $props) { continue }
                foreach ($pr in $props.PSObject.Properties) {
                    if ($pr.Name -like 'PS*') { continue }
                    if ($pr.Name -notmatch 'Palm') { continue }
                    $hits += [pscustomobject]@{ Form="FILTERKEY"; Key=$clean; ValueName=$pr.Name }
                }
            }
        }
    }
    return $hits
}
function Step([string]$Text) {
    if ($DryRun) { Write-Host ("  (시험) " + $Text) -ForegroundColor Cyan } else { Write-Host ("  " + $Text) }
}

# 앱 목록에서 누르면 일반 권한으로 시작한다. 관리자 권한으로 스스로 다시 띄운다.
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
               [Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin -and -not $DryRun -and $Gui) {
    # 설치 창은 관리자 권한으로 돈다. 여기서 스스로 다시 띄우면 창과 끊어진다.
    Stop-Here "관리자 권한으로 실행되지 않았습니다. 아무것도 지우지 않았습니다."
}
if (-not $isAdmin -and -not $DryRun) {
    try {
        $a = "-NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`""
        if ($Quiet) { $a += " -Quiet" }
        Start-Process -FilePath "powershell.exe" -ArgumentList $a -Verb RunAs | Out-Null
    } catch {
        Box "관리자 권한이 있어야 지울 수 있습니다." "Warning"
    }
    exit 0
}

Write-Host "============================================================"
Write-Host " PalmRej 제거$(if ($DryRun) { ' - 시험 실행 (아무것도 바꾸지 않음)' })"
Write-Host "============================================================"
Write-Host ("  폴더: " + $Here)

# 설치 정보
$info = $null
$infoPath = Join-Path $Here "설치정보.json"
if (Test-Path -LiteralPath $infoPath) {
    try { $info = Get-Content -LiteralPath $infoPath -Raw -Encoding UTF8 | ConvertFrom-Json } catch { $info = $null }
}
$thumb    = if ($info -and $info.PSObject.Properties['CertThumbprint']) { [string]$info.CertThumbprint } else { "" }
$addedTo  = @(if ($info -and $info.PSObject.Properties['CertAddedTo']) { $info.CertAddedTo | Where-Object { $_ } })
$links    = @(if ($info -and $info.PSObject.Properties['Shortcuts'])   { $info.Shortcuts | Where-Object { $_ } })

# 1.0.1 부터: 첫 설치가 인증서를 등록한 뒤 중간에 실패하고 다시 설치했으면, 두 번째 설치는
# 인증서가 "이미 있음" 이라 CertAddedTo 가 비어 있다. 그래도 그 인증서가 PalmRej 서명 인증서이고
# 이 컴퓨터에 그 개인 키가 없으면(= 개발 PC 가 아니면) 설치가 넣은 것으로 보고 같이 뺀다.
if ($thumb -and $addedTo.Count -lt 2) {
    $hasKey = @(Get-ChildItem Cert:\CurrentUser\My, Cert:\LocalMachine\My -ErrorAction SilentlyContinue |
                Where-Object { $_.Thumbprint -eq $thumb -and $_.HasPrivateKey }).Count -gt 0
    if (-not $hasKey) {
        foreach ($storeName in 'Root', 'TrustedPublisher') {
            if ($addedTo -contains $storeName) { continue }
            $found = @(Get-ChildItem "Cert:\LocalMachine\$storeName" -ErrorAction SilentlyContinue |
                       Where-Object { $_.Thumbprint -eq $thumb -and $_.Subject -eq 'CN=PalmRej Test Signing' })
            if ($found.Count -gt 0) { $addedTo += $storeName }
        }
    }
}

# 관리 앱이 열려 있으면 폴더를 못 지운다
$running = @(Get-Process -ErrorAction SilentlyContinue | Where-Object {
    try { $_.Path -and $_.Path.StartsWith($Here, [StringComparison]::OrdinalIgnoreCase) } catch { $false } })
if ($running.Count -gt 0 -and -not $DryRun) { Stop-Here "PalmRej 관리 앱이 열려 있습니다. 앱을 닫고 다시 제거하세요." }

if (-not $DryRun -and -not $Quiet) {
    $ask = ("PalmRej 를 이 컴퓨터에서 지웁니다.`n`n" +
            "• 드라이버를 끄고 지웁니다. Windows 테스트 모드도 끕니다`n" +
            "  (테스트 모드가 필요한 다른 프로그램이 있으면 그것도 영향을 받습니다)`n" +
            "• 설치 때 등록한 서명 인증서를 뺍니다`n" +
            "• 관리 앱, 바로가기, 드라이버 백업을 지웁니다`n`n" +
            "터치는 와콤 기본 드라이버로 계속 됩니다. 계속할까요?")
    if ($Gui) {
        $yes = Read-GuiAnswer -Icon "Question" -Caption $Title -Text $ask
    } else {
        $ans = [System.Windows.Forms.MessageBox]::Show($ask,
            $Title, [System.Windows.Forms.MessageBoxButtons]::YesNo,
            [System.Windows.Forms.MessageBoxIcon]::Question,
            [System.Windows.Forms.MessageBoxDefaultButton]::Button2)
        $yes = ($ans -eq [System.Windows.Forms.DialogResult]::Yes)
    }
    if (-not $yes) { exit 2 }
}

# ------------------------------------------------------------------
Write-Step 1 3 "드라이버"

$palm = @()
$enum = (& pnputil.exe /enum-drivers 2>&1 | Out-String)
foreach ($block in ($enum -split "(\r?\n){2,}")) {
    if ($block -match '(?im)^\s*[^:\r\n]+:\s*(oem\d+\.inf)\s*$') {
        $pub = $Matches[1]
        if ($block -match '(?i)(palmrejfilter|palmrejpipeline|palmrawusbtap)\.inf') { $palm += $pub }
    }
}
$rebootNeeded = $false
$off = Join-Path (Join-Path $Here "드라이버 ON-OFF") "OFF.ps1"
if ($palm.Count -eq 0) {
    Write-Host "  저장소에 Palm 드라이버 패키지 없음"
    # 패키지가 없어도 '이미 OFF' 라고 단정하지 않는다. 끄기가 [5/6] 에서 멈추면(코드 5) 패키지는
    # 지워지고 확장 INF 키(Filters\*Lower) 같은 등록만 남는다. 그대로 앱을 지우고 재부팅하면 없는
    # 서비스를 찾다가 터치가 멈추고, 고칠 도구도 남지 않는다. 그래서 Palm 등록이 남았거나 Palm
    # 서비스에 삭제 표시(DeleteFlag, OFF 뒤 재부팅 전)가 있으면 테스트 서명과 상관없이 OFF 를 부른다.
    # 끝까지 간 끄기 뒤라면 OFF 는 ALREADY_DOWN 으로 바로 끝나거나(Start=4) 빈 백업 하나로 끝난다.
    # 전에 드라이버만 내리고 테스트 모드를 못 끈 채 끝났으면(OFF 코드 3/4) 여기서 마저 끈다.
    # 이대로 앱을 지우면 테스트 모드를 끌 도구가 남지 않는다. OFF.ps1 은 이때(ALREADY_DOWN)
    # 백업을 새로 만들지 않고 테스트 서명만 끈다.
    $leftRegs = @(Find-PalmRegistrations)
    foreach ($x in $leftRegs) { Write-Host ("  남은 등록 {0} {1} at {2}" -f $x.Form, $x.ValueName, $x.Key) -ForegroundColor Yellow }
    $pendingDel = @('PalmRejFilter','PalmRejPipeline','PalmRawUsbTap' | Where-Object {
        $sp = Get-ItemProperty -LiteralPath "HKLM:\SYSTEM\CurrentControlSet\Services\$_" -ErrorAction SilentlyContinue
        $sp -and $sp.PSObject.Properties['DeleteFlag'] -and $sp.DeleteFlag -ne 0 })
    if ($pendingDel.Count -gt 0) { Write-Host ("  삭제 표시(재부팅 전)  " + ($pendingDel -join ", ")) }
    $unfinished = ($leftRegs.Count -gt 0 -or $pendingDel.Count -gt 0)
    $tsLeft = [string](Read-TestSigning) -match '^(?i)(Yes|예|On|True|1)$'
    if (($unfinished -or $tsLeft) -and -not (Test-Path -LiteralPath $off)) {
        # 남은 등록이 있는데 끌 도구가 없다. 앱을 지우면 고칠 길이 없어지므로 멈춘다.
        if ($leftRegs.Count -gt 0) {
            Stop-Here ("Palm 등록이 남아 있는데 드라이버를 끄는 스크립트(OFF.ps1)가 없습니다: $off`n" +
                       "지금 재부팅하면 터치가 멈출 수 있습니다. 아무것도 지우지 않았습니다.")
        }
    } elseif ($unfinished -or $tsLeft) {
        Step $(if ($unfinished) { "드라이버 끄기 마저 하기 (패키지는 이미 없음)" } else { "Windows 테스트 모드 끄기 (드라이버는 이미 없음)" })
        if (-not $DryRun) {
            $code = Invoke-Off $off
            if ($code -eq 3 -or $code -eq 4) { Stop-TestModeLeftOn $code }
            if ($code -ne 0) {
                if ($code -eq 5 -or $unfinished) { Stop-OffUnfinished $code }
                Stop-Here ("Windows 테스트 모드를 끄지 못했습니다 (코드 $code). 관리 앱과 인증서는 지우지 않았습니다.")
            }
            $rebootNeeded = $true
        }
    } else {
        Write-Host "  남은 등록 없음, 테스트 모드 꺼짐 (이미 OFF)"
    }
} else {
    if (-not (Test-Path -LiteralPath $off)) { Stop-Here "드라이버를 끄는 스크립트(OFF.ps1)가 없습니다: $off`n아무것도 지우지 않았습니다." }
    Step ("드라이버 끄기 (" + ($palm -join ", ") + ")")
    if (-not $DryRun) {
        $code = Invoke-Off $off
        if ($code -eq 3 -or $code -eq 4) { Stop-TestModeLeftOn $code }
        # 드라이버가 일부만 내려간 채다 (위 Stop-OffUnfinished 설명).
        if ($code -ne 0) { Stop-OffUnfinished $code }
        $rebootNeeded = $true
    }
}

# ------------------------------------------------------------------
Write-Step 2 3 "서명 인증서"

if (-not $thumb -or $addedTo.Count -eq 0) {
    Write-Host "  설치 때 등록한 인증서 없음 (원래 있던 것은 그대로 둡니다)"
} else {
    foreach ($storeName in $addedTo) {
        if ($storeName -notin @('Root', 'TrustedPublisher')) { continue }
        Step ("인증서 빼기  LocalMachine\{0}  {1}" -f $storeName, $thumb)
        if ($DryRun) { continue }
        $s = New-Object System.Security.Cryptography.X509Certificates.X509Store($storeName, 'LocalMachine')
        try {
            $s.Open('ReadWrite')
            foreach ($c in @($s.Certificates.Find('FindByThumbprint', $thumb, $false))) { $s.Remove($c) }
        } catch {
            Write-Host ("  인증서를 빼지 못함 ({0}): {1}" -f $storeName, $_.Exception.Message) -ForegroundColor Yellow
        } finally { $s.Close() }
    }
}

# ------------------------------------------------------------------
Write-Step 3 3 "관리 앱"

$ws = New-Object -ComObject WScript.Shell
foreach ($l in $links) {
    if (-not (Test-Path -LiteralPath $l)) { continue }
    # 이 폴더를 가리키는 바로가기만 지운다. 이름이 같은 다른 바로가기는 두고 간다.
    $target = ""
    try { $target = $ws.CreateShortcut($l).TargetPath } catch { }
    if ($target -and $target.StartsWith($Here, [StringComparison]::OrdinalIgnoreCase)) {
        Step ("바로가기 지우기  " + $l)
        if (-not $DryRun) { Remove-Item -LiteralPath $l -Force -ErrorAction SilentlyContinue }
    } else {
        Write-Host ("  두고 감 (다른 곳을 가리킴)  " + $l)
    }
}
if (Test-Path -LiteralPath $ArpKey) {
    Step "앱 목록 항목 지우기"
    if (-not $DryRun) { Remove-Item -LiteralPath $ArpKey -Recurse -Force -ErrorAction SilentlyContinue }
}

# Program Files\PalmRej 가 맞는지 한 번 더 본다 - 엉뚱한 폴더를 지우면 안 된다.
# 이 스크립트가 든 폴더지만 PowerShell 은 스크립트 파일을 잡고 있지 않다. 이 프로세스의
# 현재 폴더만 밖으로 옮기면 지금 바로 지울 수 있다 (Set-Location 만으로는 안 되고
# [Environment]::CurrentDirectory 도 옮겨야 한다). 바로 지워야 제거 창의 [지금 재부팅] 이
# 지우기보다 먼저 가는 일이 없다. 못 지운 것만 끝나고 3초 뒤에 한 번 더 지운다.
$expected = Join-Path $env:ProgramFiles "PalmRej"
if ($Here.TrimEnd('\') -ieq $expected.TrimEnd('\')) {
    Step ("폴더 지우기  " + $Here)
    if (-not $DryRun) {
        $gone = $false
        try {
            Set-Location -LiteralPath $env:SystemRoot
            [Environment]::CurrentDirectory = $env:SystemRoot
            Remove-Item -LiteralPath $Here -Recurse -Force -ErrorAction Stop
            $gone = -not (Test-Path -LiteralPath $Here)
        } catch {
            Write-Host ("  바로 지우지 못함: " + $_.Exception.Message) -ForegroundColor Yellow
        }
        if (-not $gone) {
            Start-Process -FilePath "cmd.exe" -WindowStyle Hidden -WorkingDirectory $env:SystemRoot `
                -ArgumentList ("/c ping 127.0.0.1 -n 4 >nul & rmdir /s /q `"" + $Here + "`"")
            Write-Host "  남은 것은 3초 뒤에 한 번 더 지웁니다"
        }
    }
} else {
    Write-Host ("  폴더는 두고 감 (Program Files\PalmRej 가 아님): " + $Here) -ForegroundColor Yellow
}

Write-Host ""
Write-Host "============================================================"
Write-Host " 제거 완료$(if ($rebootNeeded) { '. 재부팅하면 테스트 모드도 꺼집니다.' })" -ForegroundColor Cyan
Write-Host "============================================================"
Send-Gui REBOOT @($(if ($rebootNeeded) { '1' } else { '0' }))
if (-not $DryRun) {
    Box ("PalmRej 를 지웠습니다." + $(if ($rebootNeeded) { "`n`n재부팅하면 드라이버와 테스트 모드가 완전히 빠집니다." } else { "" }))
}
exit 0
