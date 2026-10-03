#requires -Version 5.1
<#
PalmRej 설치 창 빌드

  .\빌드.ps1 -Version 0.1.13 [-Out <폴더>]
  .\빌드.ps1 -Version 1.0.1-exp -FileVersion 1.0.1.1 [-Out <폴더>]

  결과: <Out>\PalmRej 설치.exe   (기본 Out: 이 폴더의 빌드\)

  -FileVersion 이 없으면 -Version 은 X.Y.Z 숫자만이고 파일 버전은 X.Y.Z.0 (예전과 같음).
  -FileVersion 을 주면 (1.0.1-exp 부터) -Version 에 영문 꼬리표를 붙일 수 있습니다 (X.Y.Z-이름).
  창에 보이는 판 이름은 -Version 그대로, 숫자만 들어가는 파일 버전은 -FileVersion (A.B.C.D,
  앞 세 자리는 -Version 의 숫자와 같아야 함).

  .NET Framework 4.8 용 (Windows 10/11 에 이미 들어 있음). Visual Studio 의
  Roslyn csc 와 4.8.1 참조 어셈블리로 컴파일합니다. 경고도 오류로 칩니다.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Version,
    [string]$Out,
    [string]$FileVersion
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if (-not $FileVersion) {
    if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "버전은 X.Y.Z 형식이어야 합니다: $Version" }
    $FileVersion = "$Version.0"
} else {
    if ($Version -notmatch '^(\d+\.\d+\.\d+)(-[A-Za-z][A-Za-z0-9]*)?$') { throw "버전은 X.Y.Z 또는 X.Y.Z-이름 형식이어야 합니다: $Version" }
    $num = $Matches[1]
    if ($FileVersion -notmatch '^(\d+\.\d+\.\d+)\.\d+$') { throw "파일 버전은 A.B.C.D 형식이어야 합니다: $FileVersion" }
    if ($Matches[1] -ne $num) { throw "파일 버전 $FileVersion 의 앞 세 자리가 판 $Version 과 다릅니다" }
}

$Here = $PSScriptRoot
$Src  = Join-Path $Here "소스"
if (-not $Out) { $Out = Join-Path $Here "빌드" }
$csc  = "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\Roslyn\csc.exe"
$ref  = "C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8.1"
foreach ($p in @($csc, $ref, (Join-Path $Src "Setup.cs"), (Join-Path $Src "app.manifest"), (Join-Path $Src "PalmRej.ico"))) {
    if (-not (Test-Path -LiteralPath $p)) { throw "없음: $p" }
}
New-Item -ItemType Directory -Force -Path $Out | Out-Null

# 버전과 파일 정보. TargetFramework 가 없으면 .NET Framework 가 4.0 용 앱으로 보고 옛 동작을 켠다.
$gen = Join-Path $Out "AssemblyInfo.g.cs"
$info = @"
[assembly: System.Reflection.AssemblyTitle("PalmRej 설치")]
[assembly: System.Reflection.AssemblyDescription("PalmRej 설치와 제거")]
[assembly: System.Reflection.AssemblyProduct("PalmRej")]
[assembly: System.Reflection.AssemblyCompany("PalmRej")]
[assembly: System.Reflection.AssemblyCopyright("")]
[assembly: System.Reflection.AssemblyVersion("$FileVersion")]
[assembly: System.Reflection.AssemblyFileVersion("$FileVersion")]
[assembly: System.Reflection.AssemblyInformationalVersion("$Version")]
[assembly: System.Runtime.Versioning.TargetFramework(".NETFramework,Version=v4.8", FrameworkDisplayName = ".NET Framework 4.8")]
"@
[IO.File]::WriteAllText($gen, $info, (New-Object Text.UTF8Encoding $true))

$exe = Join-Path $Out "PalmRej 설치.exe"
if (Test-Path -LiteralPath $exe) { Remove-Item -LiteralPath $exe -Force }

$cscArgs = @(
    '/nologo', '/noconfig', '/nostdlib+', '/target:winexe', '/platform:anycpu', '/optimize+', '/deterministic',
    '/langversion:latest', '/nullable:enable', '/warn:4', '/warnaserror+', '/utf8output', '/codepage:65001',
    "/reference:$ref\mscorlib.dll",
    "/reference:$ref\System.dll",
    "/reference:$ref\System.Core.dll",
    "/reference:$ref\System.Drawing.dll",
    "/reference:$ref\System.Windows.Forms.dll",
    "/win32manifest:$Src\app.manifest",
    "/win32icon:$Src\PalmRej.ico",
    "/out:$exe",
    (Join-Path $Src "Setup.cs"),
    $gen
)
$o = & $csc @cscArgs 2>&1 | Out-String
$code = $LASTEXITCODE
if ($o.Trim()) { Write-Host $o.Trim() }
if ($code -ne 0) { throw "컴파일 실패 (csc 종료 코드 $code)" }
if (-not (Test-Path -LiteralPath $exe)) { throw "exe 가 만들어지지 않았습니다: $exe" }

$fv = (Get-Item -LiteralPath $exe).VersionInfo.FileVersion
if ($fv -ne $FileVersion) { throw "파일 버전이 $fv 입니다 ($FileVersion 이어야 함)" }
$pv = (Get-Item -LiteralPath $exe).VersionInfo.ProductVersion
if ($pv -ne $Version) { throw "제품 버전(창에 보이는 판)이 $pv 입니다 ($Version 이어야 함)" }
Write-Host ("완료: {0}  ({1:N0} 바이트, 버전 {2})" -f $exe, (Get-Item -LiteralPath $exe).Length, $fv)
