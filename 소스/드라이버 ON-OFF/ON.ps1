#requires -Version 5.1
<#
드라이버 ON - Palm 드라이버를 되살리고 테스트 서명을 켭니다.

이 스크립트는 "안전한 방향"이 아닙니다. 우리 드라이버는 테스트 서명이라
테스트 서명이 꺼진 상태로 장치에 붙으면 터치가 죽습니다.
그래서 테스트 서명을 못 켜면 아무것도 설치하지 않고 멈춥니다.
아무것도 안 한 상태(= 드라이버 OFF)가 안전한 상태입니다.

두 번 실행해야 할 수 있습니다:
  1회차  테스트 서명 On -> 설치 시도 -> 거부되면 안내하고 멈춤
  재부팅
  2회차  설치 성공
#>

[CmdletBinding()]
param(
    [string]$BackupFolder = "",
    [string]$BackupRoot   = "",
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
        $t, "드라이버 ON",
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
    param([Parameter(Mandatory=$true)][string]$Message)
    Write-Host ""
    Write-Host "중단: $Message" -ForegroundColor Red
    try { Stop-Transcript | Out-Null } catch { }
    # A512: the app raises the dialog instead.
    if ($Quiet) { exit 1 }
    [void][System.Windows.Forms.MessageBox]::Show(
        ("실패했습니다." + "`n`n" + $Message + "`n`n" +
         "이 창은 닫지 마세요. 위에 나온 내용이 원인입니다." + "`n" +
         "기록: " + $Script:LogPath),
        "드라이버 ON - 실패",
        [System.Windows.Forms.MessageBoxButtons]::OK,
        [System.Windows.Forms.MessageBoxIcon]::Error)
    Hold
    exit 1
}
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
    foreach ($root in $ScanRoots) {
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
function Read-TestSigning {
    foreach ($cmd in @(@("/enum","{current}"), @("/enum","ACTIVE"), @())) {
        $t = (& bcdedit.exe @cmd 2>&1 | Out-String)
        if ($t -match '(?im)^\s*testsigning\s+(\S+)') { return $Matches[1] }
    }
    return $null
}

# 패키지 폴더 이름(palmrejfilter.inf_amd64_...)에서 앞부분(palmrejfilter)만.
function Get-PkgBase([string]$Folder) { return (($Folder -split '\.inf_')[0]).ToLowerInvariant() }

# 백업에 든 패키지가 온전한 한 벌인지. 모자라면 이유 문장, 온전하면 $null.
# Cintiq Pro 24 판은 탭·필터·파이프라인 셋, 다른 기종 판은 필터 하나다.
# 끄기가 중간에 멈춘 뒤 한 번 더 누르면 남은 패키지 하나만 든 '반쪽 백업'이 생길 수 있다.
function Get-BackupGap {
    param([object[]]$Pkgs, [object[]]$Svcs = @(), [object[]]$Regs = @())
    $names = @($Pkgs | ForEach-Object { Get-PkgBase ([string]$_.Folder) })
    $hasF = $names -contains 'palmrejfilter'
    $hasT = $names -contains 'palmrawusbtap'
    $hasP = $names -contains 'palmrejpipeline'
    if (-not $hasF) { return "필터 패키지가 없는 반쪽 백업" }
    if ($hasT -ne $hasP) { return "탭·파이프라인 중 하나만 있는 반쪽 백업" }
    # 필터 하나만 든 백업은 다른 기종 판일 수도, Cintiq 판에서 필터만 남았던 반쪽일 수도 있다.
    # 같은 백업의 서비스·등록 기록에 탭이나 파이프라인이 나오면 반쪽이다.
    if (-not $hasT) {
        $seen = @(@($Svcs | ForEach-Object { [string]$_.Name }) +
                  @($Regs | ForEach-Object { [string]$_.ValueName; @($_.Data) | ForEach-Object { [string]$_ } }))
        if ($seen -contains 'PalmRawUsbTap' -or $seen -contains 'PalmRejPipeline') {
            return "탭·파이프라인 기록은 있는데 패키지는 필터 하나뿐인 반쪽 백업"
        }
    }
    return $null
}

# 백업에 적힌 서비스 시작 값을 되살릴 값으로. 4(사용 안 함)는 3(필요할 때 시작)으로 쓴다.
# 4 는 끄기가 중간에 멈춘 흔적인데, 그대로 써 넣으면 재부팅 뒤 장치가 그 드라이버를 못 올려
# 터치가 멈춘다. 세 드라이버 모두 원래 3 이다. 값이 없으면 $null.
function Get-RestoreStart {
    param($Start)
    if ($null -eq $Start) { return $null }
    $v = [int]$Start
    if ($v -eq 4) { return 3 }
    return $v
}

# 드라이버 저장소에 들어 있는 Palm 패키지의 원래 INF 이름들 (소문자).
function Get-StoreInfNames {
    $names = @()
    $t = (& pnputil.exe /enum-drivers 2>&1 | Out-String)
    foreach ($block in ($t -split "`r?`n`r?`n")) {
        $mm = [regex]::Match($block, '(?i)(palmrejfilter|palmrejpipeline|palmrawusbtap)\.inf')
        if ($mm.Success) { $names += $mm.Value.ToLowerInvariant() }
    }
    return $names
}

Write-Host "============================================================"
Write-Host " 드라이버 ON - Palm 드라이버 복구 + 테스트 서명 On"
Write-Host "============================================================"

$identity  = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Fail "관리자 권한 PowerShell 에서 실행해야 합니다."
}
Write-Host "ADMIN=PASS"

