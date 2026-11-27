# Lupin Farm

Powershell 환경으로 즐기는 개인용 농사 타이쿤 **Lupin Farm**입니다.
Claude Code처럼 생긴 터미널 화면에서 영어 슬래시 명령어로 농사를 짓고, 팔고, 땅과 농기구에 투자합니다. 위쪽은 도트로 그린 농장, 아래쪽은 명령어와 게임 로그입니다.

현재 **M5(편의·밸런스)** 까지 만들었습니다: 작물 10종과 해금, 계절, 매일 바뀌는 시세, 장비 8종과 `when … do …` 규칙 자동화, 총자산 기록(체크포인트·일별·랭킹), 프레스티지 Lupin Bloom과 영구 보너스, 작물 도감·업적, 작물별 심는 시기 표(`/crops`)에 더해, 한글 자판 상태에서 친 명령 보정, 넓은 창 구역 2개 보기, 세이브 내보내기·가져오기(`/save`), 시뮬레이션으로 맞춘 진행 속도가 있습니다.

## 실행 준비

없습니다. Windows 11에 기본으로 들어 있는 .NET Framework 4.8과 C# 컴파일러로 빌드하고 실행합니다.

## 빌드 / 실행

```bat
build.bat           :: bin\lupin.exe 빌드
build.bat test      :: 테스트 빌드 후 실행
```

Windows Terminal의 **PowerShell 탭**에서 실행합니다. 창은 120×30 이상이어야 합니다.

```powershell
.\bin\lupin.exe
```

짧은 명령으로 실행하려면 `$PROFILE`(PowerShell 프로필)에 함수를 추가합니다.

```powershell
function lupin { & "C:\dev\git\my\wall-geop-lupin\bin\lupin.exe" }
```

## 시작하기

```text
/buy seed turnip 6        순무 씨앗 6개 (60G)
/plant turnip A1          심기 (Lv1 농기구는 한 번에 한 칸)
/water A1                 물 주기 (게임 24시간 = 실제 24분 유지)
/harvest ready            다 자란 칸 수확
/sell all                 창고 전부 판매
/upgrade bag              씨앗 주머니 Lv2 (200G) → 한 줄씩 심기
```

- 시간: 실제 1분 = 게임 1시간 (`/config timescale`로 변경). 순무는 게임 2일(실제 48분)에 자랍니다.
- 작물은 물이 있는 동안만 자라고, 물이 없어도 시들지 않습니다.
- 꺼져 있던 시간은 최대 2시간까지 반영됩니다.
- 범위: `A1`, `A1:A4`, `A`(한 줄), `all`, 다른 구역은 `f2.A1`.
- 한/영이 한글인 채로 쳐도 됩니다: `/ㅔㅣ뭇`은 `/plant`로 바뀌어 들어갑니다(입력창 위에 안내). `/save export`·`import` 뒤의 파일 경로는 그대로 둡니다.
- 창이 가로 204칸 이상이고 구역이 2개 이상이면 구역 2개를 나란히 보여 줍니다. 명령의 기본 대상은 주황으로 표시한 보는 구역입니다.
- 계절은 7일씩(봄 → 여름 → 가을 → 겨울). 제철 작물만 심을 수 있고, 이미 심은 작물은 계절이 바뀌어도 끝까지 자랍니다.
- 시세는 매일 06:00에 바뀝니다. 한꺼번에 많이 팔면 값이 내려가고, 제철이 아닌 작물은 1.3배로 팔립니다.

| 명령 | 내용 |
|---|---|
| `/growing` | 자라는 작물별 남은 시간 (마른 칸, 물이 먼저 끝나는 칸 표시) |
| `/crops` | 작물별 심는 시기: 지금 심을 수 있는지(며칠 남았는지), 아니면 며칠 뒤 어느 계절에 되는지 |
| `/market` | 오늘 시세, 어제 대비 (오르면 초록, 내리면 빨강) |
| `/inv` | 창고와 지금 팔면 받을 금액, 씨앗 |
| `/map` | 모든 구역 요약 |
| `/unlock <작물>` | 새 작물 해금 (예: `/unlock potato` 200G) |
| `/upgrade storage` | 창고 용량 2배 |

