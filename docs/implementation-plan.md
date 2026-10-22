# 実装と検証

更新日：2026-10-23（JST）。[仕様](spec.md)と[機能カタログ](feature-catalog.md)のすべてを完成させる作業一覧。D01〜D20の設計判断と全カタログ機能、Core / Integration Editor、Setup、Monitorを実装した。Stage ProjectでRuntime / Editor assemblyをコンパイルし、EditMode 88件と通常PlayMode 7件が通過した。MIDI control typeごとの値処理、XY→Vector2 Source、MIDI Clockの期限遅延時pulse欠落計測、Bar起点を守るGrid処理と時間軸変更後の予約破棄を追加した。ローカルGit URLからのUPM導入後も70件通過し、APC mini mk2のPad 1物理入力と緑色LED点灯を確認した。macOS universalのMono / IL2CPP、Windows x64 Mono Player buildも通過した。未確認の受け入れ項目を下表に残す。

## 実装作業

### 基盤・依存

- [x] [設計判断](design-notes.md)D01〜D03・D18と[導入仕様](packaging.md)：保存形式、namespace、配布単位、Runtime / Transportの依存境界を確定
- [x] 単一UPMフォルダ、package.json、機能別Runtime / Editor / Tests asmdef
- [x] R3導入手順、外部依存の固定一覧、Setup Windowを実装。ローカルGit URLからの新規導入を確認。公開HTTPS URL・release tagは未確認
- [x] 任意IntegrationのVersion Defines / Define Constraintsを設定。Stageの全依存あり、R3とUnity標準moduleのみ、Input System＋MIDI＋OSCを含む部分構成でimport・compileを確認。公開HTTPS URL・release tagは未確認
- [x] 必要パッケージの採用版とPlayer backendを記録。Unity 6000.3.8f1/macOS arm64 Editorで依存解決
- [x] Core / Integration / Editor / Testsのassembly分離。R3＋Unity標準module構成で任意Integrationなしのimport / compileを確認。Git URL導入は未完
- [x] 8 Route具体型、generic API、RouteContext、Runtime Root、共通Binding Scope
- [x] Value初期値、Trigger非Replay、最終Stream単一購読、最終値保持、Output初期化順
- [x] 共通FrameClock、Service初期化、Enable / Disable / Destroy、例外隔離と診断
- [x] 各Value / TriggerのShared Signal、Source / Output、Scene横断の循環検出

### Source・Modifier

- [x] カタログの基本・Stateful・Composite・型変換Sourceを全件実装
- [x] Float / Int / Bool / Vector / Color / Trigger Modifierを全件実装
- [x] D04〜D08：初期値・同時入力・Trigger合成・数値境界・時間処理・再Bindの意味を確定
- [x] Input System Source全件と共用ActionのEnable管理（D12）

### Transport

- [x] double Clock / epoch、BPM / Multiplier、Beat / Bar / 拍子、Phase連続性。10分相当の仮想ClockでBPM / Multiplier変更後のPositionを検証
- [x] Tap Tempo、Nudge / Reset、各Transport Source / Output。Tapのtimeoutと30 / 300 BPM境界を検証
- [x] 通常・三連Subdivision、Quantize SchedulerとTrigger Modifier。全10 Gridの境界、Bar origin、Seek後の予約破棄、1025予約時の容量とフレーム上限をEditModeで検証
- [x] DSP / Unscaled / External MIDI / External OSCのClock切替
- [x] MIDI Clock受信と送信、Running / SPP、外部同期状態の復元
- [x] D09〜D11：Grid単位、遅延・停止・Seek・外部切断・Clock精度の契約と合格目標を確定

### MIDI

