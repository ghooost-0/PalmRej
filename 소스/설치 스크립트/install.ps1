#requires -Version 5.1
<#
PalmRej 1.1.0 설치

  Cintiq Pro 24 (DTH-2420)
      패키지 세 개를 설치합니다. 손날 인식 동작은 1.0 과 같습니다.
      0.1.3 부터 파이프라인의 디버깅 기능(KMDF Verifier)은 켜지 않습니다.
      0.1.4 부터 손날 크기의 접점이 보이면 드래그 살리기를 바로 취소합니다.
      0.1.5 부터 크기 3 으로 닿은 터치는 처음 48ms 동안 붙잡아 손날인지 봅니다.
      0.1.6 부터 손날 중의 와콤 무응답을 고장으로 치지 않아, 손날 직후 드래그용 홀드가 꺼지지 않습니다.
      0.1.7 부터 필터가 읽기 요청을 취소하지 않고 (터치가 죽던 경로), 손날 크기의 짧은 접촉은 탭으로 살리지 않습니다.
      0.1.8 부터 자리를 막고 있던 낡은 탭을 버리고, 손날 직후 드래그 시작에 점을 더 보내고, 손날 크기 판단을 70ms 까지 기다립니다.
      0.1.9 부터 눌린 접점이 모두 손날 크기면 탭으로 내보내지 않습니다 (손날 스칠 때 점이 찍히던 경로).
      0.1.10 부터 미끄러진 짧은 접촉은 탭으로 살리지 않고, 탭 직후 터치가 끝나지 않던 문제와 두 손가락 탭의 뗌 누락(파이프라인)을 고쳤습니다.
      0.1.11 부터 손날을 뗀 직후(50ms 안)에 닿은 손가락을 손날과 한 덩어리로 버리지 않습니다 (손날 직후 탭이 씹히던 경로).
      0.1.12 부터 손날 직후 두 손가락 드래그가 끝난 뒤 밀려 있던 옛 입력이 다시 들어가지 않고, 파이프라인이 한 번 엉키면 재부팅 전까지 꺼져 있던 문제와 첫 입력이 "뗌" 상태로 온 제스처가 씹히던 문제를 고쳤습니다.
      0.1.13 부터 설치 창(PalmRej 설치.exe)으로 설치·제거합니다. 콘솔 창이 뜨지 않습니다. 드라이버는 0.1.12 와 같습니다.
      0.1.14 부터 손을 오래 올려 두었다 뗀 직후의 제스처가 씹히지 않고(대기 장치가 15초 쉬던 것), 펜이 닿을 때 손바닥 자리에 짧은 터치가 들어가지 않습니다.
      0.1.15 부터 손날을 뗀 직후 Wacom 이 첫 입력을 "뗌" 으로 보내고 조용해지는 경우에도 두 손가락 제스처를 살립니다.
      0.1.16 부터 손을 얹고 있을 때 타블렛이 함께 보내는 "손바닥" 표시를 프리게이트가 직접 읽어, 클립스튜디오처럼 두 손가락에 확대·회전·되돌리기를 걸어 둔 앱에서 손날만 올려도 제스처가 나가던 것을 막습니다. 진짜 두 손가락 제스처는 약 10ms 늦게 시작합니다.
      0.1.17 부터 타블렛이 "손바닥"이라고 판독한 직후에는 대기가 끝나도 터치를 내보내지 않고, 손날 직후 첫 프레임을 곧바로 내보내던 길을 닫았습니다 (손날 점이 찍히던 경로 셋). 관리 앱에 '진단 로그 기록'이 생겼습니다.
      0.1.18 부터 손날이 처음에 손가락처럼 보였다가 끊길 때 첫 프레임이 새어 나가던 것(저절로 되던 실행 취소)을 막고, 드래그 살리기 직후의 두 손가락 드래그가 통째로 씹히던 것을 고쳤습니다.
      0.1.19 부터 손이 먼저 닿고 펜이 뒤따라 들어올 때, 아직 화면에 안 나간 터치 조각을 버립니다 (펜 우선; 펜을 대는 순간 저절로 되던 실행 취소).
      0.1.20 부터 펜이 들어올 때 펜보다 먼저 시작된 손날 조각이 예외로 새던 것과 한 손가락 조각까지 버립니다 (펜 우선 보강).
      0.1.21 부터 펜이 인식 범위 안이면 예외 없이 터치를 막고(펜을 막 들어 떠 있을 때 포함), 펜 밑 손 때문에 홀드가 60초씩 꺼지지 않으며, 두 손가락 제스처 앞 한 손가락 깜빡임을 없앴습니다.
      0.1.22 부터 빠르게 콕 찍은 두 손가락 탭(실행 취소)이 그 자리에서 안 나가고 다음 터치 때 늦게 나가던 것을 고쳤습니다.
      0.1.24 부터 손날 뒤 오래 쉬었다가 한 첫 두 손가락 동작이 통째로 안 들어가던 것과, 손날 직후 긴 두 손가락 이동이 약 2초에서 한 번 끊기던 것을 고쳤습니다.
      0.1.25 부터 두 손가락처럼 닿은 손날이 두 손가락 탭(실행 취소)으로 나가던 경우 하나를 막습니다 (동시에 닿는 두 손가락은 약 0.02초 늦게 시작).
      0.1.26 부터 손날 직후 두 손가락 탭이 한 손가락 탭으로 나가던 것, 펜 아래 손을 다시 얹은 것이 실행 취소로 나가던 것을 고치고, 오래 쉰 뒤 첫 두 손가락 동작을 5분까지 살립니다.
      0.1.27 부터 손을 올려 둔 뒤 마지막으로 남은 한 점이 짧은 한 손가락 톡으로 나가던 것을 막습니다.
      1.0.0 정식판. 켜기·끄기·설치가 드물게 실패할 때 터치가 멈출 수 있던 경로를 막았습니다.
      1.0.0 공개판 (GitHub). 동작은 같고, 서명 인증서를 새로 만들고 파일 안의 빌드 정보를 정리했습니다.
      1.0.1 손날 인식 동작은 1.0.0 과 같습니다. 안내문(세 손가락 이상 차단, 와콤 센터 제스처, 문제 알리기)과 사용 조건을 고치고, 설치가 중간에 실패했을 때 남던 인증서를 제거할 때 함께 빼도록 했습니다.
      1.0.1-exp 실험판 (번호는 1.0.1 그대로, 드라이버 버전 1.0.1.1). 펜을 쓴 지 5초 안에 닿은 터치가 모든 접점이 손날 크기(3 이상)로 읽히면 최대 70ms 까지 기다려 손날인지 봅니다 (손날 점이 찍히던 경로 둘). 다시 설치할 때 번호만이 아니라 파일 내용까지 비교해서, 같은 번호의 다른 빌드도 새 판으로 바꿔 넣습니다. 서명 인증서가 '신뢰할 수 없는 인증서'에 들어 있으면 그렇다고 알려 줍니다.
      1.0.1-exp2 실험판 (번호는 1.0.1 그대로, 드라이버 버전 1.0.1.2). 1.0.1-exp 에 더해, 손날을 살짝 올렸다 뗄 때 두 손가락 탭(실행 취소)으로 나가던 경로 하나를 막습니다 (터치가 이미 손바닥으로 붙잡아 둔 짧은 두 손가락 접촉은 뗄 때 탭으로 다시 살리지 않음).
      1.0.1-exp3 실험판 (번호는 1.0.1 그대로, 드라이버 버전 1.0.1.3). 1.0.1-exp2 에 더해, 손날 크기 대기(70ms) 중에 타블렛이 손바닥으로 판독한 짧은 두 손가락 접촉도 뗄 때 두 손가락 탭(실행 취소)으로 다시 살리지 않습니다 (1.0.1-exp2 에서 빠져 있던 경우).
      1.1.0 공개판. 드라이버 동작은 1.0.1-exp3 과 같습니다 (실험판 셋을 합친 판). 펜을 쓴 지 5초 안에 닿은 터치가 모든 접점이 손날 크기로 읽히면 최대 0.07초 기다려 손날인지 보고, 터치가 이미 손바닥으로 붙잡아 둔 짧은 두 손가락 접촉은 뗄 때 두 손가락 탭(실행 취소)으로 내보내지 않습니다. 같은 번호의 다른 빌드도 새 판으로 바꿔 넣고, 서명 인증서가 막혀 있으면 그렇다고 알려 줍니다.

  다른 와콤 기종
      먼저 "확인되지 않은 기종" 경고를 띄우고 계속할지 묻습니다. 계속하면 타블렛
      구조를 Cintiq Pro 24 와 비교해서, 같을 때만 손날 인식 필터 하나를 설치합니다.
      다르면 아무것도 바꾸지 않고 멈춥니다.

  시작 조건: 드라이버가 내려가 있어야 합니다 (드라이버 OFF 상태).

  배포판 (이 폴더에 인증서\ 와 프로그램\ 이 있을 때)
      인증서\   패키지를 서명한 인증서를 이 컴퓨터가 믿도록 등록합니다. 없으면
                Windows 가 "게시자를 확인할 수 없습니다" 창을 띄우고, 관리 앱의
                ON 은 창이 숨겨져 있어 거기서 멈춥니다.
      프로그램\ 관리 앱과 ON/OFF 스크립트를 C:\Program Files\PalmRej 에 설치하고
                바탕화면·시작 메뉴 바로가기와 "앱 제거" 항목을 만듭니다.
      둘 다 없으면(개발 폴더) 드라이버만 설치합니다 - 예전과 같습니다.

  -CheckOnly      구조 비교만 하고 끝냅니다. 아무것도 바꾸지 않습니다. 관리자 권한 불필요.
  -DryRun         시작 조건 확인([1/6])까지만 하고 멈춥니다. 아무것도 바꾸지 않습니다.
  -Quiet          창을 띄우지 않습니다. 확인되지 않은 기종이면 설치하지 않습니다.
  -AssumeYes      확인되지 않은 기종 경고에 "예" 로 답합니다 (-Quiet 와 같이 쓸 때).
  -AsUnknownModel Cintiq Pro 24 도 확인되지 않은 기종처럼 다룹니다 (시험용).
  -Gui            설치 창(PalmRej 설치.exe)이 붙일 때 씁니다. 창을 띄우지 않고, 진행 단계와
                  질문·결과를 창에 넘깁니다 (아래 Send-Gui).
#>
[CmdletBinding()]
param(
    [switch]$Quiet,
    [switch]$CheckOnly,
    [switch]$DryRun,
    [switch]$AssumeYes,
    [switch]$AsUnknownModel,
    [switch]$Gui
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

Add-Type -AssemblyName System.Windows.Forms | Out-Null

# ------------------------------------------------------------------
# 설치 창(PalmRej 설치.exe)과 주고받기 - -Gui 일 때만
# 창은 이 스크립트를 콘솔 없이 돌리고, 평소 줄은 "자세히 보기"에 보여 준다.
# @@PALMREJ| 로 시작하는 줄은 창이 알아듣는 것이다. 칸마다 base64(UTF-8) 라서
# 숨은 콘솔의 코드 페이지와 상관없이 그대로 간다. 콘솔 코드 페이지는 바꾸지
# 않는다 - UTF-8 로 바꾸면 pnputil 이 영어로 바뀌어 버린다.
# ------------------------------------------------------------------
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
# 창에 예/아니요를 묻는다. 창은 빈 줄 다음에 YES 나 NO 를 보낸다 - 앞에 BOM 이
# 붙어도 그 빈 줄에 붙어서 답이 깨지지 않는다. 창이 사라지면(null) 아니요.
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
Send-Gui PLAN @('설치 조건 확인', '테스트 모드 켜기', '드라이버 설치', '드라이버 등록 확인', '관리 앱 설치', '마무리 확인')

$Ver   = "1.1.0"
$Title = "PalmRej $Ver"

# 안내 글에서 자세한 출력이 있는 곳. 설치 창에서는 콘솔 출력이 '자세히 보기' 안에 있다.
$SeeOutput = if ($Gui) { "'자세히 보기'" } else { "위 출력" }

# 설치 창에서 예상하지 못한 오류로 멈추면, 오류 문장을 그대로 창에 넘긴다. stderr 로 가면
# 숨은 콘솔 너비에서 줄이 잘려 창에는 반쪽만 보인다. 창이 없을 때는 예전과 같다
# (break = 오류를 쓰고 스크립트를 멈춘다). try/catch 가 잡는 오류는 여기 오지 않는다.
trap {
    if (-not $Gui) { break }
    $err = $_
    Write-Host ("오류: " + ($err | Out-String).Trim())
    try { Stop-Transcript | Out-Null } catch { }
    Send-Gui BOX @('Error', "$Title - 실패", ("설치 중 예상하지 못한 오류로 멈췄습니다.`n`n" + $err.Exception.Message +
                   "`n`n어디까지 됐는지는 '자세히 보기'와 기록 파일에 있습니다."))
    exit 1
}

$Root = $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($Root)) {
    try { $Root = Split-Path -Parent $MyInvocation.MyCommand.Path } catch { }
}
$PkgRoot = Join-Path $Root "패키지"