### 자동화

장비가 기능을 열고, 규칙으로 조합합니다.

```text
/buy sprinkler            스프링클러 (300G) → 보관함
/place sprinkler B3       설치: 주변 칸에 바로, 그리고 매일 06:00에 물 (치는 동안 노란 테두리로 범위 미리 보기)
/place sprinkler B5 1     보관함의 Lv1을 골라 설치 (레벨을 안 적으면 가장 높은 레벨)
/buy controller           농장 컨트롤러 Lv1 (1,000G): 규칙 2개, 게임 1시간마다 검사
/buy harvester            보는 구역에 자동 수확기 (1,500G)
/auto add when f1 ready do harvest
/buy shipping-bin         출하함 (1,000G)
/auto add when stock all >= 40 do sell all
/auto list                규칙 목록, 동작 여부
```

| 장비 | 가격 | 하는 일 |
| --- | --- | --- |
| `sprinkler` | 300G (Lv2 2,000G, Lv3 10,000G) | 칸에 설치. 물 범위 Lv1 상하좌우 4칸, Lv2 둘레 8칸, Lv3 사방 2칸(3줄 구역이라 최대 14칸). 농장 그림에서 숫자 = 레벨, 파란 테두리 = 물 범위, 노란 테두리 = 설치·업그레이드 미리 보기. 정보 칸 `in box`에 아직 놓지 않은 수 |
| `harvester` / `planter` | 1,500G / 5,000G (구역마다) | 규칙 `harvest` / `plant` |
| `greenhouse` | 50,000G (구역마다) | 계절과 상관없이 심고 다시 열림 |
| `shipping-bin` / `auto-buyer` | 1,000G / 4,000G | 규칙 `sell` / `buy` |
| `controller` | 1,000G, 이후 `/upgrade` | 규칙 슬롯 레벨 × 2, 검사 주기 60분 → 30분 → 10분 (게임 시간) |
| `caretaker` | 1,000G, 이후 `/upgrade` | 꺼져 있던 시간 반영 상한 2h → 4h → 8h → 16h → 64h |

게임 안에서는 `/help <이름>`(예: `/help harvester`, `/help can`)으로 농기구·창고·장비마다 하는 일, 가격, 쓰는 법, 지금 상태를 볼 수 있습니다. 자동 수확기·자동 파종기·출하함·자동 구매기는 사기만 해서는 동작하지 않고, 컨트롤러와 규칙이 함께 있어야 합니다.

- 규칙: `when <조건> do <행동>`. 조건 `f1 ready`, `f1 empty`, `price <작물> >= n`, `stock <작물|all> >= n`, `seed <작물> < n` (구역은 `all`도 됨). 행동 `harvest [fN]`, `plant <작물> [fN]`, `sell <작물> [n]`, `sell all`, `buy seed <작물> <n>`. `Tab` 자동완성이 끝까지 안내합니다.
- 켜진 규칙 중 위에서부터 슬롯 수만큼 동작합니다 (`/auto move`로 순서). `/auto on|off|del <n>`.
- 실행 결과는 주황 `● Auto#n(…)`, 못 한 이유는 빨간 로그로 한 번만 남깁니다.

### 기록과 프레스티지

점수는 **총자산**(골드 + 창고·씨앗 평가액 + 투자액)입니다. 엔딩 대신 지난 기록을 넘어서는 것이 목표입니다.

| 명령 | 내용 |
| --- | --- |
| `/records` | Day 7, 14, 28, 56, …이 끝날 때의 총자산을 지난 판들의 최고 기록과 비교 (`NEW BEST`) |
| `/records daily` | 실제 날짜별 총자산과 번 돈, 가장 많이 번 날 |
| `/rank` | 판별 최고 총자산 순위 |
| `/dex` | 작물 도감(누적 수확 ★ 1개, ★★ 100개, ★★★ 1,000개)과 업적 10종 |
| `/prestige` | Lupin Bloom: 판을 끝내고 `floor(sqrt(최고 총자산 / 100K))` Fertility를 얻어 새로 시작. 기록·도감·보너스·규칙은 남습니다 (두 번 확인) |
| `/perks`, `/perks buy <id>` | Fertility로 영구 보너스: 성장 속도, 판매가, 부재 상한, 규칙 슬롯, 시작 골드·칸·농기구 |