- [x] MidiHub、Surface Instance、Profile、Logical Control Reference
- [x] Generic / APC mini mk2 Driver、物理InputとFeedbackの単一定義
- [x] Absolute / Relative / Velocity / Hold / Group Index / Press / Release Source
- [x] Bool / Index / Float / Color / FlashのLED Output
- [x] Base / State / Beat / Interaction / Alertと追加Layer、Priority合成
- [x] 差分送信、更新Queue集約、Port検出、複数機器、切断・再接続・Full Resync
- [ ] D13〜D14：Realtime Messageとnative backend、実機Protocol、縮退規則を継続検証。APC Pad 1の物理入力とRtMidi raw messageは確認済み

### Integration・Output

- [x] Unity Object / Transform、Debug / Signal Output
- [x] Animator / VFX Graph / Renderer・Material / Volume Output全件
- [x] Cinemachine / VideoPlayer / AudioSource / Generic Component Output全件
- [x] OSC全型の入出力と外部Transport同期Adapter
- [x] OSC Jack forkのType Tag取得API・固定commit・型棄却・4-float同期Adapter
- [x] Lasp Level / Band / Peak / SpectrumとAudio Trigger
- [x] D15〜D17・D20：UInt / Matrix / Textureの接続、資源所有権、IL2CPP accessor、Audio / OSC値の契約を確定

### Editor・運用

- [x] 型別Node検索・選択、Route要約、複製、Copy / Paste、並べ替え、Disable / DeleteのCore Inspector
- [x] Animator / VFX / Shader / Volume / Component Memberの候補選択UIとIL2CPP accessor生成
- [x] ProfileのControl候補とEditor MIDI Learn
- [x] Play前Validation、InspectorのValid / Warning / Error、Route名付き診断。広い不正構成の受け入れ試験は未完
- [x] Runtime / MIDI / Transport診断・Route値・Output status Monitor。中間値履歴・負荷計測は未完
- [x] D19：処理中の失敗隔離、ログ抑制、観測コストと合格目標を確定
- [x] 使用例・セットアップ手順を追加。対象OSとPlayer検証結果は下表の記録に従う

## 受け入れ条件

以下は実装完了判定の受け入れ仕様。Core / Transport / OSC / Editor Validation / APC ProfileのEditModeテストと、Runtime Lifecycle / APC実機送信のPlayModeテストを追加した。全条件の記録はまだ揃っていない。単体契約はEditMode、UnityライフサイクルはPlayMode、外部連携は実機とPlayerで確認する。

