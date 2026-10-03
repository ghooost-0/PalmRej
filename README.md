# PalmRej

**English:** An unofficial palm-rejection driver for the Wacom Cintiq Pro 24 (DTH-2420) on Windows, not affiliated with Wacom. See the [English section](#english) below. The installer and app are in Korean only.

Wacom Cintiq Pro 24 (DTH-2420)용 손날 인식 드라이버 **(비공식)**

손날(손바닥 옆면)이 화면에 닿아 생기는 터치 오작동을 최대한 막습니다. 100% 막는 것은 사실상 불가능하고, 만든 사람이 실제로 쓰면서 본 바로는 99% 정도를 막습니다.
손날을 짚은 직후에도 한 손가락·두 손가락 탭과 드래그가 바로 됩니다.

> 개인이 만든 비공식 드라이버이며 Wacom 과 관계가 없습니다.

## 먼저 알아 두세요

- 만든 사람은 개발자가 아닙니다. 코딩이나 개발을 모르고, 대부분 GPT 와 Claude 를 이용해 만들었습니다. 만든 사람이 모르는 문제가 여럿 있을 수 있습니다.
- 시험은 만든 사람 혼자, 자기 장비(Cintiq Pro 24, Windows 10)에서만 했습니다.
- 만든 사람이 쓰는 방식에 맞춘 드라이버입니다. 예를 들어 세 손가락 이상 터치는 모두 막는데, 손바닥과 구별이 안 되기도 하지만 만든 사람이 쓰지 않아서 막은 것이기도 합니다.
- 그래서 다른 사람이 쓰면 어떻게 동작할지는 만든 사람도 잘 모릅니다.

## 받기

오른쪽 **Releases** 에서 최신 `PalmRej_x.y.z.zip` 을 받으세요.
받은 zip 의 SHA-256 값이 릴리스에 적힌 값과 같은지 확인하면 더 안전합니다.

## 테스트 모드가 무엇이고 왜 켜야 하나요

- Windows 는 원래 Microsoft 가 인정한 서명이 있는 드라이버만 실행합니다. 그 서명은 비용이 커서, 개인이 만든 이 드라이버는 "테스트 서명"으로만 실행할 수 있습니다.
- **테스트 모드**는 테스트 서명 드라이버도 실행되게 허용하는 Windows 설정입니다. 켜면 바탕화면 오른쪽 아래에 "테스트 모드" 글씨가 보입니다.
- 대신 다른 테스트 서명 드라이버도 실행될 수 있게 되므로 그만큼 PC 보안이 낮아집니다.
- **보안 부팅(Secure Boot)** 이 켜져 있으면 테스트 모드가 동작하지 않아서 설치되지 않습니다. BIOS(UEFI) 설정에서 꺼야 합니다. BIOS 업데이트나 초기화로 보안 부팅이 다시 켜지면 터치가 안 되니 다시 꺼야 합니다.

## 설치하기 전에 꼭 읽어 주세요

- 설치하면 Windows 테스트 모드가 켜집니다 (위 설명).
- **테스트 모드가 켜져 있으면 실행되지 않거나 문제 삼는 프로그램이 있습니다.** 뱅가드 같은 안티치트가 대표적이고(테스트 모드가 켜져 있으면 게임이 실행되지 않습니다), 다른 프로그램도 그럴 수 있습니다.
  그런 프로그램을 쓰기 전에는 관리 앱에서 'PalmRej 끄기' → 재부팅 하세요 (테스트 모드가 꺼집니다). 다시 쓰려면 'PalmRej 켜기' → 재부팅.
  뱅가드는 재부팅 한 번으로 안 될 때가 있습니다. 'PalmRej 끄기' → 재부팅 뒤에도 게임이 안 켜지면 한 번 더 재부팅하세요 (만든 사람이 해 본 것 중에는 뱅가드만 그랬습니다).
- 보안 부팅이 켜져 있어야 실행되는 프로그램은 PalmRej 와 함께 쓸 수 없습니다 (PalmRej 는 보안 부팅을 꺼야 합니다).
- 와콤 드라이버(Wacom Center)가 먼저 설치돼 있어야 합니다.
- Cintiq Pro 24, Windows 10, 와콤 드라이버 6.4.14 에서만 확인했습니다. 다른 기종은 설치할 때 경고가 뜹니다.
- 있는 그대로 드립니다. 어떤 보증도 하지 않습니다.

## 설치

1. zip 을 풉니다. (압축 파일 안에서 바로 실행하지 마세요)
2. 타블렛을 켜고 연결합니다.
3. `PalmRej 설치.exe` → 관리자 권한 "예" → [설치] → 끝나면 [지금 재부팅]

"Windows의 PC 보호" 창이 뜨면 [추가 정보] → [실행] 을 누르세요. 서명되지 않은 개인 제작 설치 파일이라 뜨는 창입니다.

**설치, 제거, 켜기, 끄기 뒤에는 반드시 재부팅해야 합니다.** 재부팅 전까지는 터치나 펜이 제대로 동작하지 않을 수 있습니다.
재부팅한 뒤 터치가 안 되면 타블렛 전원을 한 번 껐다 켜세요 (설치 뒤 첫 부팅에만 가끔 생깁니다).

자세한 내용은 zip 안의 `읽어보세요.txt` 에 있습니다.

## 알아 두실 점

**세 손가락 이상 터치는 막힙니다**

손날이나 손바닥이 화면에 닿으면 보통 접점이 세 개 이상으로 잡힙니다. 세 손가락과 손바닥은 확실하게 구별되지 않아서(특히 탭이나 누르고 있기), Cintiq Pro 24 에서 PalmRej 는 Windows 로 가는 터치 가운데 세 개 이상이 동시에 닿은 것을 모두 손바닥으로 보고 막습니다. 그래서 앱이 직접 받는 세 손가락 이상 제스처는 동작하지 않습니다. 한 손가락·두 손가락 터치와 제스처는 그대로 됩니다.

**와콤 센터의 터치 제스처는 그대로 동작합니다**

Wacom Center 에서 설정한 터치 제스처(두 손가락 스크롤·확대·회전 등)는 와콤 프로그램이 따로 처리합니다. PalmRej 는 그 경로를 건드리지 않아서 설치 전과 똑같이 동작합니다. 다만 그 경로에서는 PalmRej 가 손날을 걸러 주지 않습니다. 와콤 센터에서 세 손가락 이상 제스처를 켜 두면 손을 올려놓을 때 그 제스처가 실행될 수 있으니 꺼 두는 것을 권합니다.

**맨손이나 얇은 장갑으로 쓰는 것을 권합니다**

PalmRej 는 화면에 닿은 터치가 어떻게 잡혔는지(접점의 크기와 개수 등)를 보고 손날인지 손가락인지 가려서 손날만 막습니다. 천이 여러 겹인 두꺼운 장갑을 끼면 손날이 닿아도 작게 잡히거나 끊겨 잡혀서 손가락처럼 보일 수 있고, 그러면 오히려 덜 막힐 수 있습니다. 맨손이나 얇은 장갑으로 쓰는 것을 권합니다.

## 문제 알리기 · 다른 와콤 기종

Cintiq Pro 24 가 아닌 와콤 기종에서는 설치할 때 "확인되지 않은 기종" 경고가 뜹니다. 계속하면 타블렛 구조를 비교해서, 같으면 손날 인식만 설치하고 다르면 아무것도 바꾸지 않습니다. 다른 기종에서 써 보셨거나 문제가 생기면 아래 내용을 모아 알려 주세요.

기록을 보내 주시는 것은 선택입니다. 보내 주셔도 그 기종에서 되는 판이 꼭 나온다는 약속은 드릴 수 없습니다 (개인이 만드는 프로그램이고, 기종마다 구조가 달라 직접 확인하기 어렵습니다).

**1. 적어 주실 것**

- 타블렛 기종 이름 (예: Cintiq Pro 16, DTH-1620)
- Windows 버전 (설정 > 시스템 > 정보)
- 무엇을 했을 때 어떻게 됐는지

**2. 설치 기록**

- 압축을 푼 폴더의 `설치기록_날짜.txt` (설치 창의 [기록 파일 열기] 로도 열립니다)
- 이 기록에는 PC 이름과 Windows 사용자 이름이 여러 곳에 들어 있습니다 (맨 위 "사용자 이름", "RunAs 사용자", "Machine", "호스트 응용 프로그램" 줄과 `C:\Users\사용자이름\...` 처럼 폴더 경로가 적힌 줄). PC 이름은 "Machine:" 뒤에, 사용자 이름은 "사용자 이름:" 줄의 `\` 뒤에 있습니다. 메모장에서 Ctrl+H(바꾸기)로 두 이름을 하나씩 따로 PC, USER 같은 다른 글자로 [모두 바꾸기] 하세요. `C:\Users\` 뒤의 폴더 이름이 사용자 이름과 다르면 그것도 바꿉니다. 저장한 뒤 Ctrl+F 로 두 이름을 찾아 더 나오지 않으면 올려 주세요.

**3. 진단 로그 (설치가 된 경우)**

1. 관리 앱(바탕화면 "PalmRej 관리") 아래쪽의 **진단 로그 기록** → [기록 시작]
2. 1~2분 동안 문제가 생기는 동작을 합니다 (손날 올리기, 한 손가락·두 손가락 탭과 드래그 등).
3. [멈추고 저장] → 저장이 끝나면 파일이 있는 폴더가 저절로 열립니다 (기록 창의 "저장 위치" 칸에 적힌 곳)
4. 저장된 `… 진단 로그 날짜.log` 와 `… 요약.txt` 두 파일을 zip 으로 압축합니다 (두 파일 선택 → 오른쪽 클릭 → 보내기 → 압축(ZIP) 폴더).

진단 로그는 1분에 20 MB 안팎으로 커서 짧게 기록하는 것이 좋습니다. 압축하면 10분의 1 정도로 줄어듭니다. 압축한 파일이 25 MB 를 넘으면 GitHub 에 올라가지 않으니 더 짧게 다시 기록해 주세요. 진단 로그와 요약에는 보통 PC 이름이나 사용자 이름이 들어가지 않습니다. 요약에 "오류원문" 줄이 있으면 그 줄의 폴더 경로도 같은 방법으로 바꿔 주세요.

**4. 보내는 곳**

이 저장소의 **Issues** → [New issue] 에 1번 내용을 적고, 2·3번 파일을 끌어다 놓아 올려 주세요 (GitHub 계정이 필요합니다). Issues 는 누구나 볼 수 있으니 개인 정보를 지웠는지 한 번 더 확인해 주세요.

## 지우기

설정 > 앱 > "PalmRej (Wacom 손날 인식)" > 제거 → 재부팅

지우고 재부팅하면 테스트 모드가 꺼지고, 터치는 와콤 기본 드라이버로 계속 됩니다.

## 소스 코드

1.1.0 부터 소스 코드를 MIT 라이선스로 공개합니다. [소스](소스) 폴더에 1.1.0 배포 파일을 만든 소스가 있습니다 (드라이버 세 개, 관리 앱, 설치 창, 설치·제거·켜기·끄기 스크립트).

빌드하는 법과 주의할 점은 [빌드 안내](소스/빌드_안내.md) 에 있습니다. 만든 사람의 서명 키는 공개하지 않으므로, 직접 빌드한 드라이버는 자기 테스트 서명 인증서로 서명해야 합니다.

## 사용 조건

1.1.0 부터 PalmRej 는 MIT 라이선스입니다. 전문(영어)은 [LICENSE](LICENSE) 에 있습니다.

- 누구나 무료로 쓸 수 있습니다. 그림 작업처럼 돈을 버는 일에 써도 됩니다.
- 고쳐도 되고, 원래 것이나 고친 것을 다른 곳에 다시 올리거나 팔아도 됩니다.
- 지킬 것은 하나입니다. 다시 배포할 때는 (일부만 쓰더라도) 저작권 표시 `Copyright (c) 2026 ghooost-0` 와 라이선스 전문을 함께 넣어 주세요.
- 있는 그대로 드립니다. 어떤 보증도 하지 않으며, 쓰다가 생긴 문제를 만든 사람이 책임지지 않습니다.

1.0.1 이하(1.0.0, 1.0.1)의 배포 파일은 MIT 가 아니라, 그 판을 낼 때 함께 넣은 사용 조건을 그대로 따릅니다.

Visual Studio / WDK 템플릿이 만든 파일 몇 개처럼 MIT 가 적용되지 않는 부분은 [빌드 안내](소스/빌드_안내.md) 의 "MIT 가 적용되지 않는 부분" 에 적었습니다.

Wacom, Cintiq 은 Wacom Co., Ltd. 의 상표입니다.

## English

PalmRej is an **unofficial** palm-rejection driver for the Wacom Cintiq Pro 24 with touch (DTH-2420) on Windows. It was made by an individual and is **not affiliated with Wacom**.

**The installer, the manager app and all other documents are in Korean only.** This section covers the main points, and the table under "Install" gives the English meaning of the Korean labels you will see. Message boxes show Korean text, but their Yes / No / OK / Cancel buttons are in your Windows display language.

### What it does

- It blocks as many accidental touches from the side of your hand (the "hand edge") as possible.
- It looks at how each touch registers (contact size, number of contacts and so on) and blocks only the hand edge.
- One- and two-finger taps and drags still work, even right after the hand edge touches down.
- Blocking 100% is practically impossible. In the maker's own use, it blocks about 99% of these touches.
- There are no settings. The behavior is fixed.

### Before you start

- The maker is not a developer and does not know how to code. PalmRej was built mostly with GPT and Claude. There may be a number of problems the maker is not aware of.
- Only the maker has tested it, on their own device (Cintiq Pro 24, Windows 10).
- It is tailored to the maker's own way of working. For example, all touches with three or more fingers are blocked, partly because they cannot be told apart from a palm and partly because the maker does not use them.
- So the maker does not really know how it will behave for other people.

### Known behavior

- **Three or more fingers are blocked.** A hand edge or palm usually registers as three or more contacts, and three fingers cannot be reliably told apart from a palm (especially for taps and press-and-hold). So on the Cintiq Pro 24, PalmRej treats any touch sent to Windows with three or more contacts at once as a palm and blocks it. As a result, gestures with three or more fingers in apps (which receive touch through Windows) do not work. One- and two-finger touches and gestures work as usual.
- **Wacom Center touch gestures work as before.** Touch gestures set in Wacom Center (two-finger scroll, zoom, rotate and so on) are handled by Wacom's own software. PalmRej does not change that path, so these gestures behave exactly as they did before you installed it. This also means PalmRej does not filter the hand edge for them. If gestures with three or more fingers are turned on in Wacom Center, resting your hand on the screen can trigger them, so it is best to turn them off.
- **Use a bare hand or a thin glove.** With a thick glove made of several layers of cloth, the hand edge can register as a small or broken-up contact and look like a finger, so it may be blocked less often.

### Requirements and risks

- **Tablet:** Cintiq Pro 24 with touch (DTH-2420). On other Wacom models, the installer shows an "unverified model" warning (확인되지 않은 기종). If you continue, it compares your tablet's structure with the Cintiq Pro 24's. If they match, it installs only the hand-edge filter, without the extra part that keeps one- and two-finger taps and drags working right after the hand edge touches down. If they do not match, it changes nothing. If touch or the pen acts oddly after installing on another model, press PalmRej 끄기 (Turn PalmRej off) in the manager app and reboot.
- **Windows:** tested only on Windows 10 (64-bit, 22H2) with Wacom driver 6.4.14. Not tested on Windows 11. On Windows 11, Smart App Control (if it is on) may block the unsigned installer.
- **Wacom driver:** the Wacom driver (Wacom Center) must be installed first, and the tablet must be on and connected.
- **Test mode:** Windows normally runs only drivers with a Microsoft-approved signature. That signature is expensive, so this driver can only run with a test signature. Installing PalmRej turns on Windows test mode, and the words "Test Mode" appear in the bottom-right corner of the desktop. Other test-signed drivers can then run too, which lowers your PC's security. The installer also adds the driver's own signing certificate (for code signing only) to your PC's trusted certificates, so that the drivers install without warning prompts.
- **Secure Boot must be off** (in your BIOS/UEFI settings). If it is on, the installer tells you and changes nothing. If a BIOS update or reset turns it back on, touch stops working (the pen and mouse still work). Turn Secure Boot off again, or press PalmRej 끄기 (Turn PalmRej off) and reboot. Programs that require Secure Boot cannot be used with PalmRej.
- **Anti-cheat and similar programs:** some programs refuse to run, or report a problem, while test mode is on. Anti-cheat such as Vanguard is the typical case (the game will not start). Before using such a program, open the manager app (PalmRej 관리 on your desktop), press PalmRej 끄기 (Turn PalmRej off), reboot, and check that the "Test Mode" text is gone. If it is still there, do not use that program; follow what the manager app shows (the table under "Install" translates its messages). To use PalmRej again, press PalmRej 켜기 (Turn PalmRej on) and reboot. Vanguard sometimes needs one more reboot: if the game still does not start, reboot again. (Of the programs the maker tried, only Vanguard needed this.)
- **BitLocker:** the installer pauses BitLocker protection for one reboot only, so that turning on test mode does not trigger a BitLocker recovery-key prompt. Protection resumes automatically.
- **As is:** PalmRej is provided as is, with no warranty. The maker is not responsible for problems caused by using it, such as data loss, device failure or game account sanctions.

### Download

Download the latest `PalmRej_x.y.z.zip` from [Releases](https://github.com/ghooost-0/PalmRej/releases/latest). For extra safety, check that the zip's SHA-256 hash matches the one on the release page. In PowerShell: `Get-FileHash -Algorithm SHA256 <path to the zip>`.

### Install

1. Extract the zip. Do not run anything from inside the zip.
2. Turn the tablet on and connect it.
3. Run `PalmRej 설치.exe` (PalmRej Setup). If "Windows protected your PC" appears, click **More info**, then **Run anyway**. This warning appears because the installer is an unsigned file made by an individual. Do this only for the installer from a zip you downloaded from the Releases page above, ideally after checking its SHA-256.
4. When Windows asks "Do you want to allow this app to make changes to your device?", click **Yes**.
5. Click **설치** (Install). When it finishes, save any open work, click **지금 재부팅** (Reboot now), and confirm with **OK**.

**Always reboot after installing, uninstalling, or turning PalmRej on or off.** Until you do, touch or the pen may not work properly. If touch does not work after the reboot, turn the tablet off and on once (this occasionally happens on the first boot after installing).

Keep the zip; you need it to reinstall. To upgrade from an older version, do not uninstall it. Close the manager app if it is open, run the new installer, and reboot once. If you pressed PalmRej 끄기 (Turn PalmRej off) and have not rebooted since, reboot first. The full guide (in Korean) is `읽어보세요.txt` ("Read me") in the zip.

| Korean label | Meaning |
|---|---|
| 설치 / 취소 | Install / Cancel |
| 지금 재부팅 / 나중에 / 닫기 | Reboot now / Later / Close |
| 기록 파일 열기 | Open log file (the install log) |
| 확인되지 않은 기종 | Unverified model (warning on tablets other than the Cintiq Pro 24) |
| 계속할까요? | Continue? (Yes / No) |
| PalmRej 관리 | PalmRej Manager (the manager app, on your desktop and in the Start menu; it asks for administrator permission when it opens) |
| 드라이버 / 테스트 모드 / 타블렛 | Driver / Test mode / Tablet (status rows in the manager app) |
| PalmRej 켜짐 / 정상 동작 중입니다 | PalmRej is on / Working normally (green = OK) |
| PalmRej 꺼짐 / 재부팅이 필요합니다 | PalmRej is off / Reboot needed |
| PalmRej 끄기 / PalmRej 켜기 | Turn PalmRej off / Turn PalmRej on |
| 진단 로그 기록 | Record diagnostic log (link at the bottom of the manager app) |
| 기록 시작 / 멈추고 저장 | Start recording / Stop and save |
| 저장 위치 / 위치 바꾸기 | Save location / Change location |

### If something goes wrong

- **Touch does not work:** turn the tablet off and on. If that does not help, reboot. If it still does not work, press PalmRej 끄기 (Turn PalmRej off) and reboot.
- **The manager app shows 끄기가 덜 끝났습니다 / 재부팅하지 마세요** (turning off did not finish / do not reboot): do not reboot. Press PalmRej 끄기 again. Rebooting in that state can stop touch from working.
- **After a big Windows update:** open the manager app once and check that it is green. If it is not, extract the zip again, run `PalmRej 설치.exe` and reboot.

### Turn off or uninstall

- **Turn off:** in the manager app, press PalmRej 끄기, then reboot. This turns off only the hand-edge filtering, not touch itself: touch keeps working through Wacom's default driver. Windows test mode is turned off too. To turn PalmRej back on, press PalmRej 켜기 and reboot. Turning off keeps the signing certificate; if you no longer need PalmRej, uninstall it, which also removes the certificate.
- **Uninstall:** Settings > Apps (on Windows 10: Apps & features) > **PalmRej (Wacom 손날 인식)** > Uninstall. Click **Yes** when Windows asks for permission, **Yes** at 계속할까요? (Continue?), and **지금 재부팅** (Reboot now) at the end. This removes the drivers, the manager app, the shortcuts and the signing certificate. After the reboot, test mode is off and touch keeps working through Wacom's default driver.
- **If an install stopped halfway,** PalmRej may be missing from Settings > Apps. Run the installer again to the end, then uninstall as above. The manual cleanup steps are in `읽어보세요.txt` (Korean), but do not clean up by hand if the install log contains the line 드라이버 패키지를 추가했습니다 (driver package added): the drivers are already in place, and turning test mode off would stop touch. Send the install log instead (see "Reporting problems").

### Reporting problems

Open a new issue on the [Issues](https://github.com/ghooost-0/PalmRej/issues) page (you need a GitHub account). **Issues are public**, so check again that your personal information is removed before you post. Sending logs is optional. Even if you send them, there is no promise of a version for other models.

- **What to write:** your tablet model (e.g. Cintiq Pro 16, DTH-1620), your Windows version (Settings > System > About), and what you did and what happened.
- **Install log:** `설치기록_<date>.txt` (설치기록 = "install log") in the extracted folder. You can also open it with **기록 파일 열기** (Open log file) in the installer. **It contains your PC name and Windows user name in several places:** the header lines at the top (on English Windows: Username, RunAs User, Machine, Host Application) and folder paths such as `C:\Users\<name>\...`. Your PC name comes after "Machine:", and your user name comes after the `\` in the "Username:" line. Open the file in Notepad, press Ctrl+H, and replace each name separately with a placeholder such as PC or USER, using Replace All. If the folder name after `C:\Users\` is not the same as your user name, replace it too. Save the file, then search for both names with Ctrl+F. Upload it only when neither name is found.
- **Diagnostic log** (only if the install succeeded): in the manager app, click **진단 로그 기록** (Record diagnostic log) at the bottom, then **기록 시작** (Start recording). For 1-2 minutes, do what causes the problem (for example, resting your hand edge, or one- and two-finger taps and drags), then press **멈추고 저장** (Stop and save). The folder with the saved files opens automatically. Zip the two files, `… 진단 로그 <date>.log` (the log) and `… 요약.txt` (the summary): select both, right-click, and choose Send to > Compressed (zipped) folder. Attach the zip. The log grows by about 20 MB per minute, and zipping makes it about ten times smaller. GitHub does not accept a zip over 25 MB; if yours is bigger, record a shorter log. The log and summary usually do not contain your PC or user name, but if the summary has an "오류원문" (original error text) line, replace the folder paths in that line too.

### Source code and license

Starting with 1.1.0, PalmRej is released under the [MIT License](LICENSE) (`Copyright (c) 2026 ghooost-0`). The [소스](소스) ("source") folder contains the source code for the 1.1.0 release: the three drivers, the manager app, the installer window, and the scripts that install, uninstall, and turn PalmRej on and off. Anyone may use it for free, including for paid work such as illustration, and may modify, redistribute or sell it. If you redistribute it, even in part, include the copyright notice and the full license text. It is provided "as is", without warranty, and the maker is not responsible for any problems caused by using it.

Releases 1.0.0 and 1.0.1 are not under the MIT License; they keep the terms they shipped with. A few Visual Studio / WDK template files are not covered by the MIT License (see the build guide).

The build guide, [소스/빌드_안내.md](소스/빌드_안내.md), is in Korean, with a short English summary at the end. The maker's signing key is not published, so you must sign drivers you build yourself with your own test-signing certificate. They will load only in test-signing mode (with Secure Boot off).

### Signing certificate

The drivers are signed with a self-made test-signing certificate: `CN=PalmRej Test Signing`, thumbprint `02917AB8321CF79752BA799177759B81BA009C01`. The installer adds it to your PC's trusted certificates (for code signing only) so that the drivers install without warning prompts, and uninstalling removes it. The installer program itself is not signed.

Wacom and Cintiq are trademarks of Wacom Co., Ltd.
