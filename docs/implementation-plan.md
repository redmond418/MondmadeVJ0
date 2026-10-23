# 実装と検証

更新日：2026-10-23（JST）。[仕様](spec.md)と[機能カタログ](feature-catalog.md)を実装・検証する作業一覧。D01〜D20の設計判断、機能カタログ、Core / Integration Editor、Setup、Monitorを実装した。Stage Projectとクリーンな消費側Projectで機能契約を検証し、OSC loopback / strict Type Tag / main-thread queue、Lasp dBFS / spectrum / trigger、Video prepare・Fault、Material / Volume lease、書込み競合診断も契約試験に含めた。最新差分の部分依存消費側ではEditMode 135 passed / 2 optional skipped、通常PlayMode 7 passed / 6 Explicit skipped、QuickStart APC Profileアセット照合、macOS universal StandaloneOSX MonoおよびWindows x64 Mono Player buildが通過した。APC mini mk2実機Pad 1の緑点灯はユーザーが目視確認した。A14規定のStage OSXEditor / Apple M1 / macOS 26.1.0実機負荷を120 / 174 / 240 BPM各600秒で完走したが、全体は未合格。fpsは59.996 / 59.994 / 59.991、Clock欠落は0 / 3 / 10、p99は各0.001ms、最大遅延は5.802 / 12.565 / 10.314ms、累積期限ずれは5.7989 / 12.5641 / 10.3135ms。60fps・1000 Route・LED 10Hz（約800 control states/s）は維持した。外部受信機実測、USB再接続、Fader / Track Button物理入力、公開Git URL、Windows IL2CPPなど未確認の受入条件も残るため、A01〜A24の記録が揃うまで完成扱いにしない。

## 実装作業

### 基盤・依存

- [x] [設計判断](design-notes.md)D01〜D03・D18と[導入仕様](packaging.md)：保存形式、namespace、配布単位、Runtime / Transportの依存境界を確定
- [x] 単一UPMフォルダ、package.json、機能別Runtime / Editor / Tests asmdef
- [x] R3 1.3.0とIL2CPP linkに必要なSystem.Runtime.CompilerServices.Unsafe 6.0.0のSetup導入、外部依存固定一覧を実装。NuGetForUnity経由の導入を確認。ローカルGit URL導入は確認、公開HTTPS URL・release tagは未確認
- [x] 任意IntegrationのVersion Defines / Define Constraintsを設定。全依存あり、Core必須依存だけ、Input System＋MIDI＋OSCの部分構成をGit UPM URLでimport・compile。部分構成はEditMode 123 passed / 2 optional Integration tests skipped、macOS universal Mono Player build成功。公開HTTPS URL・release tagは未確認
- [x] 必要パッケージの採用版とPlayer backendを記録。Unity 6000.3.8f1/macOS arm64 Editorで依存解決
- [x] Core / Integration / Editor / Testsのassembly分離。任意IntegrationなしでCoreがPlayer build可能なことも検証。完全SHAを指定したローカルGit UPM URLから全依存構成とCore必須依存だけの構成へpackageを導入し、macOS universal Mono Player build成功。全依存ProjectでEditMode 169件 / PlayMode 7件、CoreのみProjectでEditMode 89 passed / 3 integration skipped。公開HTTPS URLとrelease tagは未確認
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
- [x] Play前Validation、InspectorのValid / Warning / Error、Route名付き診断。原文第53章のSource / Output / Target / VFX / Animator / Shader / MIDI Control / Stable ID / Composite / Signal cycle / RangeをStageで検証
- [x] Runtime MonitorにSource Raw→各Modifier→Final→Output履歴、MIDI論理 / Raw / LED出力履歴、Transport Tap / Clock履歴を実装。Runtimeあたり各4096件の履歴、詳細追跡は最大32 Route。Performance MonitorはRoute処理 / Frame callback時間、Route / Source / Modifier / MIDI / OSC件数・毎秒率を表示。Releaseはcaptureを既定無効にする
- [x] D19：処理中の失敗隔離、ログ抑制、観測コストと合格目標を確定
- [x] 使用例・セットアップ手順を追加。対象OSとPlayer検証結果は下表の記録に従う