$Script:LogPath = $null
if (-not $CheckOnly -and -not $DryRun) {
    $Script:LogPath = Join-Path $Root ("설치기록_" + (Get-Date -Format "yyyyMMdd_HHmmss") + ".txt")
    try { Start-Transcript -LiteralPath $Script:LogPath -Force | Out-Null } catch { }
    Send-Gui LOG @($Script:LogPath)
}

function Hold {
    if ($Quiet -or $Gui) { return }
    Write-Host ""
    Write-Host "이 창을 닫으려면 Enter 를 누르세요." -ForegroundColor DarkGray
    [void][System.Console]::ReadLine()
}

function LogLine {
    # 설치 창은 기록 파일을 버튼으로 연다. 글에는 넣지 않는다.
    if ($Script:LogPath -and -not $Gui) { return "`n`n기록: " + $Script:LogPath }
    return ""
}

function Show-Box {
    param([string]$Text, [string]$Caption, [string]$Icon = "Information")
    # 설치 창에서는 MessageBox 를 띄우지 않는다 - 콘솔이 없는 스크립트의 창은
    # 설치 창 뒤로 숨어 버린다. 결과 글은 설치 창이 보여 준다.
    if ($Gui) { Send-Gui BOX @($Icon, $Caption, $Text); return }
    if ($Quiet) { return }
    [void][System.Windows.Forms.MessageBox]::Show($Text, $Caption,
        [System.Windows.Forms.MessageBoxButtons]::OK,
        [System.Windows.Forms.MessageBoxIcon]::$Icon)
}

function Fail {
    param(
        [Parameter(Mandatory=$true)][string]$Message,
        [string]$Heading = "설치하지 못했습니다.",
        [string]$Caption = ""
    )
    Write-Host ""
    Write-Host "중단: $Message" -ForegroundColor Red
    try { Stop-Transcript | Out-Null } catch { }
    if ($Quiet) { exit 1 }
    if (-not $Caption) { $Caption = "$Title - 실패" }
    Show-Box -Text ($Heading + "`n`n" + $Message + (LogLine)) -Caption $Caption -Icon "Error"
    Hold
    exit 1
}

function Read-TestSigning {
    foreach ($cmd in @(@("/enum","{current}"), @("/enum","ACTIVE"), @())) {
        $t = (& bcdedit.exe @cmd 2>&1 | Out-String)
        if ($t -match '(?im)^\s*testsigning\s+(\S+)') { return $Matches[1] }
    }
    return $null
}

