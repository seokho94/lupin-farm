# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## 프로젝트 현황

wall-geop-lupin(월급루팡): 업무 시간에 들키지 않고 할 수 있는 개인용 농사 타이쿤 **Lupin Farm**. 사용자는 나 1명, Windows 11 회사 PC, Windows Terminal의 PowerShell 탭에서 실행.

**M1(기반·농사), M2(경제), M3(자동화) 완료**(사용자 확인 2026-11-26~27), **M4(기록·프레스티지) 구현 완료**(2026-11-27, 사용자 확인 전: `docs/3.specification/M4_기록·프레스티지 개발 명세.md` 9장 U1~U10). M4: 총자산, 체크포인트(Day 7 × 2^k, 그날이 끝나는 순간)·일별·판별 기록 `/records [daily]` `/rank`, 프레스티지 `/prestige`(Fertility, Lupin Bloom 장면, 규칙 이어가기), 영구 보너스 7종 `/perks [buy <id>]`, 도감·업적 10종 `/dex`, 작물별 심는 시기 `/crops`와 씨앗 후보의 계절, 작물별 남은 시간 `/growing`(스프링클러 칸은 물이 끊기지 않는 것으로 계산), 농장 그림의 스프링클러 레벨 숫자와 범위 파란 테두리(Lv3는 3줄 구역이라 최대 14칸), 농기구·장비 설명 `/help <이름>`(사용자 요청). 다음은 M5(후보, 범위는 사용자와 정한다). 스프링클러는 빈 칸을 차지하는 현재 방식 유지(사용자 결정 2026-11-27).

- 기준 문서: `docs/1.architecture/wall-geop-lupin 기획·설계서.md`(MVP, 핵심 결정 D1~D14, 마일스톤 M1~M5)와 같은 폴더 01~04(요구사항 ID `FR-XXX-01`, 도메인·데이터 초기값, 시스템 설계, 화면 설계). 마일스톤마다 `docs/2.feature/M{n}_… 기능 정의서.md` 작성 후 구현, 결과는 `docs/3.specification/M{n}_… 개발 명세.md`, 이슈는 `docs/4.issue/M{n}_개발 이슈.md`. 문서 규칙은 `docs/README.md`.
- 컨셉: Claude Code 같은 화면(위 도트 농장 고정 14줄, 아래 로그 + 둥근 테두리 입력창), 영어 슬래시 명령어, 농사만. 판매 → 땅·농기구·작물 투자 → 장비형 + 규칙형(`when … do …`) 자동화(M3). 실제 시간 배속(기본 실제 60초 = 게임 1시간), 꺼져 있던 시간은 상한까지(기본 2시간), 무한 성장 + 기록·랭킹 + 프레스티지 Lupin Bloom(M4). 문구는 영어 기본, `/config lang ko`.
- 사전 조사·결정 이력: `docs/0.reference/`(`아이디어 정리.md` 6절 정한 것, `명령어 농장 컨셉 분석.md`). 방향 A(바탕화면 생태계), B(PowerShell 도트 생활 시뮬)는 보류.
- 동물의 숲·스타듀 밸리는 장르·메커니즘 참고용, Claude Code는 화면 배치·분위기 참고용. 이름, 로고, 캐릭터, 그래픽, 음악은 쓰지 않고 오리지널로 만든다.
- 작업·수정한 내용은 크기와 상관없이 항상 문서로 남기고, 같은 작업 안에서 문서를 최신으로 맞춘다 (사용자 요청 2026-11-27). 진행 중 마일스톤의 기능 정의서(사용자 요청은 날짜를 붙인 K 항목), 개발 명세(바뀐 파일·테스트 수·화면·사용자 확인 항목), 개발 이슈(현상 → 원인 → 조치 → 반영), 영향 받는 `docs/1.architecture` 01~04, `README.md`, 이 파일을 함께 고친다. 디자인을 바꾸거나 되돌리면 옛 표현을 찾아 남지 않게 한다.
- 상위 폴더의 `CLAUDE.md`는 이 프로젝트에는 적용하지 않는다.

## 개발 명령