## 受け入れ条件

以下は実装完了判定の受け入れ仕様。Core / Transport / OSC / Editor Validation / APC Profile / MonitorのEditModeテストと、Runtime Lifecycle / MIDI SurfaceのPlayModeテストを追加した。全条件の記録はまだ揃っていない。単体契約はEditMode、UnityライフサイクルはPlayMode、外部連携は実機とPlayerで確認する。

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
| A19 | Unity Output | カタログ全Outputを対応型・不正型・対象欠落で検証。MPBの他Property共存、Material / Volume clone leaseと復元、Volume Override状態、Renderer / Transform / Volume書込競合、Video Value / Trigger保留・prepare失敗・10秒timeoutを確認 |
| A20 | OSC / Audio | 型・チャンネル対応、受信thread、Queue、Hz指定、値の単位、Hysteresis / Cooldownを検証。無入力時の現在値も確認 |
| A21 | Inspector保存 | Route / Node複製・Copy / Paste・Undo / Redo・保存 / 再読込後に型と参照が保たれ、意図せず実行状態を共有しない |
| A22 | Validation | 原文第53章の不正構成をすべて作り、Editor診断が該当Routeを指す |
| A23 | Monitor | Raw→中間値→Final→Output、論理MIDI / Raw MIDI、Tap履歴・Clock、処理件数を照合。無効化時の計測コストも測定 |
| A24 | 単一UPM・分離とPlayer | MondmadeVJ0をGit URL1つで導入し、Core必須依存のみ・一部外部依存あり・全依存ありの各構成でimport・Playerビルド。無効Integrationの欠落参照エラーがないこと、各対象OSでnative MIDI・AOT accessor・strippingを検証 |

## 現時点の検証結果