# BitLocker 가 켜진 컴퓨터는 부팅 설정(testsigning)이 바뀌면 다음 부팅에 복구 키를 묻는다.
# 재부팅 한 번 동안만 보호를 멈춰 두면, Windows 가 새 설정으로 다시 잠그고 보호를 되살린다.
# (Microsoft 가 부팅 설정을 바꾸기 전에 하라고 안내하는 것과 같은 방법이다.)
# 결과: none 없음·꺼짐 / suspended 한 번 멈춤 / unknown 확인 못 함 / fail 멈추지 못함
function Suspend-BitLockerOnce {
    $vol = $null
    try {
        $vol = Get-CimInstance -Namespace 'root\cimv2\Security\MicrosoftVolumeEncryption' `
                   -ClassName Win32_EncryptableVolume `
                   -Filter ("DriveLetter='{0}'" -f $env:SystemDrive) -ErrorAction Stop
    } catch {
        # Home 판처럼 BitLocker 가 없으면 이 네임스페이스나 클래스 자체가 없다.
        $code = ""
        try { $code = [string]$_.Exception.NativeErrorCode } catch { }
        if ($code -match 'InvalidNamespace|InvalidClass|NotFound') { return 'none' }
        return 'unknown'
    }
    if ($null -eq $vol) { return 'none' }
    # 상태를 못 읽은 것은 "켜져 있다" 가 아니다 - 확인 못 함으로 둔다.
    # 멈추지 못함(fail)은 켜진 것을 확인하고 멈추기에 실패했을 때만이다.
    $ps = $null
    try { $ps = (Invoke-CimMethod -InputObject $vol -MethodName GetProtectionStatus -ErrorAction Stop).ProtectionStatus } catch { return 'unknown' }
    if ($ps -ne 1) { return 'none' }
    try {
        $r = Invoke-CimMethod -InputObject $vol -MethodName DisableKeyProtectors `
                 -Arguments @{ DisableCount = [uint32]1 } -ErrorAction Stop
        if ($r.ReturnValue -eq 0) { return 'suspended' }
        return 'fail'
    } catch { return 'fail' }
}

# ------------------------------------------------------------------
# 배포판 부품
# ------------------------------------------------------------------
$CertDir = Join-Path $Root "인증서"
$ProgSrc = Join-Path $Root "프로그램"
$ProgDir = Join-Path $env:ProgramFiles "PalmRej"
$AppExe  = "PalmRej 관리.exe"
$Distribution = Test-Path -LiteralPath $ProgSrc

# 이 폴더에 들어 있는 서명 인증서 (공개 부분만). 없으면 null.
function Get-BundledCert {
    $f = Get-ChildItem -LiteralPath $CertDir -Filter *.cer -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $f) { return $null }
    return New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($f.FullName)
}

# 인증서가 LocalMachine 의 그 저장소에 있는지
function Test-CertInStore([string]$StoreName, [string]$Thumbprint) {
    $s = New-Object System.Security.Cryptography.X509Certificates.X509Store($StoreName, 'LocalMachine')
    $s.Open('ReadOnly')
    try { return ($s.Certificates.Find('FindByThumbprint', $Thumbprint, $false).Count -gt 0) }
    finally { $s.Close() }
}

# 1.0.1-exp: 인증서가 '신뢰할 수 없는 인증서'(Disallowed) 저장소에 들어 있는 곳 - LocalMachine, CurrentUser.
# 읽기만 한다. 저장소를 못 열면 그곳은 없는 것으로 친다.
function Find-DisallowedCert([string]$Thumbprint) {
    $where = @()
    foreach ($loc in 'LocalMachine', 'CurrentUser') {
        try {
            $s = New-Object System.Security.Cryptography.X509Certificates.X509Store('Disallowed', $loc)
            $s.Open('ReadOnly, OpenExistingOnly')
            try { if ($s.Certificates.Find('FindByThumbprint', $Thumbprint, $false).Count -gt 0) { $where += $loc } }
            finally { $s.Close() }
        } catch { }
    }
    return $where
}

# 1.0.1-exp: 카탈로그 서명이 NotTrusted 일 때의 안내 글. Windows 는 '신뢰할 수 없는 인증서'에 든 인증서의
# 서명을 NotTrusted 로 돌려준다. 이것은 [1/6] 에서 멈추므로 아직 아무것도 바꾸지 않은 때다.
function Get-NotTrustedText {
    param([string[]]$Where, $Cert)
    $name = $Cert.GetNameInfo([System.Security.Cryptography.X509Certificates.X509NameType]::SimpleName, $false)
    $certText = "PalmRej 서명 인증서({0}, 지문 {1}…)" -f $name, $Cert.Thumbprint.Substring(0, 8)
    $how = @()
    if (@($Where) -contains 'LocalMachine') {
        $how += "시작 메뉴에서 '컴퓨터 인증서 관리'(certlm.msc)를 열고 '신뢰할 수 없는 인증서 > 인증서' 에서 '$name' 을 지우세요."
    }
    if (@($Where) -contains 'CurrentUser') {
        $how += "시작 메뉴에서 '사용자 인증서 관리'(certmgr.msc)를 열고 '신뢰할 수 없는 인증서 > 인증서' 에서 '$name' 을 지우세요."
    }
    if ($how.Count -gt 0) {
        return ("Windows 의 '신뢰할 수 없는 인증서' 목록에 " + $certText + "가 들어 있어서 이 드라이버를 설치할 수 없습니다.`n`n" +
                ($how -join "`n") + "`n그다음 다시 설치하세요.`n`n아무것도 바꾸지 않았습니다.")
    }
    return ("Windows 가 " + $certText + "를 믿지 않도록 설정돼 있어서 이 드라이버를 설치할 수 없습니다. " +
            "'신뢰할 수 없는 인증서' 목록에서는 찾지 못했습니다.`n`n" +
            "직접 막아 둔 것이 아니라면 보안 프로그램이나 회사 정책이 막은 것일 수 있습니다.`n`n" +
            "아무것도 바꾸지 않았습니다.")
}

# ==================================================================
# 타블렛 구조 비교
#
# 손날 인식 필터가 Cintiq Pro 24 에서 붙는 컬렉션은 네 개다. 다른 기종에서 이 넷이
# 같은 자리(몇 번째 컬렉션인지)에, 같은 사용처로, 같은 크기의 보고서를 내면 "구조가
# 같다" 고 본다. 제품 번호만 빼고 나머지가 다 맞는 경우다.
#
# 다른 기종용 패키지(PalmRejFilter_Compat)는 제품 번호 대신 사용처로 붙는다. 그래서
# 그 사용처를 가진 컬렉션이 이 넷 말고 또 있으면, 비교하지 않은 곳에 필터가 붙게
# 된다. 그것도 "다르다" 로 친다.
# ==================================================================
$Spec = @(
    [pscustomobject]@{ Part = 'touch'; Col = '01'; UP = 0xFF00; U = 0x0001; In = 64;  HwId = 'HID\VID_056A&UP:FF00_U:0001'; Name = '터치 장치 컬렉션 1 (와콤 전용 보고서)' },
    [pscustomobject]@{ Part = 'touch'; Col = '02'; UP = 0x000D; U = 0x0004; In = 64;  HwId = 'HID\VID_056A&UP:000D_U:0004'; Name = '터치 장치 컬렉션 2 (터치)' },
    [pscustomobject]@{ Part = 'touch'; Col = '03'; UP = 0x000D; U = 0x0004; In = 8;   HwId = 'HID\VID_056A&UP:000D_U:0004'; Name = '터치 장치 컬렉션 3 (터치)' },
    [pscustomobject]@{ Part = 'pen';   Col = '02'; UP = 0xFF00; U = 0x000A; In = 193; HwId = 'HID\VID_056A&UP:FF00_U:000A'; Name = '펜 장치 컬렉션 2 (와콤 전용 보고서)' }
)
$CompatIds = @($Spec | ForEach-Object { $_.HwId } | Select-Object -Unique)

$HidProbeSrc = @"
using System;
using System.Runtime.InteropServices;
public static class PalmRejHidProbe {
    [DllImport("hid.dll")] public static extern void HidD_GetHidGuid(out Guid g);
    [DllImport("hid.dll", SetLastError=true)] static extern bool HidD_GetPreparsedData(IntPtr h, out IntPtr pp);
    [DllImport("hid.dll")] static extern bool HidD_FreePreparsedData(IntPtr pp);
    [DllImport("hid.dll")] static extern int HidP_GetCaps(IntPtr pp, out HIDP_CAPS caps);
    [DllImport("kernel32.dll", SetLastError=true, CharSet=CharSet.Unicode)]
    static extern IntPtr CreateFile(string n, uint a, uint s, IntPtr sa, uint c, uint f, IntPtr t);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
    [StructLayout(LayoutKind.Sequential)]
    struct HIDP_CAPS {
        public ushort Usage; public ushort UsagePage;
        public ushort InputReportByteLength; public ushort OutputReportByteLength; public ushort FeatureReportByteLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst=17)] public ushort[] Reserved;
        public ushort NumberLinkCollectionNodes; public ushort NumberInputButtonCaps; public ushort NumberInputValueCaps;
        public ushort NumberInputDataIndices; public ushort NumberOutputButtonCaps; public ushort NumberOutputValueCaps;
        public ushort NumberOutputDataIndices; public ushort NumberFeatureButtonCaps; public ushort NumberFeatureValueCaps;
        public ushort NumberFeatureDataIndices;
    }
    // 접근 권한 0 으로 연다. 운영체제가 독점으로 잡고 있는 터치 컬렉션도 이렇게는 열린다.
    // 반환: { UsagePage, Usage, 입력 보고서 크기, 기능 보고서 크기 }, 못 읽으면 null.
    public static int[] Caps(string path) {
        IntPtr h = CreateFile(path, 0, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
        if (h == new IntPtr(-1)) return null;
        try {
            IntPtr pp;
            if (!HidD_GetPreparsedData(h, out pp)) return null;
            try {
                HIDP_CAPS c;
                if (HidP_GetCaps(pp, out c) != 0x00110000) return null;
                return new int[] { c.UsagePage, c.Usage, c.InputReportByteLength, c.FeatureReportByteLength };
            } finally { HidD_FreePreparsedData(pp); }
        } finally { CloseHandle(h); }
    }
}
"@

# 지금 연결된 와콤 HID 컬렉션을 전부 읽는다.
function Get-WacomCollections {
    if (-not ('PalmRejHidProbe' -as [type])) { Add-Type -TypeDefinition $HidProbeSrc }
    $g = [Guid]::Empty
    [PalmRejHidProbe]::HidD_GetHidGuid([ref]$g)

    $out = @()
    $devs = @(Get-PnpDevice -PresentOnly -ErrorAction SilentlyContinue |
              Where-Object { $_.InstanceId -like 'HID\VID_056A&*' } | Sort-Object InstanceId)
    foreach ($d in $devs) {
        $id = $d.InstanceId
        $m = [regex]::Match($id, '^HID\\VID_056A&PID_([0-9A-F]{4})((?:&MI_[0-9A-F]{2})?)(?:&COL([0-9A-F]{2}))?\\', 'IgnoreCase')
        if (-not $m.Success) { continue }
        $prod = $m.Groups[1].Value.ToUpper()
        $col  = if ($m.Groups[3].Success) { $m.Groups[3].Value.ToUpper() } else { '' }

        $path = '\\?\' + ($id -replace '\\','#') + '#{' + $g.ToString() + '}'
        $caps = $null
        try { $caps = [PalmRejHidProbe]::Caps($path) } catch { $caps = $null }

        $hw = @()
        try {
            $p = Get-PnpDeviceProperty -InstanceId $id -KeyName 'DEVPKEY_Device_HardwareIds' -ErrorAction Stop
            if ($null -ne $p -and $null -ne $p.Data) { $hw = @($p.Data) }
        } catch { }

        $out += [pscustomobject]@{
            InstanceId = $id
            Device     = 'PID_' + $prod + $m.Groups[2].Value.ToUpper()
            Col        = $col
            Read       = ($null -ne $caps)
            UP         = $(if ($null -ne $caps) { $caps[0] } else { -1 })
            U          = $(if ($null -ne $caps) { $caps[1] } else { -1 })
            In         = $(if ($null -ne $caps) { $caps[2] } else { -1 })
            Feat       = $(if ($null -ne $caps) { $caps[3] } else { -1 })
            HwIds      = $hw
        }
    }
    return $out
}

function Format-Collection {
    param($c)
    $colText = if ($c.Col) { "COL" + $c.Col } else { "(컬렉션 없음)" }
    if (-not $c.Read) { return ("{0,-12} {1,-6} 읽지 못함" -f $c.Device, $colText) }
    return ("{0,-12} {1,-6} {2:X4}/{3:X4}  입력 {4,4}  기능 {5,4}" -f $c.Device, $colText, $c.UP, $c.U, $c.In, $c.Feat)
}

function Test-Fits {
    param($c, $s)
    return ($c.Read -and $c.Col -eq $s.Col -and $c.UP -eq $s.UP -and $c.U -eq $s.U -and $c.In -eq $s.In)
}

function Test-HasHwId {
    param($c, [string]$HwId)
    foreach ($h in @($c.HwIds)) { if ([string]::Equals([string]$h, $HwId, [StringComparison]::OrdinalIgnoreCase)) { return $true } }
    return $false
}

# 결과: Same(같은가), Problems(다른 점, 사람이 읽는 문장), Matched(자리 -> 컬렉션)
function Compare-Structure {
    param([object[]]$Cols)

    $problems = New-Object System.Collections.Generic.List[string]
    $matched  = @()
    $groups   = @($Cols | Group-Object Device)

    foreach ($part in @('touch','pen')) {
        $want = @($Spec | Where-Object { $_.Part -eq $part })
        $partName = if ($part -eq 'touch') { '터치 장치' } else { '펜 장치' }
        $taken = @($matched | ForEach-Object { $_.Col.Device } | Select-Object -Unique)

        $hits = @()
        foreach ($grp in $groups) {
            if ($taken -contains $grp.Name) { continue }
            $all = $true
            foreach ($s in $want) {
                if (@($grp.Group | Where-Object { Test-Fits $_ $s }).Count -ne 1) { $all = $false; break }
            }
            if ($all) { $hits += $grp }
        }

        if ($hits.Count -eq 0) {
            $need = ($want | ForEach-Object { "컬렉션 {0} = {1:X4}/{2:X4} 입력 {3}" -f [int]$_.Col, $_.UP, $_.U, $_.In }) -join ", "
            $problems.Add(("{0}: Cintiq Pro 24 와 같은 모양인 장치가 없습니다 (필요: {1})" -f $partName, $need))
            continue
        }
        if ($hits.Count -gt 1) {
            $problems.Add(("{0}: 같은 모양인 장치가 {1}개입니다 ({2}). 어느 쪽인지 정할 수 없습니다" -f $partName, $hits.Count, (($hits | ForEach-Object { $_.Name }) -join ", ")))
            continue
        }
        foreach ($s in $want) {
            $c = @($hits[0].Group | Where-Object { Test-Fits $_ $s })[0]
            $matched += [pscustomobject]@{ Spec = $s; Col = $c }
            if (-not (Test-HasHwId $c $s.HwId)) {
                $problems.Add(("{0}: 다른 기종용 패키지가 붙을 이름({1})이 이 컬렉션에 없습니다" -f $s.Name, $s.HwId))
            }
        }
    }

    # 다른 기종용 패키지는 사용처로 붙는다 - 비교한 네 곳 말고 또 붙을 곳이 있으면 안 된다.
    # (네 곳을 다 찾았을 때만 본다. 못 찾았으면 이미 "다름" 이고, 같은 말만 늘어난다.)
    $mine = @($matched | ForEach-Object { $_.Col.InstanceId })
    if ($problems.Count -eq 0) { foreach ($c in $Cols) {
        if ($mine -contains $c.InstanceId) { continue }
        foreach ($h in $CompatIds) {
            if (Test-HasHwId $c $h) {
                $problems.Add(("비교하지 않은 컬렉션에도 필터가 붙게 됩니다: {0}" -f (Format-Collection $c).Trim()))
                break
            }
        }
    } }

    return [pscustomobject]@{
        Same     = ($problems.Count -eq 0)
        Problems = @($problems)
        Matched  = $matched
    }
}

function Show-Structure {
    param([object[]]$Cols, $Result)
    $mine = @($Result.Matched | ForEach-Object { $_.Col.InstanceId })
    Write-Host "    연결된 와콤 HID 컬렉션:"
    foreach ($c in $Cols) {
        $mark = if ($mine -contains $c.InstanceId) { "필터 자리" } else { "" }
        Write-Host ("      {0}  {1}" -f (Format-Collection $c), $mark)
    }
    if ($Result.Same) {
        Write-Host "    구조 비교        같음 (필터가 붙을 네 곳이 Cintiq Pro 24 와 같음)" -ForegroundColor Green
    } else {
        Write-Host "    구조 비교        다름" -ForegroundColor Yellow
        foreach ($p in $Result.Problems) { Write-Host ("      - " + $p) -ForegroundColor Yellow }
    }
}

# 경고 창에 보여 줄 기종 이름 (USB 가 스스로 알려 주는 이름 + 제품 번호)
function Get-WacomModelNames {
    $names = @()
    $prods = @(Get-PnpDevice -PresentOnly -ErrorAction SilentlyContinue |
               Where-Object { $_.InstanceId -like 'HID\VID_056A&*' } |
               ForEach-Object { if ($_.InstanceId -match '(?i)PID_([0-9A-F]{4})') { $Matches[1].ToUpper() } } |
               Select-Object -Unique)
    foreach ($p in $prods) {
        $n = $null
        $usb = Get-PnpDevice -PresentOnly -ErrorAction SilentlyContinue |
               Where-Object { $_.InstanceId -like "USB\VID_056A&PID_$p*" } | Select-Object -First 1
        if ($usb) {
            try {
                $d = Get-PnpDeviceProperty -InstanceId $usb.InstanceId -KeyName 'DEVPKEY_Device_BusReportedDeviceDesc' -ErrorAction Stop
                if ($null -ne $d -and $d.Data) { $n = ([string]$d.Data).Trim() }
            } catch { }
        }
        if (-not $n) { $n = "이름 없음" }
        $names += ("{0} (제품 번호 {1})" -f $n, $p)
    }
    return $names
}

# ------------------------------------------------------------------
# -CheckOnly: 구조 비교만
# ------------------------------------------------------------------
if ($CheckOnly) {
    Write-Host "============================================================"
    Write-Host " $Title - 구조 비교만 (아무것도 바꾸지 않음)"
    Write-Host "============================================================"
    $cols = @(Get-WacomCollections)
    if ($cols.Count -eq 0) {
        Write-Host "    와콤 HID 컬렉션이 하나도 없습니다. 타블렛이 꺼져 있거나 연결되지 않았습니다." -ForegroundColor Yellow
        exit 3
    }
    $r = Compare-Structure $cols
    Show-Structure $cols $r
    if ($r.Same) { exit 0 } else { exit 3 }
}

Write-Host "============================================================"
Write-Host " $Title 설치$(if ($DryRun) { ' - 시험 실행 (아무것도 바꾸지 않음)' })"
Write-Host "============================================================"

# ------------------------------------------------------------------
Write-Step 1 6 "시작 조건 확인"

$principal = [Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
$isAdmin = $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin -and -not $DryRun) {
    Fail "관리자 권한으로 실행해야 합니다."
}
Write-Host ("    관리자 권한      {0}" -f $(if ($isAdmin) { "있음" } else { "없음 (시험 실행이라 넘어감)" }))

# 보안 부팅이 켜져 있으면 테스트 모드를 켤 수 없다. [2/6] 에서 bcdedit 가 거부하기 전에,
# 아무것도 바꾸지 않은 지금 알려 준다. 확인이 안 되는 컴퓨터(옛 BIOS 등)는 [2/6] 에 맡긴다.
$secureBoot = $null
try { $secureBoot = Confirm-SecureBootUEFI -ErrorAction Stop } catch { $secureBoot = $null }
$tsNow = Read-TestSigning
$tsOn  = [bool]($tsNow -match '^(?i)(Yes|예|On|True|1)$')
# 테스트 모드가 이미 "켜짐" 으로 적혀 있어도 보안 부팅이 켜져 있으면 실제로는 동작하지 않는다
# (드라이버가 코드 52 로 막혀 터치가 죽는다). 그래서 보안 부팅이 켜져 있으면 늘 멈춘다.
if ($secureBoot -eq $true) {
    Fail ("보안 부팅(Secure Boot)이 켜져 있어서 Windows 테스트 모드가 동작하지 않습니다.`n" +
          "이 드라이버는 테스트 서명이라 테스트 모드가 꼭 필요합니다.`n`n" +
          "BIOS(UEFI) 설정에서 Secure Boot 를 끄고 다시 실행하세요.`n" +
          "아무것도 바꾸지 않았습니다.")
}
Write-Host ("    보안 부팅        {0}" -f $(if ($secureBoot -eq $true) { "켜짐" } elseif ($secureBoot -eq $false) { "꺼짐" } else { "확인 못 함" }))