루트에서 실행한다. 외부 설치 없음(Windows 기본 .NET Framework 4.8.1, `csc.exe` C# 5). Bash 도구에서는 `cmd //c build.bat`이 안 되므로 PowerShell 도구에서 `cmd /c ".\build.bat"`로 실행한다.

| 명령 | 내용 |
| --- | --- |
| `build.bat` | `bin\lupin.exe` (`/recurse:src\*.cs`, 참조 System, System.Core, System.Web.Extensions) |
| `build.bat test` | `src\Core`, `src\Data`, `src\Storage`, `tests`를 `bin\tests.exe`로 빌드 후 실행. `public static void Test*()`를 모두 돈다 |
| `bin\lupin.exe --frame [en\|ko] [cands\|prompt\|folded\|first\|market\|inv\|map\|shop\|status\|auto\|rule\|offline\|merge\|records\|daily\|rank\|prestige\|bloom\|perks\|dex\|crops\|growing\|gear\|seeds\|perkcands\|gearcands]` | 예시 상태 화면을 글자로 출력. 콘솔 없이 동작하므로 배치 확인에 쓴다 (PowerShell에서 `[Console]::OutputEncoding = [Text.Encoding]::UTF8` 먼저) |
| `bin\lupin.exe --sprites` | 도트 일람 (콘솔 필요) |
| `LUPIN_HOME` 환경 변수 | 저장 폴더 변경. 테스트 실행 시 실제 세이브(`%LOCALAPPDATA%\LupinFarm`)를 건드리지 않도록 쓴다 |

- 사용자가 게임을 실행 중이면 `bin\lupin.exe`가 잠겨 빌드가 `CS0016`으로 실패한다. 게임을 끄지 말고 `bin\lupin.exe`를 `bin\lupin.old.exe`로 이름만 바꾼 뒤 빌드한다 (실행 중인 exe도 이름은 바뀐다, M2 이슈 1).
- 도트 확인은 터미널 창을 띄우지 말고 PowerShell에서 `[Reflection.Assembly]::Load([IO.File]::ReadAllBytes("bin\lupin.exe"))`로 불러 `LupinFarm.Data.Sprites`를 `System.Drawing`으로 PNG로 그린다 (M2 이슈 7).
- 세이브를 불러오는 확인 스크립트(`SaveStore`)는 Windows PowerShell 5.1(`powershell.exe -NoProfile -File …`)로 돌린다. PowerShell 7에는 `System.Web.Extensions`가 없어 불러오기가 `null`이 된다 (M4 이슈 7). 사용자 세이브는 복사본으로만 확인한다.
- 실제 터미널에서 띄워 볼 때 `conhost.exe`는 기본 터미널 설정 때문에 Windows Terminal로 넘어간다. 새 창을 띄우면 사용자 화면에 창이 뜨므로 꼭 필요할 때만 하고, 끝나면 정상 종료(Ctrl+C 두 번)로 창이 닫히게 한다. 콘솔 입력 주입 도구는 회사 보안 프로그램이 막으므로 쓰지 않는다 (M1 이슈 2).

## 아키텍처

C# 5 콘솔 앱 하나. `namespace LupinFarm.{Core, Data, Storage, Ui}`.

- `src/Core`: 게임 로직. **콘솔·파일·현재 시각을 쓰지 않는다**(시간은 `GameState.GameMinute`, 실제 시각은 Ui가 넘긴다). 결과는 화면 문구가 아니라 `LogEntry`(`Msg` = 문구 키 + 값, `Dur` = 실제 초, `Joined` = " · " 묶음). `Commands.Execute(ctx, line)` → `CommandResult`(로그, `PromptRequest` 선택지(콜백), `UiAction`, 보는 구역). `Engine.Advance(state, minutes)`: 06:00과 규칙 검사 시각에서 구간을 나눠 성장 → 시세 → 스프링클러 → 계절 → 규칙(`Rules.Run`). 바뀐 것이 없으면 다음 다 자람 뒤의 검사까지 건너뛴다(결과는 같음, `TestBigStepEqualsSmallSteps`로 지킨다). 물 마름은 경계 이벤트 뒤에 판정. `AdvanceReport`(구역별 Ready/Dried, Days, SeasonStarts, 규칙 실행 `Runs`). 꺼져 있던 시간도 같은 함수라 자동화가 일한다. `Farming`: 손 명령과 규칙이 같이 쓰는 수확·심기·판매·스프링클러(온실이면 계절 무시). 심을 때 칸의 필요 성장 분(`PlotState.Need`, Rich Soil)을 정하고, 수확 수(`Harvested`)·판매 수입(`Earned`)을 쌓는다. `Records`: 총자산, 하루 끝(`DayEnd`: 판 최고·체크포인트, 시세 갱신 전), `Sync(state, profile, 오늘)`로 판 기록·일별·도감·업적을 프로필에 옮기고 이벤트를 돌려준다(엔진은 프로필을 모른다). `Prestige`: Fertility, `Bloom`(새 판, 규칙 이어가기), 보너스 사기. 보너스 효과는 `GameState.Perks`(프로필 사본)로 엔진·시세·규칙에 붙는다. `Rules`: 문장 해석(틀린 위치)·캐시·슬롯(`Active`)·실행·문구. 규칙 로그는 화면(`App.AddRun`)이 바로 앞 같은 규칙 묶음과 합친다. `Market`: 단가 = 기준가 × 계수 × 계절 배수, 판매는 `Quote`/`Sell`(등비수열 합, 하한 0.5), 난수 `Rng`(xorshift32, 상태는 `GameState.Rng`). 표 모양 로그는 `Joined.Row(new Pad(…, 폭), …)`로 만들면 화면이 한글 2칸 폭으로 맞춘다. `Engine.Stats(state, field|-1)`: 패널·스피너용 통계. `RangeParser`: `[fN.](A1|A1:C3|A|all|ready)`, 필요한 농기구 레벨(한 칸 1, 한 줄 2, 그 밖 3). 시간 단위는 정수 게임 분(0 = Day 1 봄 06:00), `GameClock`.
- `src/Data`: `Content.cs`(작물 10종, 상수, 비용 공식 — 밸런스는 여기만), `Sprites.cs`(8×8 문자 그리드 + 팔레트, `.` 투명, 오른쪽·아래 끝은 풀), `Strings.cs`(En/Ko 표, 키 같아야 함).
- `src/Storage/SaveStore.cs`: `JavaScriptSerializer`(키 PascalCase), `.tmp` → `File.Replace` + `.bak`, 손상 시 `.bak`. `save.json`=`GameState`, `profile.json`=`Profile`(설정·이전 명령, M4에서 기록 추가).
- `src/Ui`: `App`(메인 루프·입력·로그 변환·화면 배치, 접은 화면 요약 줄, 기록 동기화 `Observe`, 프레스티지 장면, `Preview`는 `--frame`용), `FarmView`(14줄 농장 + 정보 패널(장비·규칙 줄) + 마우스 판정 `Hits`), `ScreenBuffer`(셀 버퍼, 차분 VT 출력, 2칸 문자 이어짐 = 빈 문자열), `LogView`(문단 → 폭 줄바꿈, 스크롤), `InputBox`, `Completion`, `Localizer`(Msg → 문구), `Terminal`(콘솔 모드, `WriteConsoleW`, 대체 화면), `InputReader`(`ReadConsoleInputW`), `BossKey`(RegisterHotKey 스레드), `Native`(P/Invoke).

### 코드 규칙

- **C# 5만** 쓴다: 문자열 보간 `$""`, `?.`, `=>` 멤버, `nameof`, 자동 속성 초기화, `out var`, 튜플, 패턴 매칭 금지. 콘솔 제어 문자는 `"\u001b"`(`Terminal.Esc`)로 쓴다(`\x1b`는 뒤 글자까지 먹을 수 있다).
- 화면 문구는 코드에 직접 쓰지 않고 `Strings.cs`에 en·ko 둘 다 넣는다. 형식 인자 수가 같아야 한다(`StringsTests`). `help.<명령>`은 `/help <명령>` 설명 키라 다른 용도로 쓰지 않는다. 같은 키를 인자 수가 다른 두 곳에 쓰지 않는다 (M4 이슈 10). 한국어에서 이름 값 뒤에 조사(이/가, 은/는)를 붙이지 않는 문장으로 쓴다 (M3 이슈 3).
- 수치·비용은 `Content.cs`에 둔다. 바꾸면 `docs/1.architecture/02_…`도 고친다.
- 새 상태 필드는 `Normalize()`에서 기본값을 채운다(이전 세이브 호환).
- 사용자에게 보이는 문구는 영어 기본, 한국어 번역 동반. 문서는 한국어.
- 회사 PC 감시·보안 프로그램 회피 기능은 만들지 않는다 (범위 밖).