# 드라이버 OFF 를 누른 뒤 재부팅 전이면 Windows 가 우리 서비스를 "재부팅 때 삭제" 로
# 표시해 둔 상태다 (DeleteFlag=1). 이 위에 다시 설치하면 서비스 등록이 거부되어 설치가
# 반쯤 깨지고, 재부팅 뒤 터치가 죽을 수 있다. 재부팅이 먼저다.
$pendingDelete = @()
foreach ($svcName in $PalmServices) {
    $sp = Get-ItemProperty -LiteralPath "HKLM:\SYSTEM\CurrentControlSet\Services\$svcName" -ErrorAction SilentlyContinue
    if ($sp -and $sp.PSObject.Properties['DeleteFlag'] -and $sp.DeleteFlag -ne 0) { $pendingDelete += $svcName }
}
if ($pendingDelete.Count -gt 0) {
    Write-Host ("    OFF 후 재부팅 전: " + ($pendingDelete -join ", ") + " 삭제 예정") -ForegroundColor Yellow
    # 끄기가 [5/6] 확인에서 멈췄으면 (1.0.0 OFF 코드 5, 0.1.27 이하 OFF 코드 1) 서비스에 삭제 표시가
    # 있는데 Palm 등록(두 형태)이나 패키지가 남아 있다. 끝까지 간 끄기는 둘 다 없어야 [5/6] 을 지난다.
    # 관리 앱(Program.cs Judge 의 OffIncomplete)과 같은 규칙이다. Start 는 보지 않는다 - 0.1.27 의
    # OFF 는 [5/6] 전에 Start=4 를 쓰고, 1.0.0 은 [5/6] 뒤에 쓰는데 그 쓰기만 실패할 수도 있다.
    # 멈춘 끄기 뒤에 재부팅하면 남은 등록이 없는 서비스를 찾다가 터치가 멈출 수 있으므로, 그때는
    # "재부팅한 다음" 이 아니라 끄기를 한 번 더 하라고 한다.
    $leftRegs = @(Find-PalmRegistrations)
    $leftPkgs = @(Get-StoreInfNames)
    Write-Host ("    남은 Palm 등록 {0}개, 저장소의 Palm 패키지 {1}개" -f $leftRegs.Count, $leftPkgs.Count) -ForegroundColor Yellow
    if ($leftRegs.Count -gt 0 -or $leftPkgs.Count -gt 0) {
        foreach ($x in $leftRegs) { Write-Host ("      등록 {0} {1} at {2}" -f $x.Form, $x.ValueName, $x.Key) -ForegroundColor Yellow }
        foreach ($x in $leftPkgs) { Write-Host ("      패키지 {0}" -f $x) -ForegroundColor Yellow }
        Write-Host "OFF_INCOMPLETE=1"
        Fail ("'PalmRej 끄기'가 다 끝나지 않은 채입니다. 지금 재부팅하면 터치가 멈출 수 있습니다.`n" +
              "재부팅하지 말고 'PalmRej 끄기'를 한 번 더 누르세요. 아무것도 바꾸지 않았습니다.")
    }
    Fail "드라이버 OFF 를 한 뒤 아직 재부팅하지 않았습니다. 재부팅한 다음 드라이버 ON 을 누르세요. 아무것도 바꾸지 않았습니다."
}

# 보안 부팅이 켜져 있으면 Windows 는 테스트 모드를 무시한다. 그 상태로 테스트 서명
# 드라이버를 올리면 재부팅 뒤 장치가 코드 52 로 멈춰 터치가 죽는다. 예전에는 bcdedit 이
# 거부해 주기를 기대했지만, testsigning 값이 이미 Yes 로 남아 있으면(보안 부팅을 켜기 전에
# 켜 둔 경우) 거부할 것이 없어서 그대로 설치까지 갔다. 그래서 무엇이든 바꾸기 전에 묻는다.
# 옛 BIOS(UEFI 아님)에서는 이 명령이 오류를 내는데, 그때는 보안 부팅 자체가 없다.
$secureBoot = $null
try { $secureBoot = Confirm-SecureBootUEFI -ErrorAction Stop } catch { $secureBoot = $null }
Write-Host ("    보안 부팅  {0}" -f $(if ($secureBoot -eq $true) { "켜짐" } elseif ($secureBoot -eq $false) { "꺼짐" } else { "확인 못 함 (옛 BIOS 등)" }))
if ($secureBoot -eq $true) {
    Fail ("보안 부팅(Secure Boot)이 켜져 있어서 Windows 테스트 모드를 쓸 수 없습니다.`n" +
          "PalmRej 드라이버는 테스트 서명이라 테스트 모드가 꼭 필요합니다.`n`n" +
          "BIOS(UEFI) 설정에서 보안 부팅을 끄고 다시 'PalmRej 켜기'를 누르세요.`n" +
          "아무것도 바꾸지 않았습니다.")
}

# ------------------------------------------------------------------
Write-Host ""
Write-Host "[1/6] 백업 찾기"

if ([string]::IsNullOrWhiteSpace($BackupFolder)) {
    if (-not (Test-Path -LiteralPath $BackupRoot)) { Fail "백업 폴더가 없습니다: $BackupRoot" }
    $cands = @(Get-ChildItem -LiteralPath $BackupRoot -Directory -ErrorAction SilentlyContinue |
               Sort-Object Name -Descending)
    if ($cands.Count -eq 0) { Fail "백업이 하나도 없습니다: $BackupRoot" }

    # 되살릴 패키지가 실제로 들어 있는 가장 최근 백업을 고른다.
    # 이미 드라이버 내리기일 때 켜기 스크립트를 또 돌리면 빈 백업이 생길 수 있고,
    # 그냥 "가장 최근" 을 집으면 멀쩡한 백업을 두고 빈 것을 집게 된다.
    $picked = $null
    foreach ($c in $cands) {
        $mf = Join-Path $c.FullName "복구정보.json"
        if (-not (Test-Path -LiteralPath $mf)) {
            Write-Host ("    건너뜀  {0}  (복구정보.json 없음)" -f $c.Name) -ForegroundColor DarkGray
            continue
        }
        try { $m = Get-Content -LiteralPath $mf -Raw -Encoding UTF8 | ConvertFrom-Json }
        catch { Write-Host ("    건너뜀  {0}  (읽을 수 없음)" -f $c.Name) -ForegroundColor DarkGray; continue }
        if (@($m.CopiedPackages).Count -eq 0) {
            Write-Host ("    건너뜀  {0}  (패키지 0개)" -f $c.Name) -ForegroundColor DarkGray
            continue
        }
        # 반쪽 백업은 고르지 않는다. 필터 없이 파이프라인만 되살리면 재부팅 뒤 터치가 멈춘다.
        $gap = Get-BackupGap @($m.CopiedPackages) @($m.Services) @($m.Registrations)
        if ($gap) {
            Write-Host ("    건너뜀  {0}  ({1})" -f $c.Name, $gap) -ForegroundColor DarkGray
            continue
        }
        $picked = $c.FullName
        break
    }
    if ($null -eq $picked) {
        Write-Host "    쓸 수 있는 백업이 없습니다. 찾은 폴더:" -ForegroundColor Red
        foreach ($c in $cands) { Write-Host ("      {0}" -f $c.FullName) -ForegroundColor Red }
        Fail ("되살릴 수 있는 온전한 백업을 찾지 못했습니다. 아무것도 바꾸지 않았습니다.`n" +
              "받은 zip 을 푼 폴더의 'PalmRej 설치.exe' 로 다시 설치하세요.")
    }
    $BackupFolder = $picked
}
if (-not (Test-Path -LiteralPath $BackupFolder)) { Fail "백업 폴더가 없습니다: $BackupFolder" }