# 관리 앱이 열려 있으면 새 판으로 바꿔 넣을 수 없다.
if ($Distribution) {
    $appRunning = @(Get-Process -ErrorAction SilentlyContinue | Where-Object {
        try { $_.Path -and $_.Path.StartsWith($ProgDir, [StringComparison]::OrdinalIgnoreCase) } catch { $false } })
    if ($appRunning.Count -gt 0) {
        if ($DryRun) { Write-Host "    관리 앱          열려 있음 (실제 설치라면 여기서 멈춤)" -ForegroundColor Yellow }
        else { Fail "PalmRej 관리 앱이 열려 있습니다. 앱을 닫고 다시 실행하세요.`n아무것도 바꾸지 않았습니다." }
    }
}

# 타블렛이 실제로 붙어 있는지. 없으면 아무것도 바꾸기 전에 멈춘다.
$wacomHid = @(Get-PnpDevice -PresentOnly -ErrorAction SilentlyContinue |
              Where-Object { $_.InstanceId -like 'HID\VID_056A&*' })
if ($wacomHid.Count -eq 0) {
    Fail ("와콤 타블렛을 찾지 못했습니다. 타블렛이 꺼져 있거나 연결되지 않았습니다.`n" +
          "타블렛을 켜고 다시 실행하세요. 아무것도 바꾸지 않았습니다.")
}

# 우리 드라이버는 와콤 드라이버(WacHidRouterPro)의 위아래에 붙는다. 와콤 드라이버 없이
# 윈도우 기본 드라이버만 있는 컴퓨터에서는 붙는 자리가 달라서 설치하지 않는다.
if (-not (Test-Path -LiteralPath "HKLM:\SYSTEM\CurrentControlSet\Services\WacHidRouterPro")) {
    Fail ("와콤 드라이버가 설치돼 있지 않습니다.`n" +
          "와콤 홈페이지에서 타블렛 드라이버(Wacom Center)를 먼저 설치하고 다시 실행하세요.`n" +
          "아무것도 바꾸지 않았습니다.")
}
Write-Host "    와콤 드라이버    있음"

$known = @(Get-PnpDevice -PresentOnly -ErrorAction SilentlyContinue |
           Where-Object { $_.InstanceId -like 'USB\VID_056A&PID_0355*' })
if ($known.Count -gt 0 -and -not $AsUnknownModel) {
    $Mode = 'Known'
    Write-Host "    타블렛           Cintiq Pro 24 연결됨"
} else {
    $Mode = 'Compat'
    Write-Host ("    타블렛           확인되지 않은 기종{0}" -f $(if ($AsUnknownModel) { " (시험용으로 그렇게 다룸)" } else { "" })) -ForegroundColor Yellow
}

# 드라이버 저장소의 Palm 패키지 (게시 이름, 원래 INF 이름, 버전).
function Get-PalmPackages {
    $list = @()
    $txt = (& pnputil.exe /enum-drivers 2>&1 | Out-String)
    foreach ($block in ($txt -split "(\r?\n){2,}")) {
        if ($block -match '(?im)^\s*[^:\r\n]+:\s*(oem\d+\.inf)\s*$') {
            $pub = $Matches[1]
            if ($block -match '(?i)(palmrejfilter|palmrejpipeline|palmrawusbtap)\.inf') {
                $orig = $Matches[0].ToLowerInvariant()
                $v = ''
                if ($block -match '(?m)(\d+\.\d+\.\d+\.\d+)\s*$') { $v = $Matches[1] }
                $list += [pscustomobject]@{ Published = $pub; Original = $orig; Version = $v }
            }
        }
    }
    return $list
}

# 1.0.1-exp: 같은 판인지를 DriverVer 번호만으로 보면, 같은 번호로 다시 빌드한 패키지를 "이미 설치됨" 으로
# 잘못 본다. 그래서 저장소에 든 패키지의 실제 파일을 이 판의 파일과 비교한다.
# 저장소의 oemNN.inf 가 어느 폴더인지는 Windows 가 HKLM\SYSTEM\DriverDatabase\DriverInfFiles\oemNN.inf 에
# 적어 둔다 (Active 값, 없으면 기본값의 첫 줄 = FileRepository 안의 폴더 이름). 읽기만 하고 관리자 권한도
# 필요 없다. 못 찾으면 $null.
function Get-PackageFolder {
    param(
        [string]$Published,
        [string]$DbRoot   = 'HKLM:\SYSTEM\DriverDatabase\DriverInfFiles',
        [string]$RepoRoot = (Join-Path $env:SystemRoot 'System32\DriverStore\FileRepository')
    )
    if ($Published -notmatch '^(?i)oem\d+\.inf$') { return $null }
    $name = $null
    try {
        $props = Get-ItemProperty -LiteralPath (Join-Path $DbRoot $Published) -ErrorAction Stop
        if ($null -eq $props) { return $null }
        $act = $props.PSObject.Properties['Active']
        if ($act -and [string]$act.Value) { $name = [string]$act.Value }
        else {
            $def = $props.PSObject.Properties['(default)']
            if ($def) { $name = @(@($def.Value) | Where-Object { [string]$_ } | Select-Object -First 1) -join '' }
        }
    } catch { return $null }
    if (-not $name -or $name -notmatch '^[^\\/:*?"<>|]+$') { return $null }
    $dir = Join-Path $RepoRoot $name
    if (-not (Test-Path -LiteralPath $dir -PathType Container)) { return $null }
    return $dir
}

# 저장소의 그 패키지($Pkg, Get-PalmPackages 의 한 줄)가 이 판의 패키지($PlanItem)와 같은 빌드인가.
#   $true  그 폴더의 .sys 와 INF 가 이 판의 것과 바이트까지 같음
#   $false 폴더는 찾았는데 다름 (또는 파일이 없음)
#   $null  모름 (폴더를 못 찾음 / 읽지 못함) - 쓰는 곳마다 안전한 쪽으로 다룬다
# INF 까지 보는 것은 Cintiq Pro 24 용과 다른 기종용 필터가 .sys 는 같고 INF 만 달라서다.
function Test-SameBuild {
    param($Pkg, $PlanItem)
    if ($null -eq $Pkg -or $null -eq $PlanItem) { return $null }
    $dir = Get-PackageFolder $Pkg.Published
    if (-not $dir) { return $null }
    try {
        $ourInf   = [string]$PlanItem.InfPath
        $ourSys   = Join-Path (Split-Path $ourInf -Parent) $PlanItem.Sys
        $theirInf = Join-Path $dir (Split-Path $ourInf -Leaf)
        $theirSys = Join-Path $dir $PlanItem.Sys
        if (-not (Test-Path -LiteralPath $theirInf -PathType Leaf) -or -not (Test-Path -LiteralPath $theirSys -PathType Leaf)) { return $false }
        $sameSys = ((Get-FileHash -LiteralPath $theirSys -Algorithm SHA256).Hash -eq (Get-FileHash -LiteralPath $ourSys -Algorithm SHA256).Hash)
        $sameInf = ((Get-FileHash -LiteralPath $theirInf -Algorithm SHA256).Hash -eq (Get-FileHash -LiteralPath $ourInf -Algorithm SHA256).Hash)
        return ($sameSys -and $sameInf)
    } catch { return $null }
}

# 저장소 패키지의 원래 INF 이름(소문자)에 맞는 이 판의 패키지. 없으면 $null.
function Get-PlanItemFor([string]$Original) {
    return (@($Plan | Where-Object { (Split-Path $_.InfPath -Leaf).ToLowerInvariant() -eq $Original }) | Select-Object -First 1)
}

# 같은 번호인 패키지 하나를 사람이 읽는 말로 (설치 기록용).
function Format-SameBuild($r) {
    if ($r -eq $true) { return "같은 빌드" }
    if ($r -eq $false) { return "다른 빌드" }
    return "같은 빌드인지 확인 못 함"
}

# Palm 등록을 두 가지 형태 모두에서 찾는다 (OFF.ps1 / ON.ps1 의 Find-PalmRegistrations 와 같음).
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
                $hits += [pscustomobject]@{ Form="MULTISZ"; Key=$clean; ValueName=$vn
                                            Data=@($data | ForEach-Object {[string]$_}) }
            }
            if ($key.PSChildName -eq '*Lower' -or $key.PSChildName -eq '*Upper') {
                $props = Get-ItemProperty -LiteralPath $key.PSPath -ErrorAction SilentlyContinue
                if ($null -eq $props) { continue }
                foreach ($pr in $props.PSObject.Properties) {
                    if ($pr.Name -like 'PS*') { continue }
                    if ($pr.Name -notmatch 'Palm') { continue }
                    $hits += [pscustomobject]@{ Form="FILTERKEY"; Key=$clean; ValueName=$pr.Name; Data=@() }
                }
            }
        }
    }
    return $hits
}

# 드라이버 OFF 를 누른 뒤 재부팅 전이면, Windows 가 우리 서비스를 "재부팅 때 삭제" 로
# 표시해 둔 상태다 (DeleteFlag=1). 이 위에 설치하면 서비스 등록이 거부되어 설치가 반쯤
# 깨지고, 재부팅 뒤 터치가 죽을 수 있다. 재부팅이 먼저다.
$pendingDelete = @()
foreach ($svcName in 'PalmRejFilter','PalmRejPipeline','PalmRawUsbTap') {
    $sp = Get-ItemProperty -LiteralPath "HKLM:\SYSTEM\CurrentControlSet\Services\$svcName" -ErrorAction SilentlyContinue
    if ($sp -and $sp.PSObject.Properties['DeleteFlag'] -and $sp.DeleteFlag -ne 0) { $pendingDelete += $svcName }
}
if ($pendingDelete.Count -gt 0) {
    $msg = ("관리 앱에서 'PalmRej 끄기'를 누르거나 PalmRej 를 제거한 뒤 아직 재부팅하지 않았습니다.`n`n" +
            "지금 설치하면 Windows 가 지우려고 표시해 둔 등록 위에 설치하게 되어 " +
            "재부팅 뒤 터치가 안 될 수 있습니다.`n`n" +
            "재부팅한 다음 다시 실행하세요. 아무것도 바꾸지 않았습니다.")
    # 끄기가 [5/6] 확인에서 멈췄으면 (1.0.0 OFF 코드 5, 0.1.27 이하 OFF 코드 1) 서비스에 삭제 표시가
    # 있는데 Palm 등록(두 형태)이나 패키지가 남아 있다. 끝까지 간 끄기는 둘 다 없어야 [5/6] 을 지난다.
    # 관리 앱(Program.cs Judge 의 OffIncomplete)과 같은 규칙이다. Start 는 보지 않는다 - 0.1.27 의
    # OFF 는 [5/6] 전에 Start=4 를 쓰고, 1.0.0 은 [5/6] 뒤에 쓰는데 그 쓰기만 실패할 수도 있다.
    # 멈춘 끄기 뒤에 재부팅하면 남은 등록이 없는 서비스를 찾다가 터치가 멈출 수 있으므로 반대로
    # 안내한다. 어느 쪽이든 여기서 멈추고 아무것도 바꾸지 않는다.
    $leftRegs = @(Find-PalmRegistrations)
    $leftPkgs = @(Get-PalmPackages)
    Write-Host ("    남은 Palm 등록 {0}개, 저장소의 Palm 패키지 {1}개" -f $leftRegs.Count, $leftPkgs.Count)
    if ($leftRegs.Count -gt 0 -or $leftPkgs.Count -gt 0) {
        foreach ($x in $leftRegs) { Write-Host ("      등록 {0} {1} at {2}" -f $x.Form, $x.ValueName, $x.Key) }
        foreach ($x in $leftPkgs) { Write-Host ("      패키지 {0} ({1})" -f $x.Published, $x.Original) }
        Write-Host "OFF_INCOMPLETE=1"
        $msg = ("관리 앱의 'PalmRej 끄기'가 다 끝나지 않은 채입니다.`n`n" +
                "지금 재부팅하면 터치가 멈출 수 있습니다.`n" +
                "재부팅하지 말고 관리 앱에서 'PalmRej 끄기'를 한 번 더 누르세요. " +
                "끄기가 끝난 뒤 재부팅하고 다시 설치하면 됩니다.`n`n" +
                "아무것도 바꾸지 않았습니다.")
    }
    if ($DryRun) { Write-Host ("    OFF 후 재부팅 전  " + ($pendingDelete -join ", ") + " 삭제 예정  (실제 설치라면 여기서 멈춤)") -ForegroundColor Yellow }
    else { Fail $msg }
}