| 対象 | 結果 |
| --- | --- |
| Unity Editor | Unity 6000.3.8f1 macOS arm64 Stage Projectで全Runtime / Editor assemblyをコンパイル |
| EditMode | Core契約、Transport、OSC Adapter、Editor Validationの原文53章全項目・VFX / Animator / Shader / MIDI参照・Component member型、APC Profile、MIDI Type Picker、Input Action lease、MIDI Clock scheduler、共有MIDI出力workerの優先queue / LED coalescing / Clock metric / schedule世代破棄、時間Modifier、Quantize / Subdivision境界、Monitor履歴と計測コスト、RouteHost複製・Copy / Paste・Undo / Redo・Scene保存 / 再読込、Material / Volume clone leaseとTransform / Genericを含むOutput競合、Video prepare共有・Fault、Lasp値・周波数bin・Trigger状態、OSC型・queue・UDP loopbackを含む169 testsが169/169通過 |
| A06 Route隔離 | Source.Buildで例外を投げるRouteだけがFaultedとなり、Route名付き診断を残し、同じRuntime上の正常Routeは初期値を出力してActiveを維持 |
| A08 数値境界 | Negative Modulo / Lookup上下端 / 0除算 / NaN入力時の最終有限値保持を確認し、NaN設定をPlay前に報告 |
| A03 単一評価 | StepとSeconds Envelopeを3 Outputへ接続し、各Trigger SourceのBuild 1回、全Outputで同一の状態遷移と値列を確認 |
| A09 時間処理 | SmoothDampはPause中もUnscaled Clockで入力停止後に追従し、購読破棄後のFrameClock更新を止める。Seconds EnvelopeはPause中のUnscaled時間と大きな1フレームによるAttack / Hold / Release通過、破棄後の停止を確認。Delay / Debounce / Beat Delayの既存試験と合わせEditModeで検証 |
| A11 Grid負荷 | 1025件を同一Beat境界へ予約し、大きく遅れた1フレームで256件だけ発火、期限超過768件とQueue超過1件をそれぞれ診断することを確認 |
| A16 LED状態 | 外部Shared TriggerでToggleBoolを変えた時にState LEDが更新され、Interaction Flash終了後に最新State Layerへ戻ることをPlayModeで確認 |
| A21 Inspector保存 | Route複製でRoute / 子Node IDが再生成されること、Copy / Paste後の値の保持と独立性、Undo / Redo、managed-referenceのRoute型 / 値 / IDと同Scene Renderer参照のScene保存 / 再読込をEditModeで確認。複製処理が基底クラスのSerializeReferenceを走査せず子Node IDを共有していた不具合を修正 |
| A22 Editor Validation | 原文第53章の未設定Source / Output、Null target、VFX property不足、Animator parameter不足・型違い、Shader property不足・型違い、MIDI Control不足・Control Type違い、Route / Node Stable ID重複、Composite接続不良、Signal循環、逆RangeをStageで検証。Generic Component member不足・型違い、Renderer / Material / Volume / Transformの複数Writer競合も検出。全169 EditMode testが169/169通過 |
| A23 Monitor | Source Raw / Modifier / Final / Outputを選択Routeごとに表示し、MIDI raw / logical / LED queuedとTap / MIDI Clock履歴、Transport状態、Route・Frame callback時間とMIDI / OSC毎秒件数を表示。各履歴は4096件、詳細Route選択上限32、Releaseではcapture既定OFF。Stageの測定でcapture OFFのtrace経路は直接Subject 10.50ns/event・0 byteに対しinstrumented 34.16ns/event・0 byte、追加23.65ns/event（20万event、macOS arm64 Editor）。Route履歴の上限・無効化・MIDI / Transportデータ・Source→Modifier→Output履歴もEditModeで検証 |
| A20 OSC / Audio | LASP値とdBFS正規化、FFT sampleRate / sizeからのBand Hz集計、Nyquist clamp、Threshold / Peak Trigger初期化・Hysteresis・80ms CooldownをEditMode 14/14で検証。OSC型・初期 / 最新値、worker→8192 queue→main-thread配送、drop / malformed計数、実UDP loopback input / outputを6/6で検証。さらにmacOS universal Mono / IL2CPP Playerの両方を別プロセスのUDP OSC相手と接続し、各Playerが0.375fを受信してmain-threadで最新値を公開した後、0.625fを相手へ返す双方向通信を確認した。実Audio device captureは未確認 |
| PlayMode | lifecycle、MIDI Surfaceの分離、Note On velocity=0 Release、3形式のRelative Encoder、Button / Velocity / Fader / Knob / Pitch Bend / Aftertouch、XY→Vector2、LED Layer復帰の7 testsが7/7通過。明示指定の実機試験は別枠 |
| APC mini mk2 | Input / Outputを機器名で検出。実機の左上Pad 1からNote 56を受信し、物理入力probe 1/1通過。AKAI Bulk SysExでPad 1を緑点灯し、ユーザーが目視確認。Pad 1 LED probeとClock 24 pulse＋LED queueの共有worker結合probeも各1/1通過。QuickStart assetの全64 Pad input / output note layoutをUnity AssetDatabase testで照合。Fader / Track Button実機probeは7回実施。5回目は90.115秒、6回目は90.596秒、7回目は90.121秒でいずれもRaw MIDI 0件となり、Fader 1 CC 48とTrack Button 1の押下・Releaseは型付き入力未確認。6・7回目は物理操作を依頼したが実施有無は確認できていない。RtMidi input / outputはport 0 `APC mini mk2 Control`、port 1 `APC mini mk2 Notes`と列挙され、本probeはport 0を選択。公式[Communications Protocol](https://cdn.inmusicbrands.com/akai/attachments/APC%20mini%20mk2%20-%20Communication%20Protocol%20-%20v1.0.pdf)はFader 1をCC 0x30 / channel 0 / Port 0、Track Button 1をNote 0x64 / channel 0 / Port 0と定義し、Profileの割当は一致する。 |
| A14 MIDI Clock | 現行40ms先行queue・8 pulse capacity・3ms spin・期限直前までの動的worker待機と、1周期未満の遅延pulseを送信workerへ渡す修正を実装。61秒全LED・1000 Route smokeではBPM 120 / 174 / 240の各fps 59.993 / 59.984 / 59.997、LED 9.999 / 9.997 / 10.000Hz、各約48,784 LED control states（約800/s）、Clock欠落 / output drop 0、p99 0.001ms。累積期限ずれ・最大遅延は各0.777 / 7.488 / 2.655ms、0.779 / 7.489 / 2.655ms。174 / 240 BPMが1ms累積期限ずれ目標を超えてA14未合格。174 BPMの2ms spin比較はfps 59.985、欠落 / output drop 0、p99 0.001ms、最大遅延8.121ms、累積期限ずれ7.013msで不採用。1ms spinの全速度61秒比較ではfps 59.993 / 59.986 / 59.984、p99 0.224 / 0.229 / 0.246ms、最大遅延0.941 / 6.257 / 3.294ms、累積ずれ0.686 / 6.129 / 2.762ms、240 BPMにClock欠落2回が出たため不採用。0.5ms spinの174 BPM試行でも累積ずれ2.495msで不採用。174 BPMで生成workerだけをAboveNormal / UserInitiatedへ下げる比較は累積ずれ4.359ms、Normal / Defaultでは2.574ms、BelowNormal / Utilityでは7.476msで未達。生成workerをNormal / UserInitiatedにすると2.188ms、Normal / Utilityにすると2.050ms、後者を1ms spinと組み合わせても2.077msで、すべて1ms目標未達のため不採用。8ms spin比較では240 BPMに2 Clock欠落、5ms / 4ms候補ではFPS・最大frame間隔悪化と欠落 / seek再同期が出たため不採用。Stage OSXEditor / Apple M1 / macOS 26.1.0の実機で規定の600秒×3速度を実施。120 BPM: fps 59.996、Route 59,996 writes/s、LED 10.000Hz / 479,984 controls、Clock 28,801対約28,800、missed 0 / dropped 0、p99 0.0010ms、最大5.8020ms、累積ずれ5.7989ms。174 BPM: fps 59.994、Route 59,994/s、LED 10.000Hz / 479,984 controls、Clock 41,756対約41,760、missed 3 / dropped 0、p99 0.0010ms、最大12.5650ms、累積ずれ12.5641ms。240 BPM: fps 59.991、Route 59,991/s、LED 10.000Hz / 479,984 controls、Clock 57,589対約57,600、missed 10 / dropped 0、p99 0.0010ms、最大10.3140ms、累積ずれ10.3135ms。各相とも累積ずれ1ms超過、174 / 240 BPMは欠落、174 / 240 BPMは最大遅延10ms超過のため不合格。実行XML: /private/tmp/MondmadeVJ0-stage/a14-full-acceptance.xml。macOS固有のMach期限待機・thread time-constraint policyを加えた61秒全LED短縮比較も3速度で実施したが、policy＋0.5ms spinはClock欠落0 / 0 / 1、最大遅延2.583 / 3.278 / 9.431ms、累積ずれ2.579 / 3.277 / 9.430ms。policyなしMach wait＋0.5ms spinは欠落0 / 0 / 3、最大3.726 / 7.834 / 4.895ms、ずれ3.722 / 7.833 / 4.894msで、いずれも未達のため実装を戻した。短縮比較XML: /private/tmp/MondmadeVJ0-stage/a14-mach-policy-smoke.xml、a14-mach-spin-smoke.xml、a14-mach-wait-smoke.xml。さらにStandaloneOSX Playerでも同じ61秒短縮probeを実行。120 / 174 / 240 BPMのfpsは59.377 / 59.559 / 59.575、最大frame間隔92.310 / 31.294 / 24.728ms、Route writes/s 59,377 / 59,559 / 59,575、LED 10Hz・約800 states/s、Clock欠落0 / 0 / 0、p99 0.096 / 0.031 / 0.006ms、最大遅延・累積ずれ4.565 / 5.808 / 4.705ms。smoke XMLは通過したが、10分×3および累積ずれ1msのassertはfull modeでのみ行うためA14合格を示さない。XML: /private/tmp/MondmadeVJ0-stage/a14-player-smoke.xml。外部受信機測定は未実施。CoreMIDI timestamped Clockの別backendを追加。APC mini mk2 Stage OSXEditor / Apple M1 / macOS 26.1.0で8ms先行予約を使った61秒×3速度smokeを実施し、120 / 174 / 240 BPMのfps 59.993 / 59.979 / 59.976、Route 59,993 / 59,979 / 59,976 writes/s、LED 9.999Hz・各48,784 controls、Clock欠落 / dropは全相0、p99 1.216 / 1.234 / 1.193ms、最大呼出し遅延4.114 / 4.296 / 4.753ms、累積呼出しずれ3.219 / 4.011 / 4.304ms。短縮smokeはXML Passedだが累積ずれ1msを超過し10分×3の代替でもないためA14未合格。XML: /private/tmp/MondmadeVJ0-stage/codemidi-safe8-a14-smoke.xml。CoreMIDIの期限付き出力が実機でいつ受信されたかは外部受信機未測定。  20秒×3速度のCoreMIDI予約lead比較も実施。10msは累積submission drift 8.1666 / 0.9441 / 6.7979ms（BPM 120 / 174 / 240）、p99 1.247 / 1.257 / 1.219ms、最大8.168 / 1.281 / 6.799ms、欠落0。12msはdrift 2.1217 / 2.1987 / 4.2602ms、p99 1.235 / 1.175 / 1.219ms、最大2.406 / 2.201 / 4.262ms、欠落0で、どちらも1ms目標未達かつ速度間で一貫しない。20msではBPM120にseek resync 30 / missed 1 / max frame 82.645ms / drift 20.8293ms、BPM174 / 240の最大遅延も9.341 / 15.707msで不採用。製品設定は8msのまま |
| PlayMode合計 | 最新全PlayModeは13件中7 passed / 6 Explicit skipped。ExplicitはPad入力、Fader / Track Button入力、LED表示、Clock / LED共有worker、USB再接続、A14負荷。Pad入力・LED表示・Clock / LED共有workerは実機で別途通過し、Pad緑点灯をユーザーが目視確認。Fader / Track ButtonはStage Projectの絞り込みPlayMode probeを90秒間で7回実行したが、いずれもRtMidi port接続後のRaw MIDIが0件で型付き入力未確認。5回目は90.115秒、6回目は90.596秒、7回目は90.121秒。6・7回目は操作を依頼したが実施有無は確認できていない。USB切断・再接続は実機を抜く操作の確認待ち。A14規定負荷は実施済みだが上記の誤差・欠落条件で不合格 |
| macOS Player | 最新差分を含む部分依存消費側（`com.redmond.mondmade-vj0`: local file package）でStandaloneOSX universal Mono Player build成功。Unity 6000.3.8f1、Unity CLI errors=0。前版のIL2CPP buildとは区別する |
| Windows Player | 最新差分を含む部分依存消費側（`com.redmond.mondmade-vj0`: local file package）でStandaloneWindows64 x64 Mono cross-build成功。Unity 6000.3.8f1、Unity CLI errors=0 / Unity Cloud Diagnostics symbol-upload warning 2件（build success）。HubのEditor module一覧にWindows IL2CPP moduleはない。Unityの[IL2CPP公式資料](https://docs.unity3d.com/jp/current/Manual/il2cpp-introduction.html)はcross-compilationを一般にサポートせず対象OS上でbuildする必要があるとしているため、このmacOS hostでは未実施。Windows上の実行・MIDI機器は未検証 |
| 任意依存なし構成 | R3 NuGet 1.3.0、System.Runtime.CompilerServices.Unsafe 6.0.0、NuGetForUnity、Unity標準moduleだけの隔離Projectでlocal UPM import・compile成功。macOS universal IL2CPPとWindows x64 Mono Player buildも成功。欠落した任意Integrationの参照エラーなし |
| ローカルGit UPM URL | 旧完全SHAを指定した`git+file://`依存のクリーンな消費側Projectで全依存・Core・部分依存構成を検証済み。最新差分はlocal file package参照でEditMode 135 passed / 2 optional skipped、PlayMode 7 passed / 6 Explicit skipped、macOS universal MonoとWindows x64 Mono Player buildを確認。完全SHA `493a8d3b1a82c7c3bf1fd7debf78db1b81ef502b` の`git+file` URLから新規導入し、EditMode 135 passed / 2 optional skipped、PlayMode 7 passed / 6 Explicit skipped、macOS StandaloneOSX universal MonoとWindows x64 Mono Player buildを確認。完全SHA `f238b0a20e0c9b3430f74f2f1c41ed6667ca4257` の`git+file` UPM URLでも部分依存Projectに新規導入し、EditMode 135 passed / 2 optional skipped、PlayMode 7 passed / 6 Explicit skippedを確認した。CoreMIDI backendを含むPackage SHA `066590edf7353b158865311324cf6c194ca74779` の`git+file` URLからも部分依存Projectへ新規導入し、EditMode 145 passed / 2 optional skipped（VFX / URP integration 2件）を確認した。XML: `/private/tmp/MondmadeVJ0-giturl-current/current-git-editmode.xml`。GitHub repositoryはpublicでHTTPS clone URLも確認したが、ローカルmainに未pushのcommitがあり、公開release tagもないため、現行成果のHTTPS UPM導入は未検証 |
| 全依存構成 | Stage Projectの現行PackageでEditMode 169/169、通常PlayMode 7 passed / 6 Explicit skipped。macOS universal Mono / IL2CPP Playerはx86_64 + arm64でbuild成功。現行CoreMIDI resolver / output診断を含むIL2CPP buildは0 errors / 2 platform warnings（macOS SDK deployment target差分、Unity Cloud Diagnostics symbol upload credentialsなし）、Windows x64 Mono cross-buildは0 errors / 2 Unity Cloud Diagnostics symbol-upload warnings。StandaloneOSX IL2CPP Player Test Runnerは「All test(s) succeeded」と表示したが、Player終了後にNUnit XMLが出ず件数は未確定。Windows Player実行、実機MIDI、Windows IL2CPPは未確認 |
| 検証済みSHA全依存Git UPM | 完全SHA `f238b0a20e0c9b3430f74f2f1c41ed6667ca4257` の`git+file` URLから全依存Stage Projectへ導入。EditMode 159/159、PlayMode 7 passed / 6 Explicit skipped。macOS universal Mono / IL2CPP Player buildはいずれもSucceeded、0 errors / 2 warningsで、両方universal x86_64 + arm64 |
| MIDI / OSC部分構成 | Input System＋MIDI＋OSCだけを追加し、Lasp / Cinemachine / URP / VFX Graphなしでimport・compile。旧完全SHA Git URLでEditMode 123 passed / 2 URP / VFX skippedとmacOS universal Mono Player buildを確認した。最新差分のWindows buildまでをlocal file package参照で確認した。全依存構成のmacOS universal Mono Player buildも成功した |
| 未完了の受入 | 現行成果のHTTPS UPM導入（push未実施、release tagなし）、Windows IL2CPP / Windows実機、実Audio device capture、A14 Stage OSXEditor 10分×3速度は不合格、CoreMIDI 8ms先行予約の61秒×3速度smokeも累積ずれ超過、外部受信機実測未実施、USB切断・再接続、A15 Fader / Track Buttonの実機入力 |

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