$manifestPath = Join-Path $BackupFolder "복구정보.json"
if (-not (Test-Path -LiteralPath $manifestPath)) { Fail "복구정보.json 이 없습니다: $manifestPath" }
$manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json

$wantPkgs = @($manifest.CopiedPackages)
$wantRegs = @($manifest.Registrations)
$wantSvcs = @($manifest.Services)

Write-Host ("    폴더      {0}" -f $BackupFolder)
Write-Host ("    만든 시각  {0}" -f $manifest.CreatedAt)
Write-Host ("    패키지 {0}개  등록 {1}개  서비스 {2}개" -f $wantPkgs.Count, $wantRegs.Count, $wantSvcs.Count)
if ($wantPkgs.Count -eq 0) { Fail "백업에 되살릴 패키지가 없습니다." }
# -BackupFolder 로 직접 고른 백업은 그대로 쓰되 알려 준다. 필터가 안 들어가면 [3/6] 이 멈춘다.
$gapNow = Get-BackupGap $wantPkgs $wantSvcs $wantRegs
if ($gapNow) { Write-Host ("    주의      {0}입니다" -f $gapNow) -ForegroundColor Yellow }
foreach ($s in $wantSvcs) {
    if ($null -ne $s.Start -and [int]$s.Start -eq 4) {
        Write-Host ("    주의      {0} 이 '사용 안 함'(Start=4)으로 적혀 있어 3 으로 되살립니다" -f $s.Name) -ForegroundColor Yellow
    }
}

# ------------------------------------------------------------------
Write-Host ""
Write-Host "[2/6] 테스트 서명 On  (여기서 실패하면 아무것도 설치하지 않습니다)"

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

# 이미 켜져 있으면 부팅 설정이 바뀌지 않으므로 BitLocker 를 건드릴 이유가 없다.
$tsBefore = Read-TestSigning
if (-not ($tsBefore -match '^(?i)(Yes|예|On|True|1)$')) {
    $bl = Suspend-BitLockerOnce
    switch ($bl) {
        'suspended' { Write-Host "    BitLocker  다음 재부팅 한 번만 보호를 멈췄습니다 (복구 키를 묻지 않게)" -ForegroundColor Green }
        'unknown'   { Write-Host "    BitLocker  상태를 확인하지 못했습니다. 쓰고 있다면 다음 부팅에 복구 키를 물을 수 있습니다" -ForegroundColor Yellow }
        'fail'      {
            Write-Host "    BitLocker  보호를 잠시 멈추지 못했습니다" -ForegroundColor Red
            Fail ("BitLocker 가 켜져 있는데 보호를 잠시 멈추지 못했습니다. 이대로 테스트 모드를 켜면 다음 부팅에 복구 키를 묻습니다.`n" +
                  "아무것도 바꾸지 않았습니다.")
        }
        default     { }
    }
}

$o = (& bcdedit.exe /set "{current}" testsigning on 2>&1 | Out-String)
$setCode = $LASTEXITCODE
Write-Host ("    " + $o.Trim())

if ($setCode -ne 0) {
    Write-Host ""
    Write-Host "    테스트 서명을 켜지 못했습니다." -ForegroundColor Red
    Write-Host "    보안 부팅(Secure Boot)이 켜져 있으면 BIOS 에서 꺼야 합니다." -ForegroundColor Yellow
    Write-Host "    BitLocker 가 켜져 있어도 막힐 수 있습니다." -ForegroundColor Yellow
    Write-Host ""
    Write-Host "    드라이버는 하나도 설치하지 않았습니다." -ForegroundColor Cyan
    Write-Host "    지금 상태(드라이버 OFF)가 안전한 상태이고, 터치는 그대로 됩니다." -ForegroundColor Cyan
    Fail "테스트 서명이 꺼진 채로 테스트 서명 드라이버를 붙이면 터치가 죽습니다."
}

$state = Read-TestSigning
if ($null -eq $state) {
    Write-Host "    testsigning 값을 읽지 못했습니다. 위 설정 결과만 믿고 진행합니다." -ForegroundColor Yellow
}
elseif ($state -match '^(?i)(Yes|예|On|True|1)$') {
    Write-Host ("    testsigning = {0}" -f $state) -ForegroundColor Green
}
else {
    Write-Host ("    testsigning = {0}   <- 켜지지 않았습니다" -f $state) -ForegroundColor Red
    Write-Host "    드라이버는 하나도 설치하지 않았습니다." -ForegroundColor Cyan
    Fail "테스트 서명이 켜지지 않았습니다."
}

# ------------------------------------------------------------------
# A513: 패키지에 쓰던 빌드를 넣고 다시 서명한다.
#
# 왜 필요한가. 패키지 안에는 처음 만들 때의 .sys 가 들어 있고, 그 뒤로 쌓은
# 빌드는 drivers 폴더에 따로 복사해서 ImagePath 만 바꿔 쓰는 구조다. 복구하면
# 패키지가 되살아나고, 재부팅하면서 Windows 가 INF 를 다시 처리하며 ImagePath
# 를 패키지 안쪽으로 되돌린다. 그래서 재부팅 후에 설치 스크립트를 또 돌리고
# 재부팅을 한 번 더 해야 했다.
#
# ImagePath 를 두고 다투는 대신, 양쪽이 같은 파일을 가리키게 만든다. 패키지
# 안의 .sys 를 쓰던 빌드로 바꾸고 카탈로그를 다시 만들어 서명하면, Windows 가
# ImagePath 를 어디로 돌려놓든 올라오는 바이너리는 같다.
#
# 실패하면 원래 파일로 되돌리고 예전처럼 진행한다. 좋아지면 좋고, 안 되면
# 지금까지와 똑같다 - 이 단계 때문에 나빠지는 경우는 없어야 한다.
# ------------------------------------------------------------------
Write-Host ""
Write-Host "[2.5/6] 패키지에 쓰던 빌드 넣기"

