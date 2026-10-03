#requires -Version 5.1
<#
드라이버 OFF - Palm 드라이버 패키지를 통째로 제거하고 테스트 서명을 끕니다.

지난번 실패 원인:
  PalmRawUsbTap 은 확장 INF(Extension INF)로 설치되어 있고, 자기 등록을
  UpperFilters / LowerFilters 값이 아니라
      ...\Enum\USB\VID_056A&PID_0355\<인스턴스>\Filters\*Lower
  라는 하위 키에 남깁니다. 예전 스크립트는 이 형태를 보지 않아서
  "남은 Palm 등록 없음" 이라고 보고한 뒤 테스트 서명을 껐고,
  그 결과 터치 장치가 CM_PROB_UNSIGNED_DRIVER 로 죽었습니다.

이번에는 등록을 지우는 대신 드라이버 패키지 자체를 제거합니다.
패키지가 없으면 다시 붙을 수도 없습니다.

순서:
  1) 드라이버 저장소의 Palm 패키지를 백업 폴더로 복사
  2) 현재 등록 상태 전부 기록 (두 가지 형태 모두)
  3) pnputil 로 Palm 패키지 제거
  4) 남은 등록 직접 정리
  5) 확인 - 하나라도 남아 있으면 서비스도 테스트 서명도 건드리지 않고 중단.
     확인이 통과한 뒤에만 서비스를 '사용 안 함'(Start=4)으로 바꿈
  6) 테스트 서명 Off

종료 코드 (관리 앱과 uninstall.ps1 이 이것으로 문구를 고른다):
  0  끝남 (재부팅하면 적용)
  1  실패 - 관리자 아님, pnputil 목록 없음, 백업 실패, 예상 못 한 오류
  3  드라이버는 내렸지만 테스트 모드를 끄지 못함
  4  드라이버는 내렸지만 BitLocker 때문에 테스트 모드를 끄지 않음
  5  [5/6] 에서 멈춤 - 등록이나 패키지가 남음. 패키지가 이미 지워졌으면 지금 재부팅할 때
     확장 INF 키(Filters\*Lower)가 없는 서비스를 가리켜 터치가 멈출 수 있다.
     재부팅하지 말고 한 번 더 눌러야 한다
#>