| ID | 対象 | 検証する操作と期待結果 |
| --- | --- | --- |
| A01 | 初期値 | 各Value型へ後から購読し、false / 0を含む現在値が1回届く。OutputもInitialize後に初期値を受ける |
| A02 | Trigger | Bind直後と再Bind直後に発火せず、実入力1回につき仕様どおりの回数だけ発火 |
| A03 | 単一評価 | 乱数・Step・EnvelopeのSourceに3 Outputを接続。1入力につき状態遷移1回、全Outputが同じ最終値を受ける |
| A04 | Lifecycle | Enable / Disableを反復、Destroy、Play再入場。二重購読・残留Frame更新・Timer・Port資源がなく、状態は確定した復帰契約に従う |
| A05 | Signal | MIDIとOSCから同じSignalを更新し、映像とLEDが同じ状態を見る。循環参照はPlay前に検出 |
| A06 | 隔離 | Source不備、Null Target、処理中例外、切断を注入。該当Route名で診断され、正常Routeが継続 |
| A07 | Composite | 初期化順、同フレーム更新、MostRecent、All Trigger、Select非選択枝をD05 / D06に照合 |
| A08 | 演算 | 0除算、負のModulo、Index範囲外、逆Range、NaN、ゼロVectorをD07へ照合。無効設定はValidationで報告 |
| A09 | 時間処理 | 低FPS・一時停止・入力停止下でSmooth / Envelope / Delayを検証。FrameClockと選択時間単位に従い、破棄後は出力しない |
| A10 | Transport連続性 | 仮想Clockで長時間進め、BPMと倍率を途中変更。Beat Positionが連続し、Beat / Bar / Phase / Subdivisionが同一時刻に整合 |
| A11 | Grid | 拍子変更、三連、境界上の入力、大きなフレーム遅延、複数予約、Stop / SeekをD09 / D10に照合 |
| A12 | Tap | 安定Tap、外れ値、タイムアウト、上下限を入力。選んだ推定規則に従う |
| A13 | 外部Clock | Timestamp付きMIDI / OSC入力でBPMとPhaseを別々に検証。Start / Continue / Stop / SPP、喪失・再取得が契約に従う |
| A14 | MIDI Clock送信 | D11のBPM・10分負荷条件で送信誤差p99≤2ms・最大≤10ms・欠落0・累積期限ずれ≤1msを検証。外部受信機の実測と送信呼出しの結果を分けて記録 |
| A15 | MIDI入力 | 各Control Type、Note On velocity=0相当のRelease、Relative Encoder各形式、同名実機2台を検証し、Surfaceを取り違えない |
| A16 | LED状態 | Toggle / IndexをMIDI以外から変更しても映像とLEDが一致。Interaction Flash終了で下位State Layerへ戻る |
| A17 | LED送信・切断保持 | 静止状態では再送なし。同フレームの更新がControl単位で集約。Fader=0.7、Toggle=true、Hold=trueの状態でUSBを抜いても値が変わらず、再接続で最新DesiredをFull Resync。実入力後だけValue更新し、Panic中は再接続でもOFFを維持 |
| A18 | Input System | 2 Routeが同じActionを使い、一方をDisableしても他方を維持。既存PlayerInputとの所有権も確認 |
| A19 | Unity Output | カタログ全Outputを対応型・不正型・対象欠落で検証。MPBの他Propertyと共存、Asset / Volume変更の所有権、Video準備待ちを確認 |
| A20 | OSC / Audio | 型・チャンネル対応、受信thread、Queue、Hz指定、値の単位、Hysteresis / Cooldownを検証。無入力時の現在値も確認 |
| A21 | Inspector保存 | Route / Node複製・Copy / Paste・Undo / Redo・保存 / 再読込後に型と参照が保たれ、意図せず実行状態を共有しない |
| A22 | Validation | 原文第53章の不正構成をすべて作り、Editor診断が該当Routeを指す |
| A23 | Monitor | Raw→中間値→Final→Output、論理MIDI / Raw MIDI、Tap履歴・Clock、処理件数を照合。無効化時の計測コストも測定 |
| A24 | 単一UPM・分離とPlayer | MondmadeVJ0をGit URL1つで導入し、R3のみ・一部外部依存あり・全依存ありの各構成でimport・Playerビルド。無効Integrationの欠落参照エラーがないこと、各対象OSでnative MIDI・AOT accessor・strippingを検証 |

## 現時点の検証結果