function Find-KitTool {
    param([string]$Name)
    $roots = @("${env:ProgramFiles(x86)}\Windows Kits\10\bin", "${env:ProgramFiles}\Windows Kits\10\bin")
    foreach ($r in $roots) {
        if (-not (Test-Path -LiteralPath $r)) { continue }
        $hit = Get-ChildItem -LiteralPath $r -Recurse -Filter $Name -ErrorAction SilentlyContinue |
               Sort-Object FullName -Descending | Select-Object -First 1
        if ($hit) { return $hit.FullName }
    }
    return $null
}

$inf2cat  = Find-KitTool "Inf2Cat.exe"
$signtool = Find-KitTool "signtool.exe"
# 공개판 1.0.0 부터 패키지는 CN=PalmRej Test Signing 으로 서명한다. 1.0.1 부터는 그 인증서(지문 고정)만 쓴다.
# 옛 개발용 인증서는 이름에 Windows 사용자 이름이 들어 있어서 더는 쓰지 않는다.
$signCert = Get-ChildItem Cert:\CurrentUser\My, Cert:\LocalMachine\My -ErrorAction SilentlyContinue |
            Where-Object { $_.Thumbprint -eq "02917AB8321CF79752BA799177759B81BA009C01" -and $_.HasPrivateKey -and $_.NotAfter -gt (Get-Date) } |
            Select-Object -First 1

if ((-not $inf2cat) -or (-not $signtool) -or (-not $signCert)) {
    Write-Host "    도구나 인증서를 찾지 못했습니다. 이 단계는 건너뜁니다." -ForegroundColor Yellow
    Write-Host "    (예전처럼 재부팅 후 설치 스크립트를 한 번 더 돌리시면 됩니다.)" -ForegroundColor DarkGray
}
else {
    foreach ($svc in $wantSvcs) {
        # drivers 폴더를 가리키는 서비스만 해당한다. 패키지 안쪽을 가리키는
        # 서비스(파이프라인)는 이미 패키지의 파일을 쓰므로 바꿀 것이 없다.
        if ($svc.ImagePath -notmatch '(?i)\\System32\\drivers\\') { continue }

        $sysName = Split-Path $svc.ImagePath -Leaf
        $srcSys  = Join-Path (Join-Path $BackupFolder "drivers") $sysName
        if (-not (Test-Path -LiteralPath $srcSys)) {
            Write-Host ("    건너뜀  {0} - 백업에 {1} 이 없습니다" -f $svc.Name, $sysName) -ForegroundColor Yellow
            continue
        }

        $pkgEntry = $wantPkgs | Where-Object { $_.Folder -like ($svc.Name.ToLower() + ".inf_*") } | Select-Object -First 1
        if ($null -eq $pkgEntry) {
            Write-Host ("    건너뜀  {0} - 짝이 되는 패키지를 못 찾았습니다" -f $svc.Name) -ForegroundColor Yellow
            continue
        }

        $pkgDir = Join-Path $BackupFolder $pkgEntry.Folder
        $pkgSys = Get-ChildItem -LiteralPath $pkgDir -Filter *.sys -ErrorAction SilentlyContinue | Select-Object -First 1
        $pkgCat = Get-ChildItem -LiteralPath $pkgDir -Filter *.cat -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($null -eq $pkgSys -or $null -eq $pkgCat) {
            Write-Host ("    건너뜀  {0} - 패키지에 .sys 나 .cat 이 없습니다" -f $svc.Name) -ForegroundColor Yellow
            continue
        }

        if ((Get-FileHash -LiteralPath $srcSys).Hash -eq (Get-FileHash -LiteralPath $pkgSys.FullName).Hash) {
            Write-Host ("    이미 같음  {0}" -f $svc.Name) -ForegroundColor DarkGray
            continue
        }

        # 되돌릴 수 있게 원본을 옆에 둔다.
        $bakSys = $pkgSys.FullName + ".원본"
        $bakCat = $pkgCat.FullName + ".원본"
        $ok = $false
        try {
            if (-not (Test-Path -LiteralPath $bakSys)) { Copy-Item -LiteralPath $pkgSys.FullName -Destination $bakSys -Force }
            if (-not (Test-Path -LiteralPath $bakCat)) { Copy-Item -LiteralPath $pkgCat.FullName -Destination $bakCat -Force }

            Copy-Item -LiteralPath $srcSys -Destination $pkgSys.FullName -Force

            $o = (& $inf2cat /driver:"$pkgDir" /os:10_X64 2>&1 | Out-String)
            if ($LASTEXITCODE -ne 0) { throw ("Inf2Cat 실패: " + $o.Trim()) }

            $o = (& $signtool sign /fd SHA256 /sha1 $signCert.Thumbprint $pkgCat.FullName 2>&1 | Out-String)
            if ($LASTEXITCODE -ne 0) { throw ("서명 실패: " + $o.Trim()) }

            if ((Get-AuthenticodeSignature -LiteralPath $pkgCat.FullName).Status -ne "Valid") {
                throw "서명이 유효하지 않습니다"
            }
            $ok = $true
        }
        catch {
            Write-Host ("    실패  {0} - {1}" -f $svc.Name, $_.Exception.Message) -ForegroundColor Yellow
            try {
                if (Test-Path -LiteralPath $bakSys) { Copy-Item -LiteralPath $bakSys -Destination $pkgSys.FullName -Force }
                if (Test-Path -LiteralPath $bakCat) { Copy-Item -LiteralPath $bakCat -Destination $pkgCat.FullName -Force }
                Write-Host "        원래 패키지로 되돌렸습니다. 예전 방식대로 진행합니다." -ForegroundColor DarkGray
            } catch {
                Write-Host "        되돌리기도 실패했습니다. 이 패키지는 설치하지 않습니다." -ForegroundColor Red
            }
        }

        if ($ok) {
            Write-Host ("    넣음  {0}  ->  {1}" -f $sysName, $pkgEntry.Folder) -ForegroundColor Green
        }
    }
}

# ------------------------------------------------------------------
Write-Host ""
Write-Host "[3/6] 패키지 재설치"