[CmdletBinding()]
param(
    [string]$BackupRoot = "",
    [switch]$Quiet
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# 백업 폴더 위치.
#
# param() 기본값 안에서 $PSScriptRoot 를 쓰면 이 환경에서는 빈 문자열이 나와서
# Join-Path 가 바로 실패한다 (2026-09-10 확인). 그래서 본문에서 계산하고,
# 못 구하면 두 가지 방법을 더 시도한다. 이 값이 틀리면 복구가 통째로 막힌다.
if ([string]::IsNullOrWhiteSpace($BackupRoot)) {
    $__root = $PSScriptRoot
    if ([string]::IsNullOrWhiteSpace($__root)) {
        try { $__root = Split-Path -Parent $MyInvocation.MyCommand.Path } catch { }
    }
    if ([string]::IsNullOrWhiteSpace($__root)) { $__root = (Get-Location).Path }
    $BackupRoot = Join-Path $__root "백업"
}
Write-Host ("백업 폴더 : " + $BackupRoot) -ForegroundColor DarkGray

$PalmServices = @("PalmRejFilter","PalmRawUsbTap","PalmRejPipeline")
$ScanRoots = @(
    "HKLM:\SYSTEM\CurrentControlSet\Enum\HID",
    "HKLM:\SYSTEM\CurrentControlSet\Enum\USB",
    "HKLM:\SYSTEM\CurrentControlSet\Control\Class"
)

Add-Type -AssemblyName System.Windows.Forms | Out-Null

# 실행 기록. 창을 닫아도 남으므로, 나중에 무슨 일이 있었는지 볼 수 있다.
$Script:LogDir = Join-Path $PSScriptRoot "실행기록"
if (-not (Test-Path -LiteralPath $Script:LogDir)) { New-Item -ItemType Directory -Force -Path $Script:LogDir | Out-Null }
$Script:LogPath = Join-Path $Script:LogDir ("실행기록_" + (Get-Date -Format "yyyyMMdd_HHmmss") + ".txt")
try { Start-Transcript -LiteralPath $Script:LogPath -Force | Out-Null } catch { }

function Done {
    param([string]$Message = "")
    try { Stop-Transcript | Out-Null } catch { }
    # A512: the app raises the dialog instead.
    if ($Quiet) { return }
    $t = "완료했습니다."
    if ($Message) { $t = $t + "`n`n" + $Message }
    $t = $t + "`n`n기록: " + $Script:LogPath
    [void][System.Windows.Forms.MessageBox]::Show(
        $t, "드라이버 OFF",
        [System.Windows.Forms.MessageBoxButtons]::OK,
        [System.Windows.Forms.MessageBoxIcon]::Information)
}

# 실패했을 때는 창을 닫지 않는다. 화면에 남은 내용이 원인을 찾는 유일한 단서인데
# 그걸 지워버리면 다시 재현하는 수밖에 없다. 알림만 띄우고 창은 그대로 둔다.
function Hold {
    # A512: no console to press Enter in.
    if ($Quiet) { return }
    Write-Host ""
    Write-Host "이 창을 닫으려면 Enter 를 누르세요." -ForegroundColor DarkGray
    [void][System.Console]::ReadLine()
}

function Fail {
    param([Parameter(Mandatory=$true)][string]$Message, [int]$Code = 1)
    Write-Host ""
    Write-Host "중단: $Message" -ForegroundColor Red
    try { Stop-Transcript | Out-Null } catch { }
    # A512: the app raises the dialog instead.
    if ($Quiet) { exit $Code }
    [void][System.Windows.Forms.MessageBox]::Show(
        ("실패했습니다." + "`n`n" + $Message + "`n`n" +
         "이 창은 닫지 마세요. 위에 나온 내용이 원인입니다." + "`n" +
         "기록: " + $Script:LogPath),
        "드라이버 OFF - 실패",
        [System.Windows.Forms.MessageBoxButtons]::OK,
        [System.Windows.Forms.MessageBoxIcon]::Error)
    Hold
    exit $Code
}

function Get-RegValueOrNull {
    param([string]$KeyPath, [string]$ValueName)
    $props = Get-ItemProperty -LiteralPath $KeyPath -ErrorAction SilentlyContinue
    if ($null -eq $props) { return $null }
    $m = $props.PSObject.Properties[$ValueName]
    if ($null -eq $m) { return $null }
    return $m.Value
}

# Palm 등록을 두 가지 형태 모두에서 찾는다.
#   형태 A  UpperFilters / LowerFilters  (REG_MULTI_SZ 값)
#   형태 B  ...\Filters\*Lower 또는 *Upper 하위 키의 값 이름
function Find-PalmRegistrations {
    $hits = @()
    foreach ($root in $ScanRoots) {
        if (-not (Test-Path -LiteralPath $root)) { continue }
        $keys = @(Get-ChildItem -LiteralPath $root -Recurse -ErrorAction SilentlyContinue)
        foreach ($key in $keys) {
            $clean = ($key.PSPath -replace 'Microsoft\.PowerShell\.Core\\Registry::','')

            foreach ($vn in @("UpperFilters","LowerFilters")) {
                $data = Get-RegValueOrNull -KeyPath $key.PSPath -ValueName $vn
                if ($null -eq $data) { continue }
                if ((@($data) -join ';') -notmatch 'Palm') { continue }
                $hits += [pscustomobject]@{
                    Form = "MULTISZ"; Key = $clean; ValueName = $vn
                    Data = @($data | ForEach-Object { [string]$_ })
                }
            }

            if ($key.PSChildName -eq '*Lower' -or $key.PSChildName -eq '*Upper') {
                $props = Get-ItemProperty -LiteralPath $key.PSPath -ErrorAction SilentlyContinue
                if ($null -eq $props) { continue }
                foreach ($pr in $props.PSObject.Properties) {
                    if ($pr.Name -like 'PS*') { continue }
                    if ($pr.Name -notmatch 'Palm') { continue }
                    $hits += [pscustomobject]@{
                        Form = "FILTERKEY"; Key = $clean; ValueName = $pr.Name
                        Data = @()
                    }
                }
            }
        }
    }
    return $hits
}

# ------------------------------------------------------------------
# 테스트 서명 끄기에 쓰는 도구들. 아래 두 곳에서 쓴다:
#   - 드라이버를 내린 뒤 [6/6]
#   - 드라이버는 이미 꺼져 있는데 테스트 서명만 켜져 있을 때
#     (예전에는 여기서 아무것도 안 하고 끝나서, 반쯤 실패한 OFF 뒤에 앱 버튼으로는
#      테스트 서명을 끌 방법이 없었다)

# bcdedit 이 말하는 testsigning 값. 못 읽으면 $null.
function Read-TestSigningState {
    foreach ($cmd in @(@("/enum","{current}"), @("/enum","ACTIVE"), @())) {
        $t = (& bcdedit.exe @cmd 2>&1 | Out-String)
        if ($t -match '(?im)^\s*testsigning\s+(\S+)') { return $Matches[1] }
        if ($t -match '(?im)^\s*(Windows 부팅 로더|Windows Boot Loader)') { return "(testsigning 줄 없음 = Off)" }
    }
    return $null
}
function Test-TsOn([string]$v)  { return [bool]($v -match '^(?i)(Yes|예|On|True|1)$') }
function Test-TsOff([string]$v) { return [bool](($v -match '^(?i)(No|아니요|Off|False|0)$') -or ($v -match '없음')) }

# 보안 부팅이 켜져 있으면 Windows 는 testsigning 값을 무시한다 = 테스트 모드는 실제로 꺼져 있다.
# 옛 BIOS(UEFI 아님)에서는 이 명령이 오류를 내므로 "꺼짐" 으로 본다.
function Get-SecureBootOn {
    try { return [bool](Confirm-SecureBootUEFI -ErrorAction Stop) } catch { return $false }
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

# 테스트 서명을 끈다. 결과:
#   off         꺼졌다 (또는 이미 꺼져 있었다)
#   secureboot  bcdedit 은 거부했지만 보안 부팅이 켜져 있어 테스트 모드는 실제로 꺼져 있다
#   bitlocker   BitLocker 보호를 멈추지 못해 끄지 않았다 (끄면 다음 부팅에 복구 키를 묻는다)
#   failed      끄지 못했다
function Invoke-TestSigningOff {
    $before = Read-TestSigningState
    if ($null -ne $before -and (Test-TsOff $before)) {
        Write-Host ("    testsigning = {0}  (이미 꺼져 있음)" -f $before) -ForegroundColor Green
        return 'off'
    }
    $secure = Get-SecureBootOn
    Write-Host ("    보안 부팅    {0}" -f $(if ($secure) { "켜짐" } else { "꺼짐 또는 확인 못 함" }))

    # 보안 부팅이 켜져 있으면 testsigning 값은 적용되지 않고 bcdedit 도 바꾸지 못하게 막는다.
    # 부팅 설정을 바꾸지 않으므로 BitLocker 도 멈추지 않는다.
    if ($secure) { return 'secureboot' }

    # 이미 꺼져 있으면 부팅 설정이 바뀌지 않으므로 BitLocker 를 건드릴 이유가 없다.
    if (Test-TsOn $before) {
        $bl = Suspend-BitLockerOnce
        switch ($bl) {
            'suspended' { Write-Host "    BitLocker  다음 재부팅 한 번만 보호를 멈췄습니다 (복구 키를 묻지 않게)" -ForegroundColor Green }
            'unknown'   { Write-Host "    BitLocker  상태를 확인하지 못했습니다. 쓰고 있다면 다음 부팅에 복구 키를 물을 수 있습니다" -ForegroundColor Yellow }
            'fail'      {
                Write-Host "    BitLocker  보호를 잠시 멈추지 못했습니다 - 테스트 서명은 건드리지 않습니다" -ForegroundColor Yellow
                # 보안 부팅이 켜져 있으면 테스트 모드는 어차피 적용되지 않는다.
                if ($secure) { return 'secureboot' }
                return 'bitlocker'
            }
            default     { }
        }
    }

    $o = (& bcdedit.exe /set "{current}" testsigning off 2>&1 | Out-String)
    $code = $LASTEXITCODE
    Write-Host ("    " + $o.Trim())

    # 읽어서 확인한다. 명령이 성공했다고 해도 값이 그대로면 실패다.
    $after = Read-TestSigningState
    if ($null -eq $after) {
        Write-Host "    testsigning 값을 읽지 못했습니다" -ForegroundColor Yellow
    } else {
        Write-Host ("    testsigning = {0}" -f $after)
    }
    if ($code -eq 0 -and ($null -eq $after -or (Test-TsOff $after))) { return 'off' }

    # 보안 부팅이 켜져 있으면 bcdedit 이 testsigning 을 못 바꾸게 막는다
    # ("protected by Secure Boot policy"). 하지만 그때는 Windows 가 이 값을 무시하므로
    # 테스트 모드는 실제로 꺼져 있다 - OFF 가 원하는 상태다.
    if ($secure) { return 'secureboot' }
    return 'failed'
}

# 드라이버는 내렸는데 테스트 모드는 켜진 채로 끝날 때. 성공(0)도 실패(1)도 아니라
# 앱이 따로 알려 줄 수 있게 따로 끝낸다: 3 = 끄지 못함, 4 = BitLocker 때문에 끄지 않음.
function Exit-TestModeLeftOn {
    param([Parameter(Mandatory=$true)][string]$Message, [int]$Code = 3)
    Write-Host ""
    Write-Host ("주의: " + $Message) -ForegroundColor Yellow
    Write-Host "TESTMODE_LEFT_ON=1"
    try { Stop-Transcript | Out-Null } catch { }
    if ($Quiet) { exit $Code }
    [void][System.Windows.Forms.MessageBox]::Show(
        ($Message + "`n`n기록: " + $Script:LogPath),
        "PalmRej 끄기 - 테스트 모드",
        [System.Windows.Forms.MessageBoxButtons]::OK,
        [System.Windows.Forms.MessageBoxIcon]::Warning)
    Hold
    exit $Code
}

# Invoke-TestSigningOff 의 결과로 스크립트를 끝낸다.
function Finish-TestSigning {
    param([string]$Result, [string]$BackupPath = "")
    switch ($Result) {
        'bitlocker' {
            Exit-TestModeLeftOn ("PalmRej 는 껐지만, BitLocker 보호를 잠시 멈추지 못해서 Windows 테스트 모드는 끄지 않았습니다.`n" +
                                 "이대로 끄면 다음 부팅에 복구 키를 묻기 때문입니다. 터치는 Wacom 드라이버로 계속 됩니다.`n" +
                                 "테스트 모드를 허용하지 않는 프로그램을 쓰려면 BitLocker 를 잠시 멈춘 뒤 'PalmRej 끄기'를 한 번 더 누르세요.") -Code 4
        }
        'failed' {
            Exit-TestModeLeftOn ("PalmRej 는 껐지만 Windows 테스트 모드를 끄지 못했습니다.`n" +
                                 "터치는 Wacom 드라이버로 계속 됩니다. 재부팅한 뒤 'PalmRej 끄기'를 한 번 더 눌러 보세요.")
        }
    }
    if ($Result -eq 'secureboot') {
        Write-Host "    보안 부팅이 켜져 있어 설정값은 바꾸지 못했지만, 테스트 모드는 실제로 꺼져 있습니다." -ForegroundColor Green
    }
    Write-Host ""
    Write-Host "============================================================"
    Write-Host " 완료. 재부팅해야 적용됩니다." -ForegroundColor Cyan
    Write-Host ""
    Write-Host " 다시 켜기:  관리 앱의 [PalmRej 켜기]" -ForegroundColor Cyan
    if ($BackupPath) { Write-Host " 백업 위치:  $BackupPath" -ForegroundColor Cyan }
    Write-Host "============================================================"
    Done
    exit 0
}

Write-Host "============================================================"
Write-Host " 드라이버 OFF - Palm 패키지 제거 + 테스트 서명 Off"
Write-Host "============================================================"

$identity  = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Fail "관리자 권한 PowerShell 에서 실행해야 합니다."
}
Write-Host "ADMIN=PASS"

# ------------------------------------------------------------------
Write-Host ""
Write-Host "[1/6] 지금 상태 조사"

$palmPackages = @()
$enumText = (& pnputil.exe /enum-drivers 2>&1 | Out-String)
if ([string]::IsNullOrWhiteSpace($enumText)) {
    Fail "pnputil 이 드라이버 목록을 내놓지 않았습니다. 진행하지 않습니다."
}
foreach ($block in ($enumText -split "`r?`n`r?`n")) {
    if ($block -notmatch '(?i)palm') { continue }
    $pub = ([regex]::Match($block,'(?im)^[^\r\n:]*:\s*(oem\d+\.inf)\s*$')).Groups[1].Value
    if (-not $pub) { $pub = ([regex]::Match($block,'(?i)(oem\d+\.inf)')).Groups[1].Value }
    $org = ([regex]::Match($block,'(?im)^[^\r\n:]*:\s*(\S*palm\S*\.inf)\s*$')).Groups[1].Value
    if (-not $org) { $org = ([regex]::Match($block,'(?i)(\S*palm\S*\.inf)')).Groups[1].Value }
    if ($pub) {
        $palmPackages += [pscustomobject]@{ Published = $pub; Original = $org }
        Write-Host ("    패키지  {0}  ({1})" -f $pub, $org)
    }
}
if ($palmPackages.Count -eq 0) {
    Write-Host "    Palm 패키지 없음 - 이미 드라이버 OFF 상태일 수 있습니다." -ForegroundColor Yellow
}

$regsBefore = @(Find-PalmRegistrations)
foreach ($h in $regsBefore) {
    Write-Host ("    등록[{0}]  {1} = {2}" -f $h.Form, $h.ValueName, (@($h.Data) -join ';'))
    Write-Host ("             at {0}" -f $h.Key)
}

$svcBefore = @()
foreach ($n in $PalmServices) {
    $k = "HKLM:\SYSTEM\CurrentControlSet\Services\$n"
    if (-not (Test-Path -LiteralPath $k)) { continue }
    $svcBefore += [pscustomobject]@{
        Name = $n
        Start = (Get-RegValueOrNull -KeyPath $k -ValueName "Start")
        ImagePath = (Get-RegValueOrNull -KeyPath $k -ValueName "ImagePath")
    }
    Write-Host ("    서비스  {0}  Start={1}" -f $n, (Get-RegValueOrNull -KeyPath $k -ValueName "Start"))
}

# 이미 드라이버 내리기면 여기서 끝낸다.
# 그냥 진행하면 비어 있는 백업 폴더가 하나 더 생기고, 되돌리기 스크립트가
# "가장 최근" 백업으로 그 빈 폴더를 집어서 멀쩡한 백업을 못 찾게 된다.
# 서비스 키는 OFF 뒤 재부팅 전까지 Start=4 로 남아 있다. 그것도 "이미 꺼짐" 이다 - 아니면
# 다시 누를 때마다 패키지 없는 빈 백업 폴더가 하나씩 생긴다.
if ($palmPackages.Count -eq 0 -and $regsBefore.Count -eq 0 -and @($svcBefore | Where-Object { $_.Start -ne 4 }).Count -eq 0) {
    Write-Host ""
    Write-Host "  Palm 패키지도 등록도 없고 서비스도 꺼져 있습니다 - PalmRej 는 이미 꺼져 있습니다." -ForegroundColor Cyan
    Write-Host "  기존 백업은 그대로 두고 드라이버는 건드리지 않습니다." -ForegroundColor Cyan
    Write-Host "ALREADY_DOWN=1"
    $tsNow = Read-TestSigningState
    if (Test-TsOn $tsNow) {
        # 반쯤 끝난 OFF 뒤다 (BitLocker 때문에 테스트 서명을 못 껐거나, 켜기가 테스트
        # 서명만 켜고 멈췄다). 백업 폴더는 새로 만들지 않고 테스트 서명만 끈다.
        Write-Host ""
        Write-Host "[6/6] 테스트 서명 Off  (드라이버는 이미 꺼져 있고 테스트 서명만 켜져 있음)"
        Finish-TestSigning -Result (Invoke-TestSigningOff) -BackupPath ""
    }
    Write-Host ""
    try { Stop-Transcript | Out-Null } catch { }
    Hold
    exit 0
}

# ------------------------------------------------------------------
Write-Host ""
Write-Host "[2/6] 패키지 백업"

$stamp  = Get-Date -Format "yyyyMMdd_HHmmss"
$backup = Join-Path $BackupRoot $stamp
New-Item -ItemType Directory -Force -Path $backup | Out-Null

$repo = "C:\Windows\System32\DriverStore\FileRepository"
$copied = @()
Get-ChildItem -LiteralPath $repo -Directory -ErrorAction SilentlyContinue |
  Where-Object { $_.Name -match '(?i)^palm' } |
  ForEach-Object {
      $dst = Join-Path $backup $_.Name
      Copy-Item -LiteralPath $_.FullName -Destination $dst -Recurse -Force
      $inf = Get-ChildItem -LiteralPath $dst -Filter *.inf | Select-Object -First 1
      if ($null -eq $inf) { Fail ("백업한 패키지에 INF 가 없습니다: " + $_.Name) }
      $copied += [pscustomobject]@{ Folder = $_.Name; Inf = $inf.Name }
      Write-Host ("    복사  {0}  ->  {1}" -f $_.Name, $inf.Name)
  }

if ($copied.Count -eq 0 -and $palmPackages.Count -gt 0) {
    Fail "드라이버 저장소에서 Palm 패키지를 복사하지 못했습니다. 복구할 수 없게 되므로 중단합니다."
}

# 지금 붙어 있는 .sys 파일도 같이 챙긴다 (ImagePath 가 가리키는 실제 빌드)
$sysDir = Join-Path $backup "drivers"
New-Item -ItemType Directory -Force -Path $sysDir | Out-Null
foreach ($s in $svcBefore) {
    if (-not $s.ImagePath) { continue }
    $f = $s.ImagePath -replace '^\\SystemRoot\\','C:\Windows\'
    $f = $f -replace '^\\\?\?\\',''
    if (Test-Path -LiteralPath $f) {
        Copy-Item -LiteralPath $f -Destination $sysDir -Force
        Write-Host ("    복사  {0}" -f (Split-Path $f -Leaf))
    }
}

$manifest = [ordered]@{
    CreatedAt      = (Get-Date).ToString("o")
    BackupFolder   = $backup
    Packages       = $palmPackages
    CopiedPackages = $copied
    Registrations  = $regsBefore
    Services       = $svcBefore
}
$manifestPath = Join-Path $backup "복구정보.json"
[System.IO.File]::WriteAllText($manifestPath,
    ($manifest | ConvertTo-Json -Depth 8),
    (New-Object System.Text.UTF8Encoding($true)))
Write-Host ("    복구정보  {0}" -f $manifestPath)
Write-Host "BACKUP=PASS"

# ------------------------------------------------------------------
Write-Host ""
Write-Host "[3/6] 패키지 제거"

# 백업해 둔 패키지만 지운다. 백업에 없는 것은 되돌릴 수 없으므로 건드리지 않는다.
$backedUp = @($copied | ForEach-Object { $_.Inf.ToLower() })
Write-Host ("    백업된 패키지: {0}" -f ($backedUp -join ', '))

foreach ($pkg in $palmPackages) {
    $orig = ""
    if ($pkg.Original) { $orig = ([string]$pkg.Original).ToLower() }

    if (-not $orig -or ($backedUp -notcontains $orig)) {
        Write-Host ("    건너뜀  {0}  (원본 '{1}' 이 백업에 없음)" -f $pkg.Published, $pkg.Original) -ForegroundColor Yellow
        continue
    }

    Write-Host ("    제거  {0}  ({1})" -f $pkg.Published, $pkg.Original)
    $o = (& pnputil.exe /delete-driver $pkg.Published /uninstall /force 2>&1 | Out-String)
    Write-Host ("        " + ($o.Trim() -replace "`r?`n","`n        "))
}

# ------------------------------------------------------------------
Write-Host ""
Write-Host "[4/6] 남은 등록 정리"

foreach ($h in @(Find-PalmRegistrations)) {
    $rp = "Registry::" + $h.Key
    if (-not (Test-Path -LiteralPath $rp)) { continue }

    if ($h.Form -eq "MULTISZ") {
        $cur = @(Get-RegValueOrNull -KeyPath $rp -ValueName $h.ValueName)
        $keep = @($cur | Where-Object { $_ -notmatch 'Palm' })
        if ($keep.Count -eq 0) {
            Remove-ItemProperty -LiteralPath $rp -Name $h.ValueName -ErrorAction SilentlyContinue
            Write-Host ("    삭제  {0} at {1}" -f $h.ValueName, $h.Key)
        } else {
            Set-ItemProperty -LiteralPath $rp -Name $h.ValueName -Value ([string[]]$keep) -Type MultiString
            Write-Host ("    정리  {0}={1}" -f $h.ValueName, ($keep -join ';'))
        }
    }
    else {
        Remove-ItemProperty -LiteralPath $rp -Name $h.ValueName -ErrorAction SilentlyContinue
        Write-Host ("    삭제  {0} at {1}" -f $h.ValueName, $h.Key)
    }
}

# 서비스는 여기서 '사용 안 함'(Start=4)으로 바꾸지 않는다. [5/6] 확인이 통과한 뒤에 바꾼다.
# 예전에는 여기서 먼저 바꿨다. 그런데 [5/6] 에서 패키지가 하나 남은 채 멈추면, 그 패키지는
# 장치에 붙어 있는데 서비스만 꺼져서 재부팅 뒤 그 장치가 시작하지 못했다 (터치가 죽는 모양).
# 그 상태에서 끄기를 한 번 더 누르면 '사용 안 함'이 새 백업에 적혀 켜기까지 번졌다.

# ------------------------------------------------------------------
Write-Host ""
Write-Host "[5/6] 확인"

$leftReg = @(Find-PalmRegistrations)
$leftPkg = @()
$enum2 = (& pnputil.exe /enum-drivers 2>&1 | Out-String)
foreach ($block in ($enum2 -split "`r?`n`r?`n")) {
    if ($block -match '(?i)palm') {
        $pub = ([regex]::Match($block,'(?i)(oem\d+\.inf)')).Groups[1].Value
        if ($pub) { $leftPkg += $pub }
    }
}

if ($leftReg.Count -gt 0 -or $leftPkg.Count -gt 0) {
    Write-Host "    아직 남아 있습니다:" -ForegroundColor Red
    foreach ($x in $leftReg) { Write-Host ("      등록 {0} {1} at {2}" -f $x.Form, $x.ValueName, $x.Key) -ForegroundColor Red }
    foreach ($x in $leftPkg) { Write-Host ("      패키지 {0}" -f $x) -ForegroundColor Red }
    Write-Host ""
    Write-Host "    지금 재부팅하면 터치가 멈출 수 있습니다." -ForegroundColor Yellow
    Write-Host "    재부팅하지 말고 'PalmRej 끄기'를 한 번 더 누르세요." -ForegroundColor Yellow
    Write-Host "    두 번째에도 여기서 멈추면, 재부팅하지 말고 이 기록을 보내 주세요." -ForegroundColor Yellow
    Write-Host "    (서비스 Start 값과 테스트 모드는 바꾸지 않았습니다. 그래도 지워진 패키지의 서비스는" -ForegroundColor DarkGray
    Write-Host "     재부팅 때 없어지므로, 남은 등록이 그 서비스를 찾다가 장치가 시작하지 못할 수 있습니다.)" -ForegroundColor DarkGray
    Write-Host "OFF_INCOMPLETE=1"
    $head = "끄기가 다 끝나지 않았습니다 (남은 등록 {0}개, 패키지 {1}개)." -f $leftReg.Count, $leftPkg.Count
    # 5 = 여기서 멈춤. 앱이 일반 실패(1)와 구분해 "재부팅하지 말고 한 번 더" 를 띄운다.
    Fail ($head + "`n" +
          "지금 재부팅하면 터치가 멈출 수 있습니다.`n" +
          "재부팅하지 말고 'PalmRej 끄기'를 한 번 더 누르세요.`n" +
          "두 번째에도 여기서 멈추면, 재부팅하지 말고 이 기록을 보내 주세요.") -Code 5
}
Write-Host "    남은 Palm 등록/패키지 없음"

# 확인이 통과했으니 이제 서비스를 '사용 안 함'으로 바꾼다.
foreach ($n in $PalmServices) {
    $k = "HKLM:\SYSTEM\CurrentControlSet\Services\$n"
    if (-not (Test-Path -LiteralPath $k)) { continue }
    try {
        Set-ItemProperty -LiteralPath $k -Name Start -Value 4
        Write-Host ("    서비스 정지  {0} (Start=4)" -f $n)
    } catch {
        Write-Host ("    서비스 정지 실패  {0} - {1}" -f $n, $_.Exception.Message) -ForegroundColor Yellow
    }
}

$leftSvc = @()
foreach ($n in $PalmServices) {
    $k = "HKLM:\SYSTEM\CurrentControlSet\Services\$n"
    if (-not (Test-Path -LiteralPath $k)) { continue }
    $st = Get-RegValueOrNull -KeyPath $k -ValueName "Start"
    if ($st -ne 4) { $leftSvc += ("$n Start=$st") }
}
# 패키지와 등록이 모두 없어진 뒤라 이 서비스를 부르는 장치가 없다. Start 가 3 으로 남아도
# 올라오지 않으므로 멈추지 않고 알리기만 한다. (여기서 멈추면 사용자가 끄기를 한 번 더 눌러
# 패키지 없는 빈 백업만 하나 더 생긴다.)
if ($leftSvc.Count -gt 0) {
    foreach ($x in $leftSvc) { Write-Host ("    서비스 '사용 안 함' 못 바꿈  {0} - 부르는 장치가 없어 올라오지 않습니다" -f $x) -ForegroundColor Yellow }
}
Write-Host "VERIFY=PASS"

# ------------------------------------------------------------------
Write-Host ""
Write-Host "[6/6] 테스트 서명 Off"

# 끝까지 확인한다: 명령이 성공했다고 해도 값이 그대로면 실패다. 예전에는 실패해도
# "완료" 로 끝나서, 테스트 모드가 켜진 채인 것을 아무도 몰랐다.
Finish-TestSigning -Result (Invoke-TestSigningOff) -BackupPath $backup