| 対象 | 結果 |
| --- | --- |
| Unity Editor | Unity 6000.3.8f1 macOS arm64 Stage Projectで全Runtime / Editor assemblyをコンパイル |
| EditMode | Core契約、Transport、OSC Adapter、Editor Validation、APC Profile、MIDI Type Picker、Input Action lease、MIDI Clock scheduler、時間Modifier、Quantize / Subdivisionの全Grid境界・Seek / Clock再取得・overload境界を含む88 testsが88/88通過 |
| A06 Route隔離 | Source.Buildで例外を投げるRouteだけがFaultedとなり、Route名付き診断を残し、同じRuntime上の正常Routeは初期値を出力してActiveを維持 |
| A03 単一評価 | StepとSeconds Envelopeを3 Outputへ接続し、各Trigger SourceのBuild 1回、全Outputで同一の状態遷移と値列を確認 |
| A09 時間処理 | SmoothDampはPause中もUnscaled Clockで入力停止後に追従し、購読破棄後のFrameClock更新を止める。Seconds EnvelopeはPause中のUnscaled時間と大きな1フレームによるAttack / Hold / Release通過、破棄後の停止を確認。Delay / Debounce / Beat Delayの既存試験と合わせEditModeで検証 |
| A11 Grid負荷 | 1025件を同一Beat境界へ予約し、大きく遅れた1フレームで256件だけ発火、期限超過768件とQueue超過1件をそれぞれ診断することを確認 |
| A16 LED状態 | 外部Shared TriggerでToggleBoolを変えた時にState LEDが更新され、Interaction Flash終了後に最新State Layerへ戻ることをPlayModeで確認 |
| A22 Editor Validation | Output未設定、Null target、重複Route ID、逆Clamp rangeをPlay前にRoute名付きで報告する試験を追加。Animator / VFX / Shader / MIDI等の参照・型検証を含む全原文条件の試験は未完 |
| PlayMode | lifecycle、MIDI Surfaceの分離、Note On velocity=0 Release、3形式のRelative Encoder、Button / Velocity / Fader / Knob / Pitch Bend / Aftertouch、XY→Vector2、LED Layer復帰の7 testsが7/7通過。明示指定の実機試験は別枠 |
| APC mini mk2 | Input / Outputを機器名で検出し、両portをOpen。実機の左上Pad 1からNote 56を受信し、物理入力probe 1/1通過。LEDのON（0x96, 56, 21）/OFF送信payloadを検証。Pad 1の緑点灯も目視確認 |
| PlayMode合計 | 通常runは7/7通過。Explicit物理入力とLED表示試験はそれぞれ別途明示実行して1/1通過。追加のUSB再接続probeはテストを追加したが、通常runでSkippedし実機確認待ち |
| macOS Player | Unity 6000.3.8f1、universal x86_64 + arm64でMono2xとIL2CPP build成功、各errors=0 / Unity集計warnings=2。IL2CPP linkerはbaselibの最低macOS版がlink targetより新しい旨を出力し、Cloud Diagnostics symbol uploadは認証情報がなく失敗 |
| Windows Player | Windows Build Support (Mono)を導入し、Unity 6000.3.8f1からWindows x64 Mono build成功、errors=0。20 warningsはWindows非対応URP shader variantsのimport診断。Hubの同Editor向けmodule一覧にWindows IL2CPPはなく、IL2CPP buildは未実施 |
| R3最小構成 | Unity標準moduleを明示し、Input System / URP / MIDI / OSC / Lasp / Cinemachine / VFX GraphなしでUPM local packageをimport。Core / Setup / Animator / Transport / Videoがcompileし、欠落した任意Integrationの参照エラーなし |
| 部分構成 | UPM local packageにInput System＋MIDI＋OSCを追加し、Lasp / Cinemachine / URP / VFX Graphなしでimport・compile。MIDI / OSC assemblyが有効、未導入Integrationのassemblyは無効 |
| 未実施 | 公開HTTPS URL / release tagからの導入、部分構成ごとのPlayer build、Windows IL2CPP / Windows実機実行、Player上のOSC / 全Integration、MIDI Clock 10分精度、USB切断・再接続 |

上記は検証した環境に限る。Acceptance A01〜A24をすべて通過した記録が揃うまでは全体完了としない。

## 動作例として揃えるRoute

- APC Fader → Remap → Smooth → VFX Float
- APC Press → ToggleBoolSource → VFX Bool ＋ MIDI Bool LED
- Button Group → Camera Index → Cinemachine ＋ Indexed LED
- Beat Phase → WaveShape / Curve → Material / VFX
- Beat Trigger → Envelope（Beats）→ VFX Emission
- Press → Quantize → Camera / Animator / VFX Event
- Lasp Band → AttackRelease → Material Float
- MIDI / OSC → Shared Int Signal → Camera ＋ LED
- Tap Button → Transport Tap、Button Group → IndexToFloat `[0.5, 1, 2]` → BPM Multiplier

全カタログ項目、Subsystemチェックリスト、受け入れ条件の検証記録を揃えて完成とする。外部パッケージ導入済み・Editorでコンパイル済みというだけでは機能完成と数えない。