# 지금 부팅에서 테스트 모드가 실제로 켜져 있는가 (위에서 켠 것은 재부팅해야 적용된다).
# 꺼진 부팅에서는 아래 pnputil /install 이 드라이버를 지금 바로 장치에 붙이고, Windows 가
# 서명 때문에 거부해서(코드 52) 재부팅 전까지 터치 USB 장치 전체와 펜의 와콤 전용 창구
# (COL02)가 멈춘다. 2026-09-21 08:32 에 실제로 그랬고, 재부팅하면 풀린다. 끄기 뒤에는 늘
# 이 경우다. 미리 알리고, 끝에서 바로 재부팅하라고 한다.
# (bcdedit 이 아니라 이번 부팅의 실제 설정을 읽는다.)
$bootTestSigning = [bool]([string](Get-RegValueOrNull -KeyPath 'HKLM:\SYSTEM\CurrentControlSet\Control' -ValueName 'SystemStartOptions') -match 'TESTSIGNING')
if (-not $bootTestSigning) {
    Write-Host "    지금 부팅은 테스트 모드가 꺼진 상태입니다." -ForegroundColor Yellow
    Write-Host "    여기서부터 재부팅 전까지 터치와 펜 일부가 멈춥니다. 끝나면 바로 재부팅하세요." -ForegroundColor Yellow
    Write-Host "PAUSED_UNTIL_REBOOT=1"
}

# 순서: 탭 -> 필터 -> 파이프라인. 파이프라인 패키지는 주 터치 창구(COL02)에 필터와 파이프라인
# 두 이름을 함께 적는데 (palmrejpipeline.inf 45줄), 필터 서비스는 필터 패키지가 만든다. 필터가
# 안 들어갔는데 파이프라인을 넣으면 COL02 가 없는 서비스를 찾다가 재부팅 뒤 터치가 시작하지
# 못한다 (재부팅을 더 해도 안 풀린다). 그래서 필터가 안 들어가면 파이프라인은 넣지 않고 멈춘다.
function Get-PkgRank([string]$Folder) {
    switch (Get-PkgBase $Folder) { 'palmrawusbtap' { return 0 } 'palmrejfilter' { return 1 } 'palmrejpipeline' { return 2 } }
    return 3
}
$ordered = @($wantPkgs | Sort-Object { Get-PkgRank ([string]$_.Folder) })

$installed = 0
$okBases   = @()    # 들어간 패키지 (palmrejfilter 처럼)
$failed    = @()    # 안 들어간 패키지
$filterStop = $false
foreach ($pkg in $ordered) {
    $base = Get-PkgBase ([string]$pkg.Folder)
    if ($base -eq 'palmrejpipeline' -and ($okBases -notcontains 'palmrejfilter')) {
        Write-Host ("    넣지 않음  {0}  (필터가 들어가지 않아서. 넣으면 재부팅 뒤 터치가 멈춥니다)" -f $pkg.Inf) -ForegroundColor Yellow
        $failed += $base
        $filterStop = $true
        continue
    }
    $inf = Join-Path (Join-Path $BackupFolder $pkg.Folder) $pkg.Inf
    if (-not (Test-Path -LiteralPath $inf)) {
        Write-Host ("    없음  {0}" -f $inf) -ForegroundColor Yellow
        $failed += $base
        continue
    }
    Write-Host ("    설치  {0}" -f $pkg.Inf)
    $o = (& pnputil.exe /add-driver $inf /install 2>&1 | Out-String)
    $code = $LASTEXITCODE
    Write-Host ("        " + ($o.Trim() -replace "`r?`n","`n        "))
    Write-Host ("        (pnputil 종료 코드 {0})" -f $code) -ForegroundColor DarkGray
    # 0 = 됨, 3010 = 됨 (재부팅하면 적용). 다른 값이어도 저장소에 들어가 있으면 된 것으로 본다
    # (같은 패키지가 이미 있으면 바꿀 장치가 없다는 값이 올 수 있다).
    $ok = ($code -eq 0 -or $code -eq 3010)
    if (-not $ok -and (@(Get-StoreInfNames) -contains ([string]$pkg.Inf).ToLowerInvariant())) {
        Write-Host "        종료 코드는 다르지만 저장소에 들어가 있어서 된 것으로 봅니다" -ForegroundColor DarkGray
        $ok = $true
    }
    if ($ok) { $installed++; $okBases += $base }
    else     { $failed += $base; Write-Host ("        설치 안 됨  {0}" -f $pkg.Inf) -ForegroundColor Yellow }
}