## 조작

| 키 | 동작 |
|---|---|
| `Enter` / `Tab` | 실행 / 자동완성 |
| `↑` `↓` | 이전 명령 (자동완성 목록이 열려 있으면 목록 이동) |
| `Esc` | 자동완성 닫기 → 입력 지우기 |
| `PgUp` / `PgDn` | 이전 / 다음 구역 |
| `Shift+PgUp` / `Shift+PgDn` | 로그 스크롤 |
| `F2` 또는 `/hide` | 농장 접기 (로그와 입력창만 남기고, 안내 줄 오른쪽에 농장 요약 한 줄) |
| `Ctrl+Alt+H` (전역) | 보스키: 평범한 PowerShell 프롬프트만 보이게. 다시 누르면 복귀 (`/config bosskey`로 변경) |
| `Ctrl+C` 두 번 / `/exit` | 저장 후 종료 |
| `?` (입력이 비었을 때) | 단축키 목록 |
| 마우스 | `◀` `▶`·아래 점으로 구역 이동, 밭 클릭 시 좌표 입력(넓은 창의 다른 구역은 `f2.B3`처럼), 로그 휠 스크롤 |

화면 문구는 영어가 기본이고 `/config lang ko`로 한국어가 됩니다. 명령어는 항상 영어입니다.

## 저장

`%LOCALAPPDATA%\LupinFarm\`의 `save.json`(현재 판), `profile.json`(설정, 이전 명령, 기록, 보너스, 도감, 업적). 직전 버전은 `.bak`으로 남습니다. 명령 뒤, 30초마다, 종료할 때 저장합니다.

| 명령 | 내용 |
| --- | --- |
| `/save` | 저장 폴더, 마지막 저장, 내보낸 파일 |
| `/save export [파일\|폴더]` | 판과 기록·보너스·도감·설정을 JSON 파일 하나로. 기본은 저장 폴더의 `export\lupin-날짜-시각.json`이고, 이름만 적어도 `export` 폴더에 씁니다. USB 등 다른 곳은 절대 경로로 (`/save export D:\backup`) |
| `/save import <파일>` | 내보낸 파일 불러오기. 한 번 더 묻고, 지금 데이터는 먼저 `export\before-import-….json`으로 남깁니다. 내보낸 뒤 지난 시간은 꺼져 있던 시간으로 반영합니다 |

`/reset`은 `export` 폴더를 지우지 않습니다.

## 개발용

```powershell
.\bin\lupin.exe --frame en          # 예시 화면 한 장을 글자로 출력 (ko, cands, prompt, folded, first, market, inv, map, shop, status, auto, rule, offline, merge, records, daily, rank, prestige, bloom, perks, dex, crops, growing, gear, seeds, perkcands, gearcands, wide, save, preview, placelv, ime)
.\bin\tests.exe balance            # 밸런스 시뮬레이션: 플레이어 세 유형이 단계마다 닿은 게임 날짜 (build.bat test 뒤)
.\bin\lupin.exe --sprites           # 도트 일람
$env:LUPIN_HOME = "C:\temp\lupin"   # 저장 폴더 바꾸기
```

## 문서

| 문서 | 내용 |
| --- | --- |
| [`docs/1.architecture/wall-geop-lupin 기획·설계서.md`](docs/1.architecture/wall-geop-lupin%20기획·설계서.md) | 기준 문서: MVP 범위, 핵심 결정, 마일스톤 |
| [`docs/1.architecture/`](docs/1.architecture) | 01 요구사항, 02 도메인·데이터, 03 시스템, 04 화면 |
| [`docs/2.feature/`](docs/2.feature) | 마일스톤별 범위와 규칙 (M1~M5) |
| [`docs/3.specification/`](docs/3.specification) | 마일스톤별로 만든 것, 사용자 확인 항목 (M1~M5) |
| [`docs/4.issue/`](docs/4.issue) | 마일스톤별 개발 중 판단과 문제 (M1~M5) |
| [`docs/0.reference/`](docs/0.reference) | 아이디어, 장르·메커니즘·기술 조사 |