# 이미 Palm 패키지가 있을 때.
#   지금 부팅에서 테스트 모드가 켜져 있으면 (= 우리 드라이버가 돌고 있으면) 덮어쓴다.
#   새 판을 먼저 넣고, 버전이 다른 옛 판은 [3/6] 끝에서 지운다. OFF 도, 중간 재부팅도
#   필요 없다. 옛 판으로 되돌릴 때도 같은 길이다 - 옛 판을 넣고 새 판을 지우면 장치가
#   남은 옛 판으로 다시 설치된다.
#   테스트 모드가 꺼진 채 패키지만 남은 이상한 상태면 예전처럼 OFF 부터 하게 한다.
#   (Get-PalmPackages 는 위 '끄기가 덜 끝남' 확인에서도 쓰므로 그 앞에 있다.)
$existing = @(Get-PalmPackages)
$bootTestSigning = [bool]((Get-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Control' -ErrorAction SilentlyContinue).SystemStartOptions -match 'TESTSIGNING')
$Upgrade = $false
if ($existing.Count -gt 0) {
    $existingText = ($existing | ForEach-Object { "{0} ({1})" -f $_.Published, $(if ($_.Version) { $_.Version } else { '?' }) }) -join ", "
    if ($bootTestSigning -and ($null -eq $tsNow -or $tsOn)) {
        $Upgrade = $true
        Write-Host ("    기존 Palm 패키지  " + $existingText) -ForegroundColor Cyan
        Write-Host  "                      -> 이 판으로 덮어씁니다 (OFF 필요 없음, 재부팅 한 번)" -ForegroundColor Cyan
    } elseif ($tsOn) {
        # 패키지는 있고 테스트 모드는 다음 부팅부터 켜진다 - 설치(또는 PalmRej 켜기)를 하고
        # 아직 재부팅하지 않은 상태다. 필요한 것은 재부팅뿐이다.
        Write-Host ("    기존 Palm 패키지  " + $existingText + "  (테스트 모드는 재부팅하면 켜짐)") -ForegroundColor Yellow
        $msg = ("PalmRej 는 이미 설치돼 있고 재부팅만 남았습니다. Windows 테스트 모드는 재부팅하면 켜집니다.`n`n" +
                "재부팅한 다음, 그래도 다시 설치하고 싶으면 그때 실행하세요. 아무것도 바꾸지 않았습니다.")
        if ($DryRun) { Write-Host "    재부팅 전 (실제 설치라면 여기서 멈춤)" -ForegroundColor Yellow }
        else { Fail -Heading "재부팅이 먼저입니다." -Message $msg }
    } else {
        Write-Host ("    기존 Palm 패키지  " + $existingText + "  테스트 모드 꺼짐") -ForegroundColor Yellow
        $msg = ("이미 설치된 PalmRej 드라이버가 있는데 Windows 테스트 모드가 꺼져 있습니다.`n`n" +
                "관리 앱에서 'PalmRej 끄기'를 먼저 누르고 재부팅한 다음 다시 실행하세요. 아무것도 바꾸지 않았습니다.")
        if ($DryRun) { Write-Host "    (실제 설치라면 여기서 멈춤)" -ForegroundColor Yellow }
        else { Fail $msg }
    }
} else {
    Write-Host "    기존 Palm 패키지  없음"
}

# 확인되지 않은 기종: 경고 -> 계속하면 구조 비교 -> 같을 때만 진행
if ($Mode -eq 'Compat') {
    $models = @(Get-WacomModelNames)
    Write-Host "    연결된 와콤 장치:"
    foreach ($n in $models) { Write-Host ("      " + $n) }

    if ($Quiet -and -not $AssumeYes) {
        Fail "확인되지 않은 기종입니다. 창 없이(-Quiet) 는 설치하지 않습니다. 설치 창(PalmRej 설치.exe)이나 설치.cmd 로 직접 실행하세요."
    }
    if (-not $Quiet) {
        $warn = ("확인되지 않은 기종입니다.`n`n" +
                 "이 드라이버는 Wacom Cintiq Pro 24 (DTH-2420) 에서만 확인했습니다.`n" +
                 "연결된 장치:`n    " + ($models -join "`n    ") + "`n`n" +
                 "다른 기종에서는 정상적으로 작동하지 않을 수 있습니다.`n`n" +
                 "[예] 를 누르면 타블렛 구조를 Cintiq Pro 24 와 비교합니다.`n" +
                 "구조가 같을 때만 설치하고, 다르면 아무것도 바꾸지 않고 멈춥니다.`n`n" +
                 "계속할까요?")
        if ($Gui) {
            $yes = Read-GuiAnswer -Icon "Warning" -Caption "$Title - 확인되지 않은 기종" -Text $warn
        } else {
            $ans = [System.Windows.Forms.MessageBox]::Show($warn, "$Title - 확인되지 않은 기종",
                       [System.Windows.Forms.MessageBoxButtons]::YesNo,
                       [System.Windows.Forms.MessageBoxIcon]::Warning,
                       [System.Windows.Forms.MessageBoxDefaultButton]::Button2)
            $yes = ($ans -eq [System.Windows.Forms.DialogResult]::Yes)
        }
        if (-not $yes) {
            Write-Host ""
            Write-Host "취소했습니다. 아무것도 바꾸지 않았습니다." -ForegroundColor Cyan
            try { Stop-Transcript | Out-Null } catch { }
            exit 2
        }
    }
    Write-Host "    경고             계속하기로 함"

    $cols = @(Get-WacomCollections)
    $cmp  = Compare-Structure $cols
    Show-Structure $cols $cmp
    if (-not $cmp.Same) {
        Fail -Heading "이 기종은 구조가 Cintiq Pro 24 와 달라 설치할 수 없습니다." `
             -Caption "$Title - 설치할 수 없음" `
             -Message $(if ($Gui) { "다른 점은 '자세히 보기'에 있습니다.`n`n아무것도 바꾸지 않았습니다." }
                        else { "다른 점:`n  - " + ($cmp.Problems -join "`n  - ") + "`n`n아무것도 바꾸지 않았습니다." })
    }
} else {
    # 참고용. 이 기종에서도 비교가 "같음" 으로 나와야 비교 자체가 맞게 도는 것이다.
    # 설치 여부에는 영향을 주지 않는다.
    try {
        $cols = @(Get-WacomCollections)
        $cmp  = Compare-Structure $cols
        Write-Host ("    구조 비교(참고)  {0}" -f $(if ($cmp.Same) { "같음" } else { "다름: " + ($cmp.Problems -join " / ") }))
    } catch {
        Write-Host ("    구조 비교(참고)  실행 못함: " + $_.Exception.Message) -ForegroundColor DarkGray
    }
}

# 설치할 패키지 - 서비스 이름, 패키지 폴더
if ($Mode -eq 'Known') {
    $Plan = @(
        [pscustomobject]@{ Service = 'PalmRawUsbTap';   Folder = 'PalmRawUsbTap';        Inf = 'palmrawusbtap';   Sys = 'PalmRawUsbTap.sys'   },
        [pscustomobject]@{ Service = 'PalmRejFilter';   Folder = 'PalmRejFilter';        Inf = 'palmrejfilter';   Sys = 'PalmRejFilter.sys'   },
        [pscustomobject]@{ Service = 'PalmRejPipeline'; Folder = 'PalmRejPipeline';      Inf = 'palmrejpipeline'; Sys = 'PalmRejPipeline.sys' }
    )
} else {
    # 파이프라인과 USB 탭은 Cintiq Pro 24 의 제품 번호에 묶여 있다 (소스는 소스\ 에 있지만 다른 기종에 맞춰 본 적이 없다).
    # 다른 기종에는 손날 인식 필터만 간다.
    $Plan = @(
        [pscustomobject]@{ Service = 'PalmRejFilter';   Folder = 'PalmRejFilter_Compat'; Inf = 'palmrejfilter';   Sys = 'PalmRejFilter.sys'   }
    )
}

function Read-InfText([string]$p) {
    $raw = [IO.File]::ReadAllBytes($p)
    if ($raw.Length -ge 2 -and $raw[0] -eq 0xFF -and $raw[1] -eq 0xFE) { return [Text.Encoding]::Unicode.GetString($raw, 2, $raw.Length - 2) }
    return [Text.Encoding]::UTF8.GetString($raw)
}

# 서명 확인. 다른 컴퓨터에서는 인증서를 아직 믿지 않으므로 Valid 가 아니라 "믿을 수 없는
# 루트" 로 나온다. 그때는 서명자가 함께 든 인증서와 같은지만 보고, 등록한 다음 [3/6] 에서
# 다시 Valid 인지 확인한다. 내용이 바뀐 패키지(HashMismatch)는 어느 경우든 멈춘다.
$BundledCert = Get-BundledCert
if ($Distribution -and -not $BundledCert) { Fail "인증서\ 폴더에 서명 인증서(.cer)가 없습니다. 배포판이 온전하지 않습니다." }
$needTrust = $false

foreach ($x in $Plan) {
    $d = Join-Path $PkgRoot $x.Folder
    $inf = Get-ChildItem -LiteralPath $d -Filter *.inf -ErrorAction SilentlyContinue | Select-Object -First 1
    $cat = Get-ChildItem -LiteralPath $d -Filter *.cat -ErrorAction SilentlyContinue | Select-Object -First 1
    $sys = Get-ChildItem -LiteralPath $d -Filter *.sys -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $inf -or -not $cat -or -not $sys) { Fail "패키지가 온전하지 않습니다: $d" }
    $sig = Get-AuthenticodeSignature -LiteralPath $cat.FullName
    if ($sig.Status -ne "Valid") {
        $ours = $BundledCert -and $sig.SignerCertificate -and ($sig.SignerCertificate.Thumbprint -eq $BundledCert.Thumbprint)
        if ($ours -and ([string]$sig.Status -eq 'UnknownError')) { $needTrust = $true }   # 1.0.1: NotTrusted(명시적으로 막힌 인증서)는 받지 않는다
        elseif ($BundledCert -and ([string]$sig.Status -eq 'NotTrusted') -and
                ($ours -or (-not $sig.SignerCertificate -and @(Find-DisallowedCert $BundledCert.Thumbprint).Count -gt 0))) {
            # 1.0.1-exp: 이 컴퓨터가 우리 인증서를 막아 둔 경우 - 어디서 풀면 되는지 알려 준다.
            $blockedIn = @(Find-DisallowedCert $BundledCert.Thumbprint)
            Write-Host ("    서명            NotTrusted: {0}\{1}, 막아 둔 곳 = {2}" -f $x.Folder, $cat.Name,
                        $(if ($blockedIn.Count -gt 0) { $blockedIn -join ", " } else { "찾지 못함" })) -ForegroundColor Yellow
            Fail -Heading "이 컴퓨터가 PalmRej 서명 인증서를 막아 두었습니다." -Message (Get-NotTrustedText -Where $blockedIn -Cert $BundledCert)
        }
        else { Fail "카탈로그 서명이 유효하지 않습니다 ($($sig.Status)): $($x.Folder)\$($cat.Name)" }
    }
    if ($BundledCert -and $sig.SignerCertificate -and ($sig.SignerCertificate.Thumbprint -ne $BundledCert.Thumbprint)) {
        Fail "패키지 서명자가 함께 든 인증서와 다릅니다: $($x.Folder)"
    }
    $x | Add-Member -NotePropertyName InfPath -NotePropertyValue $inf.FullName
    $x | Add-Member -NotePropertyName CatPath -NotePropertyValue $cat.FullName

    # 파이프라인 INF 가 디버깅 기능(KMDF Verifier)을 켜는 옛 판이면 멈춘다.
    if ($x.Service -eq 'PalmRejPipeline') {
        $vl = @([regex]::Matches((Read-InfText $inf.FullName), '(?m)^[^;\r\n]*Verifier[^\r\n]*') | ForEach-Object { $_.Value })
        if ($vl.Count -gt 0) { Fail ("파이프라인 패키지가 디버깅 기능을 켭니다: " + ($vl -join " / ")) }
    }

    # 필터 INF 가 붙는 이름표가 폴더와 맞는지 - 폴더가 뒤바뀌면 여기서 걸린다.
    if ($x.Service -eq 'PalmRejFilter') {
        $ids = @([regex]::Matches((Read-InfText $inf.FullName), '(?im)^[^;\r\n]*,\s*(HID\\VID_056A&[^\s;]+)\s*$') | ForEach-Object { $_.Groups[1].Value })
        if ($Mode -eq 'Compat') {
            $extra = @($ids | Where-Object { $CompatIds -notcontains $_ })
            if ($ids.Count -eq 0 -or $extra.Count -gt 0) { Fail ("다른 기종용 패키지의 이름표가 비교한 것과 다릅니다: " + ($ids -join ", ")) }
        } else {
            $broad = @($ids | Where-Object { $_ -notmatch '(?i)&PID_' })
            if ($ids.Count -eq 0 -or $broad.Count -gt 0) { Fail ("Cintiq Pro 24 용 필터 패키지의 이름표가 이상합니다: " + ($ids -join ", ")) }
        }
    }
}
Write-Host ("    패키지 {0}개        온전함, 서명 {1}  ({2})" -f $Plan.Count,
    $(if ($needTrust) { "PalmRej 인증서 (등록 후 Valid)" } else { "Valid" }),
    (($Plan | ForEach-Object { $_.Folder }) -join ", "))
if ($Distribution) {
    Write-Host ("    배포판           인증서 {0}, 관리 앱 -> {1}" -f $BundledCert.Thumbprint.Substring(0, 8), $ProgDir)
}

if ($DryRun) {
    Write-Host ""
    Write-Host ("시험 실행이라 여기서 멈춥니다. 실제로는 이어서 설치할 패키지: " + (($Plan | ForEach-Object { $_.Folder }) -join ", ")) -ForegroundColor Cyan
    Write-Host ("MODE={0}" -f $Mode)
    Write-Host ("DISTRIBUTION={0} NEEDTRUST={1}" -f $Distribution, $needTrust)
    exit 0
}

# ------------------------------------------------------------------
Write-Step 2 6 "테스트 서명 On"

# 이미 켜져 있으면 부팅 설정이 바뀌지 않으므로 BitLocker 를 건드릴 이유가 없다.
if (-not $tsOn) {
    $bl = Suspend-BitLockerOnce
    switch ($bl) {
        'suspended' { Write-Host "    BitLocker  다음 재부팅 한 번만 보호를 멈췄습니다 (복구 키를 묻지 않게)" -ForegroundColor Green }
        'none'      { Write-Host "    BitLocker  없음" }
        'unknown'   { Write-Host "    BitLocker  상태를 확인하지 못했습니다. 쓰고 있다면 다음 부팅에 복구 키를 물을 수 있습니다" -ForegroundColor Yellow }
        'fail'      {
            Fail ("BitLocker 가 켜져 있는데 보호를 잠시 멈추지 못했습니다. 이대로 테스트 모드를 켜면 다음 부팅에 복구 키를 묻습니다.`n" +
                  "아무것도 바꾸지 않았습니다.")
        }
    }
}

$o = (& bcdedit.exe /set "{current}" testsigning on 2>&1 | Out-String)
if ($LASTEXITCODE -ne 0) {
    Write-Host ("    " + $o.Trim())
    Fail ("테스트 서명을 켜지 못했습니다. 보안 부팅(Secure Boot)이 켜져 있으면 BIOS 에서 꺼야 합니다.`n" +
          "아무것도 설치하지 않았습니다.")
}
$state = Read-TestSigning
Write-Host ("    testsigning = {0}" -f $(if ($state) { $state } else { "(읽지 못함)" }))

# ------------------------------------------------------------------
Write-Step 3 6 "패키지 설치"

# 서명 인증서를 이 컴퓨터가 믿도록 등록한다 (신뢰할 수 있는 루트 + 신뢰할 수 있는 게시자).
# 코드 서명 전용 인증서라 웹사이트나 다른 용도에는 쓰일 수 없다. 원래 없던 곳에 넣은 것만
# 기록해 두고, 앱 제거가 그것만 뺀다 - 원래 있던 컴퓨터(개발 PC)에서는 건드리지 않는다.
$CertAddedTo = @()
if ($BundledCert) {
    foreach ($storeName in 'Root','TrustedPublisher') {
        if (Test-CertInStore $storeName $BundledCert.Thumbprint) {
            Write-Host ("    인증서  {0,-17} 이미 있음" -f $storeName)
            continue
        }
        $s = New-Object System.Security.Cryptography.X509Certificates.X509Store($storeName, 'LocalMachine')
        try {
            $s.Open('ReadWrite')
            $s.Add($BundledCert)
        } catch {
            Fail ("서명 인증서를 등록하지 못했습니다 ($storeName): " + $_.Exception.Message + "`n`n" +
                  "드라이버는 설치하지 않았습니다. Windows 테스트 모드는 켜졌을 수 있습니다. " +
                  "재부팅한 다음 설치를 다시 해 보세요.")
        } finally { $s.Close() }
        $CertAddedTo += $storeName
        Write-Host ("    인증서  {0,-17} 등록함" -f $storeName) -ForegroundColor Green
    }
    # 이제는 Valid 여야 한다. 아니면 설치해도 Windows 가 경고 창을 띄운다.
    foreach ($x in $Plan) {
        $st = (Get-AuthenticodeSignature -LiteralPath $x.CatPath).Status
        if ($st -ne "Valid") { Fail "인증서를 등록했는데도 서명이 유효하지 않습니다 ($st): $($x.Folder)" }
    }
    Write-Host "    서명    모든 패키지 Valid"
}

# 이 판의 버전. 덮어쓰기에서 옛 판을 가려내고, 설치가 됐는지 확인하는 데 쓴다.
$ourVer = ''
$mv = [regex]::Match((Read-InfText $Plan[0].InfPath), '(?im)^\s*DriverVer\s*=\s*[^,\r\n]+,\s*([\d.]+)')
if ($mv.Success) { $ourVer = $mv.Groups[1].Value }

# 같은 판이 이미 다 들어 있으면 드라이버는 바뀌지 않는다 (관리 앱만 새로 까는 경우).
# 1.0.1-exp: 번호(DriverVer)만 같아서는 안 되고, 저장소의 그 패키지가 이 판과 같은 빌드여야 한다.
# 같은 빌드인지 모르면 "다름" 으로 친다 - 재부팅하라는 안내만 더 나간다.
$sameAlready = $false
if ($Upgrade -and $ourVer) {
    $sameAlready = $true
    foreach ($x in $Plan) {
        $infName = (Split-Path $x.InfPath -Leaf).ToLowerInvariant()
        $sameVer = @($existing | Where-Object { $_.Original -eq $infName -and $_.Version -eq $ourVer })
        $sameBuild = @()
        foreach ($p in $sameVer) {
            $r = Test-SameBuild $p $x
            Write-Host ("    같은 번호        {0} ({1}, {2}) - {3}" -f $p.Published, $p.Original, $p.Version, (Format-SameBuild $r))
            if ($r -eq $true) { $sameBuild += $p }
        }
        if ($sameBuild.Count -eq 0) { $sameAlready = $false }
    }
}

# 하나씩 넣고 바로 확인한다. 순서는 $Plan 그대로 탭 -> 필터 -> 파이프라인.
# 설치가 됐는지는 pnputil 의 종료 코드가 아니라 "이 판이 저장소에 들어갔는가" 로 본다.
# 같은 판을 다시 깔거나 옛 판으로 되돌릴 때는 바꿀 장치가 없다는 이유로 0 도 3010 도
# 아닌 값이 올 수 있는데, 그것도 패키지는 들어간 정상이다.
#
# 파이프라인 패키지는 주 터치 창구(COL02)에 필터와 파이프라인 두 이름을 함께 적는데
# (palmrejpipeline.inf 45줄), 필터 서비스는 필터 패키지가 만든다 (53줄 AddService 는 파이프라인만).
# 필터가 안 들어갔는데 파이프라인을 넣으면 COL02 가 없는 서비스를 찾다가 재부팅 뒤 터치가
# 시작하지 못하고, 재부팅을 더 해도 풀리지 않는다. 덮어쓰기에서는 아래 옛 판 정리가 옛 필터까지
# 지워서 더 확실하게 그렇게 된다. 그래서 필터가 안 들어가면 파이프라인도 넣지 않고, 옛 판도
# 지우지 않고 멈춘다.
# 1.0.1-exp: 번호가 같아도 저장소의 그것이 이 판과 다른 빌드면 "들어갔다" 로 치지 않는다 (같은 번호의 옛 빌드가
# 남고 이 판이 안 들어간 경우). 같은 빌드인지 모르면(저장소 폴더를 못 읽음) 예전처럼 번호만으로 친다 -
# 모른다는 이유로 필터 설치 실패로 멈추지 않게.
function Test-OurPackageInStore($PlanItem) {
    $InfName = (Split-Path $PlanItem.InfPath -Leaf).ToLowerInvariant()
    return (@(Get-PalmPackages | Where-Object { $_.Original -eq $InfName -and ($ourVer -eq '' -or $_.Version -eq $ourVer) -and
                                                ((Test-SameBuild $_ $PlanItem) -ne $false) }).Count -gt 0)
}
$installed = 0
$landed = @()     # 저장소에 들어간 이 판의 INF 이름 (소문자)
$missed = @()     # 들어가지 않은 것의 서비스 이름
foreach ($x in $Plan) {
    $infName = (Split-Path $x.InfPath -Leaf).ToLowerInvariant()
    if ($x.Service -eq 'PalmRejPipeline' -and $missed -contains 'PalmRejFilter') {
        Write-Host ("    넣지 않음  {0}\{1}  (필터가 들어가지 않아서)" -f $x.Folder, (Split-Path $x.InfPath -Leaf)) -ForegroundColor Yellow
        $missed += $x.Service
        continue
    }
    Write-Host ("    설치  {0}\{1}" -f $x.Folder, (Split-Path $x.InfPath -Leaf))
    $o = (& pnputil.exe /add-driver $x.InfPath /install 2>&1 | Out-String)
    $code = $LASTEXITCODE
    Write-Host ("        " + ($o.Trim() -replace "`r?`n","`n        "))
    Write-Host ("        (pnputil 종료 코드 {0})" -f $code) -ForegroundColor DarkGray
    if (Test-OurPackageInStore $x) {
        $installed++
        $landed += $infName
    } else {
        Write-Host ("    저장소에 없음  {0} {1}" -f $infName, $ourVer) -ForegroundColor Yellow
        $missed += $x.Service
    }
}
if ($installed -eq 0) {
    Fail ("드라이버를 하나도 설치하지 못했습니다. 이유는 $SeeOutput 에 있습니다.`n`n" +
          "Windows 테스트 모드는 켜졌을 수 있습니다. 재부팅한 다음 설치를 다시 해 보세요.")
}
if ($missed -contains 'PalmRejFilter') {
    Fail ("손날 인식 필터를 설치하지 못해서, 파이프라인은 넣지 않고 멈췄습니다. 이유는 $SeeOutput 에 있습니다.`n`n" +
          "필터 없이 파이프라인을 넣으면 재부팅 뒤 터치가 멈추기 때문입니다. " +
          $(if ($Upgrade) { "원래 설치돼 있던 판은 지우지 않았습니다. " } else { "" }) +
          "Windows 테스트 모드는 켜졌을 수 있습니다. 재부팅한 다음 설치를 다시 해 보세요.")
}
Write-Host ("    설치됨 {0}/{1}" -f $installed, $Plan.Count)
if ($missed.Count -gt 0) {
    Write-Host ("    안 들어간 것  {0}" -f ($missed -join ", ")) -ForegroundColor Yellow
}

& pnputil.exe /scan-devices 2>&1 | Out-Null

# 덮어쓰기: 이 판과 버전이 다른 Palm 패키지를 지운다 (1.0.1-exp 부터 같은 번호의 다른 빌드도). /uninstall 은 그 패키지를 쓰던
# 장치를 남은 것 중 가장 알맞은 드라이버(= 방금 넣은 이 판)로 다시 설치하게 한다.
# 못 지워도 멈추지 않는다 - 새 판이 우선이라 동작은 같고, 다음 OFF 때 같이 지워진다.
# 이 판에서 안 들어간 패키지의 옛 판은 지우지 않는다 - 그것까지 지우면 그 자리가 빈다.
$oldDeleted = 0
$sameVerLeft = 0   # 1.0.1-exp: 못 지운 "같은 번호의 다른 빌드"
if ($Upgrade) {
    if (-not $ourVer) {
        Write-Host "    옛 판 정리  이 판의 버전을 읽지 못해 건너뜀" -ForegroundColor Yellow
    } else {
        $planInfs = @($Plan | ForEach-Object { (Split-Path $_.InfPath -Leaf).ToLowerInvariant() })
        # 1.0.1-exp: 번호가 이 판과 같아도, 이 판에 같은 INF 가 있고 파일 내용이 다르면(같은 번호로 다시 빌드한 것)
        # 옛 판으로 친다. 같은 빌드인지 모르는 것은 지우지 않는다.
        $nowPkgs = @(Get-PalmPackages)
        foreach ($p in $nowPkgs) {
            $r = $null; $checked = $false
            if ($p.Version -eq $ourVer) {
                $pi = Get-PlanItemFor $p.Original
                if ($null -ne $pi) { $checked = $true; $r = Test-SameBuild $p $pi }
            }
            $p | Add-Member -NotePropertyName SameBuild -NotePropertyValue $r -Force
            $p | Add-Member -NotePropertyName VerText -NotePropertyValue $(if ($checked -and $r -eq $false) { "$($p.Version), 다른 빌드" } else { $p.Version }) -Force
            if ($checked -and $null -eq $r) {
                Write-Host ("    같은 번호 확인 못 함 {0} ({1}) - 이 판과 같은 빌드인지 몰라 지우지 않습니다" -f $p.Published, $p.Version) -ForegroundColor Yellow
            }
        }
        $allOld  = @($nowPkgs | Where-Object { $_.Version -ne $ourVer -or $_.SameBuild -eq $false })
        $oldPkgs = @($allOld | Where-Object { ($planInfs -notcontains $_.Original) -or ($landed -contains $_.Original) })
        foreach ($p in @($allOld | Where-Object { ($planInfs -contains $_.Original) -and ($landed -notcontains $_.Original) })) {
            Write-Host ("    옛 판 둠    {0} ({1}) - 이 판의 {2} 가 안 들어가서 지우지 않습니다" -f $p.Published, $p.VerText, $p.Original) -ForegroundColor Yellow
        }
        if ($allOld.Count -eq 0) { Write-Host ("    옛 판 정리  지울 것 없음 (모두 {0})" -f $ourVer) }
        foreach ($p in $oldPkgs) {
            $o = (& pnputil.exe /delete-driver $p.Published /uninstall 2>&1 | Out-String)
            $code = $LASTEXITCODE
            if ($code -eq 0 -or $code -eq 3010) {
                $oldDeleted++
                Write-Host ("    옛 판 지움  {0} ({1})" -f $p.Published, $p.VerText) -ForegroundColor Green
            } elseif ($p.SameBuild -eq $false) {
                # 같은 번호의 다른 빌드는 Windows 가 어느 쪽을 고를지 정해져 있지 않다 - "재부팅 필요 없음" 이라고 하지 않는다.
                $sameVerLeft++
                Write-Host ("    옛 판 남음  {0} ({1}) - 번호가 같아서 어느 빌드가 쓰일지 정해져 있지 않습니다. 다음 OFF 때 지워집니다." -f $p.Published, $p.VerText) -ForegroundColor Yellow
                Write-Host ("        " + ($o.Trim() -replace "`r?`n","`n        "))
            } else {
                Write-Host ("    옛 판 남음  {0} ({1}) - 새 판이 우선이라 동작은 같습니다. 다음 OFF 때 지워집니다." -f $p.Published, $p.VerText) -ForegroundColor Yellow
                Write-Host ("        " + ($o.Trim() -replace "`r?`n","`n        "))
            }
        }
    }
}

# ------------------------------------------------------------------
Write-Step 4 6 "서비스 키 확인"

# 첫 부팅에서 터치 장치(COL02)가 PalmRejPipeline 을 필터로 달고 시작하는데, 그 서비스
# 키를 Windows 가 재부팅 도중에야 만들면 장치가 "없는 필터"를 찾다 오류 19 로 멈춘다.
# 그래서 재부팅을 두 번 해야 했다. 없으면 미리 만든다 - 값 네 개는 Windows 가 만드는
# 것과 대조해 확인한 그대로이고, 나머지는 Windows 가 재부팅 때 채운다.
$repo = "C:\Windows\System32\DriverStore\FileRepository"
foreach ($svc in $Plan) {
    $key = "HKLM:\SYSTEM\CurrentControlSet\Services\$($svc.Service)"
    if ($missed -contains $svc.Service) {
        Write-Host ("    건너뜀  {0,-16} 이 판의 패키지가 안 들어가서 만들지도 고치지도 않음" -f $svc.Service) -ForegroundColor Yellow
        continue
    }
    if (Test-Path -LiteralPath $key) {
        $img = (Get-ItemProperty -LiteralPath $key -ErrorAction SilentlyContinue).PSObject.Properties["ImagePath"]
        $imgText = if ($img) { [string]$img.Value } else { "" }
        Write-Host ("    있음    {0,-16} {1}" -f $svc.Service, $(if ($imgText) { $imgText } else { "(ImagePath 없음)" }))

        # 덮어쓴 경우: 서비스가 가리키는 파일이 이 판의 파일인지 본다. 옛 판을 지운 뒤라
        # 옛 폴더를 가리키고 있으면 다음 부팅에 드라이버가 안 뜬다. 이 판의 파일과 내용이
        # 같은 저장소 폴더를 찾아 그쪽으로 고친다.
        $ourSys = Join-Path (Split-Path $svc.InfPath -Parent) $svc.Sys
        $ourHash = (Get-FileHash -LiteralPath $ourSys).Hash
        $cur = $imgText -replace '^\\SystemRoot\\', ($env:SystemRoot + '\')
        $curOk = ($cur -and (Test-Path -LiteralPath $cur) -and ((Get-FileHash -LiteralPath $cur).Hash -eq $ourHash))
        if (-not $curOk) {
            $match = Get-ChildItem -LiteralPath $repo -Directory -ErrorAction SilentlyContinue |
                     Where-Object { $_.Name -like ($svc.Inf + ".inf_*") -and (Test-Path (Join-Path $_.FullName $svc.Sys)) -and
                                    ((Get-FileHash -LiteralPath (Join-Path $_.FullName $svc.Sys)).Hash -eq $ourHash) } |
                     Sort-Object LastWriteTime -Descending | Select-Object -First 1
            if ($match) {
                $fixed = "\SystemRoot\System32\DriverStore\FileRepository\$($match.Name)\$($svc.Sys)"
                Set-ItemProperty -LiteralPath $key -Name "ImagePath" -Value $fixed
                Write-Host ("    고침    {0,-16} {1}" -f $svc.Service, $fixed) -ForegroundColor Green
            } else {
                Write-Host ("    확인 못 함 {0,-13} 이 판의 파일을 저장소에서 찾지 못했습니다" -f $svc.Service) -ForegroundColor Yellow
            }
        } else {
            Write-Host ("            {0,-16} 이 판의 파일 맞음" -f "")
        }
        continue
    }
    $found = Get-ChildItem -LiteralPath $repo -Directory -ErrorAction SilentlyContinue |
             Where-Object { $_.Name -like ($svc.Inf + ".inf_*") -and (Test-Path (Join-Path $_.FullName $svc.Sys)) } |
             Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if (-not $found) {
        Write-Host ("    없음    {0,-16} 저장소에서 패키지를 못 찾아 만들지 않음 (재부팅 두 번 필요할 수 있음)" -f $svc.Service) -ForegroundColor Yellow
        continue
    }
    $imgPath = "\SystemRoot\System32\DriverStore\FileRepository\$($found.Name)\$($svc.Sys)"
    try {
        New-Item -Path $key -Force | Out-Null
        New-ItemProperty -LiteralPath $key -Name "Type"         -Value 1 -PropertyType DWord -Force | Out-Null
        New-ItemProperty -LiteralPath $key -Name "Start"        -Value 3 -PropertyType DWord -Force | Out-Null
        New-ItemProperty -LiteralPath $key -Name "ErrorControl" -Value 1 -PropertyType DWord -Force | Out-Null
        New-ItemProperty -LiteralPath $key -Name "ImagePath"    -Value $imgPath -PropertyType ExpandString -Force | Out-Null
        Write-Host ("    만듦    {0,-16} {1}" -f $svc.Service, $imgPath) -ForegroundColor Green
    } catch {
        Write-Host ("    실패    {0,-16} {1}" -f $svc.Service, $_.Exception.Message) -ForegroundColor Yellow
    }
}

# 0.1.3 부터 파이프라인의 디버깅 기능(KMDF Verifier)을 쓰지 않는다. 새 INF 는 켜지 않지만,
# 옛 설치의 서비스 키가 남아 있으면 그 값도 같이 남는다. 있으면 지운다.
$wdf = "HKLM:\SYSTEM\CurrentControlSet\Services\PalmRejPipeline\Parameters\Wdf"
if (Test-Path -LiteralPath $wdf) {
    $vo = (Get-ItemProperty -LiteralPath $wdf -ErrorAction SilentlyContinue).PSObject.Properties["VerifierOn"]
    if ($vo) {
        Remove-ItemProperty -LiteralPath $wdf -Name "VerifierOn" -Force
        Write-Host ("    지움    PalmRejPipeline  디버깅 기능 (VerifierOn={0})" -f $vo.Value) -ForegroundColor Green
    } else {
        Write-Host "    꺼짐    PalmRejPipeline  디버깅 기능"
    }
}

# ------------------------------------------------------------------
Write-Step 5 6 "관리 앱 설치 · 옛 빌드 보관"

$appInstalled = $false
$exePath = Join-Path $ProgDir $AppExe
$shortcuts = @()
if ($Distribution) {
    try {
        New-Item -ItemType Directory -Force -Path (Join-Path $ProgDir "드라이버 ON-OFF") | Out-Null
        # 앱, 아이콘, 제거 스크립트
        foreach ($f in @(Get-ChildItem -LiteralPath $ProgSrc -File)) {
            Copy-Item -LiteralPath $f.FullName -Destination (Join-Path $ProgDir $f.Name) -Force
        }
        # ON/OFF 스크립트만 바꿔 넣는다. 백업\ 과 실행기록\ 은 그대로 둔다 - 다시 설치해도
        # 전에 OFF 로 백업해 둔 것으로 되돌릴 수 있게.
        foreach ($f in @(Get-ChildItem -LiteralPath (Join-Path $ProgSrc "드라이버 ON-OFF") -File)) {
            Copy-Item -LiteralPath $f.FullName -Destination (Join-Path (Join-Path $ProgDir "드라이버 ON-OFF") $f.Name) -Force
        }
        # 인터넷에서 받은 zip 에서 나온 파일에는 "인터넷에서 온 파일" 표시가 붙어 있어서
        # 앱을 열 때마다 SmartScreen 경고가 뜬다. 설치한 사본에서만 떼어 낸다.
        Get-ChildItem -LiteralPath $ProgDir -Recurse -File | Unblock-File -ErrorAction SilentlyContinue
        if (-not (Test-Path -LiteralPath $exePath)) { throw "$AppExe 가 복사되지 않았습니다" }
        $appInstalled = $true
        Write-Host ("    관리 앱  {0}" -f $exePath) -ForegroundColor Green
    } catch {
        Write-Host ("    관리 앱  설치 실패: " + $_.Exception.Message) -ForegroundColor Yellow
    }

    if ($appInstalled) {
        $ws = New-Object -ComObject WScript.Shell
        foreach ($dir in @([Environment]::GetFolderPath('Desktop'), [Environment]::GetFolderPath('CommonPrograms'))) {
            if ([string]::IsNullOrWhiteSpace($dir)) { continue }
            try {
                $lnkPath = Join-Path $dir "PalmRej 관리.lnk"
                $lnk = $ws.CreateShortcut($lnkPath)
                $lnk.TargetPath = $exePath
                $lnk.WorkingDirectory = $ProgDir
                $lnk.IconLocation = "$exePath,0"
                $lnk.Description = "PalmRej 상태 확인과 드라이버 ON/OFF"
                $lnk.Save()
                $shortcuts += $lnkPath
                Write-Host ("    바로가기 {0}" -f $lnkPath) -ForegroundColor Green
            } catch {
                Write-Host ("    바로가기 만들지 못함: {0} ({1})" -f $dir, $_.Exception.Message) -ForegroundColor Yellow
            }
        }

        # 설치 정보 - 앱 제거가 무엇을 되돌릴지 여기서 읽는다. 다시 설치하면 인증서가 이미
        # 있어서 이번에는 "등록함" 이 비는데, 처음 설치 때 등록한 기록을 잃지 않게 합친다.
        $infoPath = Join-Path $ProgDir "설치정보.json"
        $prevAdded = @()
        if (Test-Path -LiteralPath $infoPath) {
            try {
                $prev = Get-Content -LiteralPath $infoPath -Raw -Encoding UTF8 | ConvertFrom-Json
                if ($prev.CertThumbprint -eq $BundledCert.Thumbprint) { $prevAdded = @($prev.CertAddedTo) }
            } catch { }
        }
        $info = [ordered]@{
            Version        = $Ver
            InstalledAt    = (Get-Date).ToString("o")
            CertThumbprint = $BundledCert.Thumbprint
            CertAddedTo    = @(@($prevAdded) + @($CertAddedTo) | Where-Object { $_ } | Select-Object -Unique)
            Shortcuts      = @($shortcuts)
        }
        [IO.File]::WriteAllText($infoPath, ($info | ConvertTo-Json -Depth 4), (New-Object Text.UTF8Encoding($true)))

        # 설정 > 앱 목록의 "제거" 항목
        $un = "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PalmRej"
        try {
            New-Item -Path $un -Force | Out-Null
            $unScript = Join-Path $ProgDir "uninstall.ps1"
            Set-ItemProperty -LiteralPath $un -Name DisplayName     -Value "PalmRej (Wacom 손날 인식)"
            Set-ItemProperty -LiteralPath $un -Name DisplayVersion  -Value $Ver
            Set-ItemProperty -LiteralPath $un -Name Publisher       -Value "PalmRej"
            Set-ItemProperty -LiteralPath $un -Name InstallLocation -Value $ProgDir
            Set-ItemProperty -LiteralPath $un -Name DisplayIcon     -Value $exePath
            # 0.1.13 부터 제거도 설치 창으로 한다 (콘솔 없음). 창이 없으면 예전처럼 스크립트를 바로.
            $unExe = Join-Path $ProgDir "PalmRej 설치.exe"
            $unCmd = if (Test-Path -LiteralPath $unExe) { "`"$unExe`" /uninstall" }
                     else { "powershell.exe -NoProfile -ExecutionPolicy Bypass -File `"$unScript`"" }
            Set-ItemProperty -LiteralPath $un -Name UninstallString -Value $unCmd
            New-ItemProperty -LiteralPath $un -Name NoModify -Value 1 -PropertyType DWord -Force | Out-Null
            New-ItemProperty -LiteralPath $un -Name NoRepair -Value 1 -PropertyType DWord -Force | Out-Null
            Write-Host "    앱 목록  설정 > 앱 에 'PalmRej' 제거 항목을 만듦" -ForegroundColor Green
        } catch {
            Write-Host ("    앱 목록  만들지 못함: " + $_.Exception.Message) -ForegroundColor Yellow
        }
    }
}

# 드라이버 저장소의 파일을 씁니다. drivers 폴더에 쌓인 옛 빌드(A4xx, A5xx)는
# 아무것도 가리키지 않습니다. 지우지 않고 옮깁니다 - 되돌릴 수 있게.
$arch = Join-Path (Split-Path $Root -Parent) "보관\drivers_옛빌드"
$moved = 0; $kept = 0
$old = @(Get-ChildItem -LiteralPath "C:\Windows\System32\drivers" -File -ErrorAction SilentlyContinue |
         Where-Object { $_.Name -like 'PalmRejFilter_*.sys' -or $_.Name -like 'PalmRawUsbTap_*.sys' })
if ($old.Count -gt 0) {
    New-Item -ItemType Directory -Force -Path $arch | Out-Null
    foreach ($f in $old) {
        try {
            Move-Item -LiteralPath $f.FullName -Destination (Join-Path $arch $f.Name) -Force
            $moved++
        } catch {
            # 아직 메모리에 올라가 있는 파일은 못 옮긴다. 해가 없다.
            $kept++
        }
    }
}
if ($old.Count -gt 0) { Write-Host ("    옛 빌드  옮김 {0}개,  못 옮김 {1}개" -f $moved, $kept) }

# ------------------------------------------------------------------
Write-Step 6 6 "확인"

$ok = $true
if ($missed.Count -gt 0) {
    Write-Host ("    패키지  안 들어간 것 {0}" -f ($missed -join ", ")) -ForegroundColor Yellow
    $ok = $false
}
foreach ($x in $Plan) {
    $key = "HKLM:\SYSTEM\CurrentControlSet\Services\$($x.Service)"
    if (Test-Path -LiteralPath $key) {
        Write-Host ("    서비스  {0,-16} 있음" -f $x.Service) -ForegroundColor Green
    } else {
        Write-Host ("    서비스  {0,-16} 없음" -f $x.Service) -ForegroundColor Yellow
        $ok = $false
    }
}

if (Test-Path -LiteralPath $wdf) {
    if ((Get-ItemProperty -LiteralPath $wdf -ErrorAction SilentlyContinue).PSObject.Properties["VerifierOn"]) {
        Write-Host "    디버깅  PalmRejPipeline  아직 켜져 있음" -ForegroundColor Yellow
        $ok = $false
    } else {
        Write-Host "    디버깅  PalmRejPipeline  꺼짐" -ForegroundColor Green
    }
}

# 같은 판을 다시 깐 경우 (관리 앱만 새로 들어감) 드라이버는 그대로라 재부팅이 필요 없다.
$noReboot = ($sameAlready -and $oldDeleted -eq 0 -and $sameVerLeft -eq 0 -and $ok)
Send-Gui REBOOT @($(if ($noReboot) { '0' } else { '1' }))

Write-Host ""
Write-Host "============================================================"
if ($noReboot) {
    Write-Host " 설치 완료. 같은 판이 이미 돌고 있어서 드라이버는 그대로입니다 - 재부팅 필요 없음." -ForegroundColor Cyan
} else {
    Write-Host " 설치 완료. 재부팅하면 $Title 이 올라옵니다." -ForegroundColor Cyan
}
# 테스트 모드가 꺼진 부팅에서 처음 설치하면 [3/6] 의 pnputil /install 이 드라이버를 지금 바로
# 장치에 붙이고, Windows 가 서명 때문에 거부해서(코드 52) 재부팅 전까지 터치 USB 장치와 펜의
# 와콤 전용 창구(COL02)가 멈춘다 (2026-09-21 08:32, 켜기에서 실제로 확인). 재부팅하면 풀린다.
$pausedNow = (-not $bootTestSigning) -and (-not $noReboot)
$pauseText = "재부팅 전까지 터치와 펜 일부가 멈춰 있습니다. 지금 재부팅하세요."
if ($pausedNow) { Write-Host (" " + $pauseText) -ForegroundColor Yellow }
Write-Host "============================================================"

try { Stop-Transcript | Out-Null } catch { }

$note = ""
if ($missed.Count -gt 0) {
    $note += ("`n`n일부 드라이버를 설치하지 못했습니다 (" + ($missed -join ", ") + "). 이유는 $SeeOutput 에 있습니다. " +
              "재부팅한 다음 설치를 다시 해 보세요.")
} elseif (-not $ok) { $note += "`n`n마무리 확인에서 걸린 항목이 있습니다 ($SeeOutput). 재부팅 후 터치가 안 되면 한 번 더 재부팅하세요." }
if ($appInstalled) { $note += "`n`n바탕화면의 'PalmRej 관리' 로 상태를 보고 드라이버를 켜고 끌 수 있습니다." }
elseif ($Distribution) { $note += "`n`n관리 앱은 설치하지 못했습니다 ($SeeOutput). 드라이버는 설치됐습니다." }

if ($Mode -eq 'Known' -and $noReboot) {
    Show-Box -Caption $Title -Icon "Information" -Text (
        "$Title 설치를 마쳤습니다.`n`n" +
        "같은 판이 이미 돌고 있어서 드라이버는 바뀌지 않았습니다.`n" +
        "재부팅하지 않아도 됩니다." + $note + (LogLine))
} elseif ($Mode -eq 'Known') {
    Show-Box -Caption $Title -Icon "Information" -Text (
        "$Title 설치를 마쳤습니다.`n`n" +
        $(if ($pausedNow) { $pauseText + "`n" } else { "재부팅하면 적용됩니다.`n" }) +
        "재부팅 후 터치가 안 되면 타블렛을 한 번 껐다 켜세요 (설치 뒤 첫 부팅에만 가끔 생깁니다)." +
        $note + (LogLine))
} else {
    Show-Box -Caption "$Title - 확인되지 않은 기종에 설치함" -Icon "Warning" -Text (
        "$Title 설치를 마쳤습니다.`n`n" +
        $(if ($pausedNow) { $pauseText + " " } else { "" }) +
        "재부팅하면 적용됩니다. 구조가 Cintiq Pro 24 와 같아서 손날 인식 필터만 설치했습니다. " +
        "손날 직후 탭·드래그를 살려 주는 부분은 Cintiq Pro 24 전용이라 빠집니다.`n`n" +
        "재부팅 후 터치나 펜이 이상하면 관리 앱에서 'PalmRej 끄기'를 누르고 재부팅하세요. " +
        "드라이버가 내려가고 와콤 기본 드라이버로 돌아갑니다." + $note + (LogLine))
}
exit 0