if ($installed -eq 0) {
    Write-Host ""
    Write-Host "  패키지를 하나도 설치하지 못했습니다." -ForegroundColor Yellow
    Write-Host "  테스트 서명은 재부팅해야 실제로 적용됩니다. 방금 켰으니 아직 적용 전입니다." -ForegroundColor Yellow
    Write-Host ""
    Write-Host "  => 지금 재부팅하고, 이 스크립트를 한 번 더 실행하세요." -ForegroundColor Cyan
    Hold
    exit 2
}
if ($filterStop -or ($okBases -notcontains 'palmrejfilter')) {
    # 필터가 안 들어갔다. 파이프라인은 넣지 않았고, 서비스·등록도 되돌리지 않고 여기서 멈춘다.
    # 들어간 것(탭)은 자기 서비스를 스스로 만들므로, 재부팅 뒤에도 터치를 막지 않는다.
    Write-Host ""
    Write-Host "  필터 패키지가 들어가지 않아서 여기서 멈춥니다." -ForegroundColor Yellow
    Write-Host "  파이프라인은 넣지 않았고 필터 등록도 되돌리지 않았습니다 (넣으면 재부팅 뒤 터치가 멈춥니다)." -ForegroundColor Yellow
    Write-Host "  테스트 모드는 켜 두었습니다." -ForegroundColor Yellow
    Write-Host ""
    Write-Host "  => 재부팅한 다음 'PalmRej 켜기'를 한 번 더 누르세요." -ForegroundColor Cyan
    Write-Host "PARTIAL_STOP=filter"
    try { Stop-Transcript | Out-Null } catch { }
    Hold
    exit 2
}
if ($failed.Count -gt 0) {
    Write-Host ("    일부만 설치됨 ({0}/{1}), 안 들어간 것: {2}. 끝에서 다시 알려 드립니다." -f `
        $installed, $wantPkgs.Count, (($failed | Select-Object -Unique) -join ", ")) -ForegroundColor Yellow
}

Write-Host "    장치 다시 훑기"
$o = (& pnputil.exe /scan-devices 2>&1 | Out-String)
Write-Host ("        " + ($o.Trim() -replace "`r?`n","`n        "))

# ------------------------------------------------------------------
Write-Host ""
Write-Host "[4/6] 쓰던 빌드 되돌리기"

$sysDir = Join-Path $BackupFolder "drivers"
foreach ($s in $wantSvcs) {
    $key = "HKLM:\SYSTEM\CurrentControlSet\Services\$($s.Name)"
    # 패키지가 안 들어간 드라이버의 서비스는 만들지도 고치지도 않는다.
    if ($failed -contains ([string]$s.Name).ToLowerInvariant()) {
        Write-Host ("    건너뜀  {0} - 패키지가 들어가지 않았습니다" -f $s.Name) -ForegroundColor Yellow
        continue
    }
    if (-not (Test-Path -LiteralPath $key)) {
        # ------------------------------------------------------------------
        # A514: 서비스 키를 미리 만든다.
        #
        # PalmRejPipeline 은 여기서 늘 키가 없다. Windows 가 만드는데, 재부팅
        # 하면서 장치 설치를 마무리할 때 만들기 때문이다. 그런데 터치 장치
        # COL02 는 UpperFilters 에 PalmRejPipeline 을 달고 시작하므로, 첫 부팅
        # 에서는 "아직 없는 필터"를 찾다가 오류 19 로 시작에 실패한다. 그 부팅
        # 중에 키가 생기고 두 번째 부팅부터 정상이 된다 - 그래서 재부팅이 두
        # 번 필요했다.
        #
        # 값은 우리가 지어내지 않는다. 내리기 전에 살아 있던 키에서 그대로
        # 옮긴다. ImagePath 만은 예전 저장소 폴더 이름이 달라질 수 있으므로,
        # 방금 설치된 폴더를 찾아 다시 만든다. 못 찾으면 만들지 않는다 -
        # 없는 파일을 가리키는 키를 남기느니 예전처럼 재부팅 두 번이 낫다.
        # ------------------------------------------------------------------
        $made = $false
        if ($s.ImagePath -match '(?i)\\DriverStore\\FileRepository\\([^\\]+)\\([^\\]+)$') {
            $oldFolder = $Matches[1]
            $sysLeaf   = $Matches[2]
            $infBase   = ($oldFolder -split '\.inf_')[0]
            $repo      = "C:\Windows\System32\DriverStore\FileRepository"
            $found = Get-ChildItem -LiteralPath $repo -Directory -ErrorAction SilentlyContinue |
                     Where-Object { $_.Name -like ($infBase + ".inf_*") -and (Test-Path (Join-Path $_.FullName $sysLeaf)) } |
                     Sort-Object LastWriteTime -Descending | Select-Object -First 1
            if ($found) {
                $img = "\SystemRoot\System32\DriverStore\FileRepository\$($found.Name)\$sysLeaf"
                $newStart = Get-RestoreStart $s.Start
                if ($null -eq $newStart) { $newStart = 3 }
                try {
                    New-Item -Path $key -Force | Out-Null
                    New-ItemProperty -LiteralPath $key -Name "Type"         -Value 1 -PropertyType DWord -Force | Out-Null
                    New-ItemProperty -LiteralPath $key -Name "Start"        -Value ([int]$newStart) -PropertyType DWord -Force | Out-Null
                    New-ItemProperty -LiteralPath $key -Name "ErrorControl" -Value 1 -PropertyType DWord -Force | Out-Null
                    New-ItemProperty -LiteralPath $key -Name "ImagePath"    -Value $img -PropertyType ExpandString -Force | Out-Null
                    $back = (Get-ItemProperty -LiteralPath $key -Name ImagePath).ImagePath
                    if ($back -eq $img) {
                        $made = $true
                        Write-Host ("    서비스 키 만듦  {0}  ->  {1}" -f $s.Name, $img) -ForegroundColor Green
                        Write-Host  "        (Windows 가 재부팅 때 나머지 값을 채웁니다)" -ForegroundColor DarkGray
                    }
                } catch {
                    Write-Host ("    서비스 키 만들기 실패  {0} - {1}" -f $s.Name, $_.Exception.Message) -ForegroundColor Yellow
                }
            }
            else {
                Write-Host ("    서비스 키 없음  {0} - 설치된 패키지를 못 찾아 건너뜀" -f $s.Name) -ForegroundColor Yellow
            }
        }
        if (-not $made) {
            Write-Host ("    서비스 키 없음  {0} - 건너뜀 (재부팅을 두 번 해야 할 수 있습니다)" -f $s.Name) -ForegroundColor Yellow
        }
        continue
    }
    if (-not $s.ImagePath) { continue }

    $leaf = Split-Path ($s.ImagePath -replace '^\\SystemRoot\\','C:\Windows\') -Leaf
    $src  = Join-Path $sysDir $leaf
    $dst  = Join-Path "C:\Windows\System32\drivers" $leaf

    # 손으로 넣어둔 빌드(System32\drivers\ 아래)만 되돌린다.
    # 드라이버 저장소를 가리키던 서비스는 pnputil 이 방금 새로 만든 경로를
    # 쓰게 둔다. 저장소 폴더 이름이 달라질 수 있어서 예전 경로를 덮어쓰면
    # 없는 파일을 가리키게 된다.
    if ($s.ImagePath -notmatch '(?i)\\System32\\drivers\\[^\\]+$') {
        $nowImg = (Get-ItemProperty -LiteralPath $key -ErrorAction SilentlyContinue).PSObject.Properties["ImagePath"]
        Write-Host ("    그대로 둠  {0}  ->  {1}" -f $s.Name, $(if ($nowImg) { $nowImg.Value } else { "(없음)" }))
    }
    elseif (Test-Path -LiteralPath $src) {
        # 이미 같은 파일이 제자리에 있으면 복사하지 않는다.
        #
        # 재부팅 전에 이 스크립트를 돌리면 그 .sys 는 아직 메모리에 올라가
        # 있어서 덮어쓸 수 없다. 예전 판은 여기서 Copy-Item 이 예외를 던지고
        # 스크립트가 통째로 멈춰서, 바로 아래의 ImagePath 복원을 못 했다.
        # 그러면 ImagePath 는 pnputil 이 방금 만든 패키지 기본 빌드를 가리킨
        # 채로 남고, 그대로 재부팅하면 쓰던 빌드가 아니라 옛 빌드가 올라온다.
        # 2026-09-10 에 실제로 그렇게 됐다. 파일은 멀쩡히 제자리에 있었고
        # 해시까지 같았는데 굳이 덮어쓰려다 걸린 것이라, 같으면 건너뛴다.
        $needCopy = $true
        if (Test-Path -LiteralPath $dst) {
            try {
                $hSrc = (Get-FileHash -LiteralPath $src -Algorithm SHA256).Hash
                $hDst = (Get-FileHash -LiteralPath $dst -Algorithm SHA256).Hash
                if ($hSrc -eq $hDst) {
                    $needCopy = $false
                    Write-Host ("    이미 같음  {0}  (복사 생략)" -f $leaf)
                }
            } catch { }
        }

        if ($needCopy) {
            try {
                Copy-Item -LiteralPath $src -Destination $dst -Force
            } catch {
                Write-Host ("    복사 실패  {0}" -f $leaf) -ForegroundColor Yellow
                Write-Host ("      {0}" -f $_.Exception.Message) -ForegroundColor Yellow
                Write-Host  "      ImagePath 는 그래도 되돌립니다." -ForegroundColor Yellow
                Write-Host  "      재부팅한 뒤 이 스크립트를 한 번 더 실행하면 파일도 맞춰집니다." -ForegroundColor Yellow
            }
        }

        if (Test-Path -LiteralPath $dst) {
            $sig = Get-AuthenticodeSignature -LiteralPath $dst
            if ($sig.Status -ne "Valid") {
                Write-Host ("    서명 상태 {0}  {1}" -f $sig.Status, $leaf) -ForegroundColor Yellow
            }
        }

        # 복사가 어떻게 됐든 ImagePath 는 반드시 되돌린다. 이것이 어떤 빌드가
        # 올라올지를 정하는 값이고, 이걸 놓치는 것이 이 스크립트의 유일한
        # 치명적 실패다.
        Set-ItemProperty -LiteralPath $key -Name ImagePath -Value $s.ImagePath -Type ExpandString
        $back = (Get-ItemProperty -LiteralPath $key -EA SilentlyContinue).ImagePath
        if ($back -eq $s.ImagePath) {
            Write-Host ("    복원  {0}  ->  {1}" -f $leaf, $s.ImagePath) -ForegroundColor Green
        } else {
            Write-Host ("    ImagePath 를 되돌리지 못했습니다  {0}" -f $s.Name) -ForegroundColor Red
            Write-Host ("      지금 값 : {0}" -f $back) -ForegroundColor Red
            Write-Host  "      이대로 재부팅하면 예전 빌드가 올라옵니다." -ForegroundColor Red
        }
    }
    else {
        Write-Host ("    백업에 {0} 가 없습니다 - 패키지 기본 빌드로 남습니다." -f $leaf) -ForegroundColor Yellow
    }

    $newStart = Get-RestoreStart $s.Start
    if ($null -ne $newStart) {
        Set-ItemProperty -LiteralPath $key -Name Start -Value ([int]$newStart)
        if ([int]$newStart -ne [int]$s.Start) {
            Write-Host ("    서비스  {0}  Start={1}  (백업에는 {2} - 되살릴 때는 {1})" -f $s.Name, $newStart, $s.Start) -ForegroundColor Yellow
        } else {
            Write-Host ("    서비스  {0}  Start={1}" -f $s.Name, $newStart)
        }
    }
}

# ------------------------------------------------------------------
Write-Host ""
Write-Host "[5/6] 필터 등록 되돌리기"

# UpperFilters / LowerFilters 는 값 형태와 내용을 그대로 기록해뒀으니 직접 되돌린다.
# Filters\*Lower 하위 키 형태(확장 INF 가 만드는 것)는 값의 형식을 기록해두지
# 않았으므로 손대지 않는다. 잘못된 형식으로 써 넣느니 없는 편이 낫다.
# 그건 확장 INF 가 장치에 다시 붙을 때 스스로 만든다. 아래에서 확인한다.
#
# 적으려는 Palm 이름의 패키지가 저장소에 없으면 그 값은 쓰지 않는다. 없는 서비스를 필터로
# 적으면 재부팅 뒤 그 장치가 "없는 필터"를 찾다가 시작하지 못한다 (터치가 멈춤).
$storeNow = @(Get-StoreInfNames)
foreach ($r in $wantRegs) {
    if ($r.Form -ne "MULTISZ") { continue }
    $rp = "Registry::" + $r.Key
    if (-not (Test-Path -LiteralPath $rp)) {
        Write-Host ("    키 없음  {0}" -f $r.Key) -ForegroundColor Yellow
        continue
    }
    $cur  = @(Get-RegValueOrNull -KeyPath $rp -ValueName $r.ValueName)
    $cur  = @($cur | Where-Object { $_ })
    $want = @($r.Data | ForEach-Object { [string]$_ })
    $absent = @($want | Where-Object { $_ -match 'Palm' -and ($storeNow -notcontains ($_.ToLowerInvariant() + '.inf')) })
    if ($absent.Count -gt 0) {
        Write-Host ("    안 씀  {0}  (패키지가 없는 이름: {1})" -f $r.ValueName, ($absent -join ', ')) -ForegroundColor Yellow
        Write-Host ("           at {0}" -f $r.Key) -ForegroundColor Yellow
        continue
    }
    $merged = @($cur)
    foreach ($w in $want) { if ($merged -notcontains $w) { $merged += $w } }

    if (($merged -join ';') -ne ($cur -join ';')) {
        Set-ItemProperty -LiteralPath $rp -Name $r.ValueName -Value ([string[]]$merged) -Type MultiString
        Write-Host ("    기록  {0}={1}" -f $r.ValueName, ($merged -join ';'))
    } else {
        Write-Host ("    이미 있음  {0}={1}" -f $r.ValueName, ($cur -join ';'))
    }
}

# ------------------------------------------------------------------
Write-Host ""
Write-Host "[6/6] 확인"

$nowRegs = @(Find-PalmRegistrations)
$missing = @()
foreach ($r in $wantRegs) {
    $hit = $nowRegs | Where-Object { $_.Key -eq $r.Key -and $_.ValueName -eq $r.ValueName -and $_.Form -eq $r.Form }
    if ($hit) {
        Write-Host ("    있음   [{0}] {1}" -f $r.Form, $r.ValueName) -ForegroundColor Green
    } else {
        Write-Host ("    없음   [{0}] {1}  at {2}" -f $r.Form, $r.ValueName, $r.Key) -ForegroundColor Yellow
        $missing += $r
    }
}

# 재부팅하면 Windows 가 INF 를 다시 처리하면서 ImagePath 를 패키지 자기 것으로
# 되돌려 놓는다. 실제로 그렇게 됐다 (2026-09-08: PalmRejFilter 가 A482R2 대신
# 8월 패키지 기본 빌드로 돌아가 있었고, 등록은 5/5 라 확인은 통과했다).
$imgWrong = @()
foreach ($s in $wantSvcs) {
    if (-not $s.ImagePath) { continue }
    $k = "HKLM:\SYSTEM\CurrentControlSet\Services\$($s.Name)"
    if (-not (Test-Path -LiteralPath $k)) { continue }
    $now = Get-RegValueOrNull -KeyPath $k -ValueName "ImagePath"
    if ($now -ne $s.ImagePath) {
        $imgWrong += [pscustomobject]@{ Name=$s.Name; Want=$s.ImagePath; Now=$now }
        Write-Host ("    빌드 다름  {0}" -f $s.Name) -ForegroundColor Yellow
        Write-Host ("        지금  {0}" -f $now) -ForegroundColor Yellow
        Write-Host ("        원래  {0}" -f $s.ImagePath) -ForegroundColor Yellow
    } else {
        Write-Host ("    빌드 맞음  {0}" -f $s.Name) -ForegroundColor Green
    }
}

$pkgNow = 0
$enum = (& pnputil.exe /enum-drivers 2>&1 | Out-String)
foreach ($block in ($enum -split "`r?`n`r?`n")) { if ($block -match '(?i)palm') { $pkgNow++ } }
Write-Host ("    드라이버 저장소의 Palm 패키지 = {0}" -f $pkgNow)

foreach ($n in $PalmServices) {
    $k = "HKLM:\SYSTEM\CurrentControlSet\Services\$n"
    if (Test-Path -LiteralPath $k) {
        $st = Get-RegValueOrNull -KeyPath $k -ValueName "Start"
        $ip = Get-RegValueOrNull -KeyPath $k -ValueName "ImagePath"
        Write-Host ("    서비스  {0,-16} Start={1}  {2}" -f $n, $st, $ip)
    } else {
        Write-Host ("    서비스  {0,-16} 없음" -f $n) -ForegroundColor Yellow
    }
}

Write-Host ""
Write-Host "============================================================"
if ($missing.Count -gt 0) {
    Write-Host (" 아직 덜 됐습니다 - 등록 {0}개가 안 돌아왔습니다." -f $missing.Count) -ForegroundColor Yellow
    Write-Host ""
    Write-Host " 재부팅하고 타블렛을 껐다 켜신 다음, 'PalmRej 켜기'를 한 번 더" -ForegroundColor Cyan
    Write-Host " 눌러서 위 목록이 전부 '있음' 이 되는지 확인하세요." -ForegroundColor Cyan
    Write-Host " (확장 INF 는 장치가 다시 잡힐 때 스스로 등록합니다)" -ForegroundColor Cyan
    Write-Host ""
    Write-Host " 테스트 서명은 켜져 있으니 터치는 죽지 않습니다." -ForegroundColor Cyan
} else {
    Write-Host " 등록은 전부 돌아왔습니다. 재부팅해야 적용됩니다." -ForegroundColor Cyan
    Write-Host " 재부팅 후 첫 부팅에서는 타블렛을 껐다 켜야 터치가 돌아옵니다." -ForegroundColor Cyan
}

# 쓰던 빌드와 다른 파일을 가리키는 서비스 - 재부팅 뒤 끄기 전과 다른 빌드가 올라올 수 있다.
# 옛 안내(A482R2 설치 스크립트, 재부팅없이_드라이버교체 + 타블렛 껐다 켜기)는 지금은 보관
# 폴더에만 있고, 교체 + 껐다 켜기는 와콤 프로그램이 죽었던 방법이라 권하지 않는다.
# 지금 판의 설치는 끄지 않고 덮어쓰며, 서비스가 그 판의 파일을 가리키게 고친다 (install.ps1 [4/6]).
if ($imgWrong.Count -gt 0) {
    Write-Host ""
    Write-Host (" 쓰던 빌드가 아닌 것이 {0}개 있습니다 (위의 '빌드 다름')." -f $imgWrong.Count) -ForegroundColor Yellow
    Write-Host " 재부팅하면 끄기 전과 다른 빌드가 올라올 수 있습니다." -ForegroundColor Yellow
    Write-Host ""
    Write-Host " => 재부팅한 다음, 받은 zip 을 푼 폴더의 'PalmRej 설치.exe' 로 지금 판을 다시 설치하세요." -ForegroundColor Cyan
    Write-Host "    끄지 않고 그 위에 덮어씁니다. 설치가 끝나면 재부팅 한 번이면 됩니다." -ForegroundColor Cyan
}

# 끄기 뒤처럼 테스트 모드가 꺼진 부팅에서 켰으면, 재부팅 전까지 터치와 펜 일부가 멈춰 있다.
$doneMsg = ""
if (-not $bootTestSigning) {
    Write-Host ""
    Write-Host " 재부팅 전까지 터치와 펜 일부가 멈춰 있습니다. 지금 재부팅하세요." -ForegroundColor Yellow
    $doneMsg = "재부팅 전까지 터치와 펜 일부가 멈춰 있습니다. 지금 재부팅하세요."
}

# 패키지 일부가 안 들어갔으면 성공(0)으로 끝내지 않는다. 2 = 재부팅한 뒤 한 번 더.
if ($failed.Count -gt 0) {
    Write-Host ""
    Write-Host (" 들어가지 않은 패키지가 있습니다: {0}" -f (($failed | Select-Object -Unique) -join ", ")) -ForegroundColor Yellow
    Write-Host " 그 패키지의 등록은 되돌리지 않았습니다. 재부팅한 다음 'PalmRej 켜기'를 한 번 더 누르세요." -ForegroundColor Cyan
    Write-Host "PARTIAL=1"
    Write-Host "============================================================"
    try { Stop-Transcript | Out-Null } catch { }
    Hold
    exit 2
}
Write-Host "============================================================"
Done $doneMsg
exit 0