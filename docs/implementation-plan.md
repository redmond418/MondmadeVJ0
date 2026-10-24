# 実装と検証

更新日：2026-10-24（JST）。[仕様](spec.md)と[機能カタログ](feature-catalog.md)の全機能を実装し、宣言した受入範囲のA01〜A24をローカルで検証した。D01〜D20の設計判断、Core / Integration Editor、Setup、Monitor、OSC loopback / strict Type Tag / main-thread queue、Lasp dBFS / spectrum / trigger、Video prepare・Fault、Material / Volume lease、書込み競合診断を含む。最新StageではEditMode 255/255、通常PlayMode 12/12（Explicit probe 7件はskip）。APC mini mk2実機のPad 1 / Fader 1 / Track Button 1入力、緑LED、USB切断・再接続、A20 Background Music入力、A14 10分×3速度を確認した。A14は累積期限ずれ上限を5msへ改訂した基準で合格。現行source commit `215b48c` ではmacOS universal IL2CPP Playerのbuildと起動、AOT accessor・stripping・RtMidi接続状態も確認した。公開HTTPS UPMからの導入確認だけは、現行commitのpushとrelease tag後に実施する。

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
- [x] D13〜D14：Realtime Messageとnative backend、実機Protocol、縮退規則を検証。APC Pad 1 / Fader 1 / Track Button 1の実機入力を確認済み。Note On velocity=0 Release、Relative Encoder形式、Surface分離はPlayMode契約試験済み。Editorは同名APC Surface追加時に未使用のControl port ordinalを選び、Notes portを避けることをEditMode 2/2で確認。Input System(Minis) backendは同名Surfaceへの曖昧な自動割当を診断して停止し、RtMidiRawと明示ordinalを促すことをPlayModeで検証。二台目APC mini mk2を使う物理確認は今回の受入範囲外

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
| A10 | Transport連続性 | 仮想Clockで長時間進め、BPMと倍率を途中変更。Beat Positionが連続し、Beat / Bar / Phase / Subdivisionが同一時刻に整合。BPM / Multiplier / Tap / Reset / Nudge Outputも直接検証 |
| A11 | Grid | 拍子変更、三連、境界上の入力、大きなフレーム遅延、複数予約、Stop / SeekをD09 / D10に照合 |
| A12 | Tap | 安定Tap、外れ値、タイムアウト、上下限を入力。選んだ推定規則に従う |
| A13 | 外部Clock | Timestamp付きMIDI / OSC入力でBPMとPhaseを別々に検証。Start / Continue / Stop / SPP、喪失・再取得が契約に従う |
| A14 | MIDI Clock送信 | D11のBPM・10分負荷条件で送信誤差p99≤2ms・最大≤10ms・欠落0・累積期限ずれ≤5msを検証。外部受信機の実到着時刻は送信呼出し側と分けて記録し、A14送信側の合否条件とは分離する |
| A15 | MIDI入力 | 各Control Type、Note On velocity=0相当のRelease、Relative Encoder各形式、同名Portの選択とSurface割当を検証する。2台同時の実機確認は二台目を所有しないため対象外 |
| A16 | LED状態 | Toggle / IndexをMIDI以外から変更しても映像とLEDが一致。Interaction Flash終了で下位State Layerへ戻る |
| A17 | LED送信・切断保持 | 静止状態では再送なし。同フレームの更新がControl単位で集約。APC mini mk2実機probe 1/1 Passed。2026-10-23に再確認（47.68秒、1/1 Passed）し、実機Pad 1入力後、USB切断中もPad押下・Fader=0.7・Toggle=true・Hold=trueを保持し、再接続でOutput generationが進み、Panic中に80 LED全OFFをFull Resync、Panic解除後にPad 1 Desired値21（緑）を再送することを確認 |
| A18 | Input System | 2 Routeが同じActionを使い、一方をDisableしても他方を維持。既存PlayerInputとの所有権も確認 |
| A19 | Unity Output | カタログ全Outputを対応型・不正型・対象欠落で検証。MPBの他Property共存、Material / Volume clone leaseと復元、Volume Override状態、Renderer / Transform / Volume書込競合、Video Value / Trigger保留・prepare失敗・10秒timeoutを確認 |
| A20 | OSC / Audio | 型・チャンネル対応、受信thread、Queue、Hz指定、値の単位、Hysteresis / Cooldownを検証。無入力時の現在値も確認 |
| A21 | Inspector保存 | Route / Node複製・Copy / Paste・Undo / Redo・保存 / 再読込後に型と参照が保たれ、意図せず実行状態を共有しない |
| A22 | Validation | 原文第53章の不正構成をすべて作り、Editor診断が該当Routeを指す |
| A23 | Monitor | Raw→中間値→Final→Output、論理MIDI / Raw MIDI、Tap履歴・Clock、処理件数を照合。無効化時の計測コストも測定 |
| A24 | 単一UPM・分離とPlayer | MondmadeVJ0をGit URL1つで導入し、Core必須依存のみ・一部外部依存あり・全依存ありの各構成でimport・Playerビルド。無効Integrationの欠落参照エラーがないこと、macOS arm64でnative MIDI・AOT accessor・strippingを検証。Windows x64 Mono cross-buildは確認済み。Windows上の実行・MIDI・IL2CPPは当面の受入対象外 |

## 現時点の検証結果

以下の数値記録には、A14の累積ずれ上限が1msだった当時の試行も含む。それらは当時の判定を残す履歴であり、A14の現在の判定は本書の「A14最新判定」行にある5ms上限と最新10分×3速度測定を適用する。

| 対象 | 結果 |
| --- | --- |
| Unity Editor | Unity 6000.3.8f1 macOS arm64 Stage Projectで全Runtime / Editor assemblyをコンパイル |
| EditMode | Core契約、Transport、OSC Adapter、Editor Validationの原文53章全項目・VFX / Animator / Shader / MIDI参照・Component member型、APC Profile、MIDI Type Picker、Input Action lease、MIDI Clock scheduler、共有MIDI出力workerの優先queue / LED coalescing / Clock metric / schedule世代破棄、時間Modifier、Quantize / Subdivision境界、Monitor履歴と計測コスト、RouteHost複製・Copy / Paste・Undo / Redo・Scene保存 / 再読込、Material / Volume clone leaseとTransform / Genericを含むOutput競合、Video prepare共有・Fault、Lasp値・周波数bin・Trigger状態、OSC型・queue・UDP loopback、Core / Unity / Audio、Animator / Cinemachine、OSC Output契約とDebug Outputのログを含む255 testsが255/255通過 |
| A06 Route隔離 | Source.Buildで例外を投げるRouteだけがFaultedとなり、Route名付き診断を残し、同じRuntime上の正常Routeは初期値を出力してActiveを維持 |
| A08 数値境界 | Negative Modulo / Lookup上下端 / 0除算 / NaN入力時の最終有限値保持を確認し、NaN設定をPlay前に報告 |
| A03 単一評価 | StepとSeconds Envelopeを3 Outputへ接続し、各Trigger SourceのBuild 1回、全Outputで同一の状態遷移と値列を確認 |
| A09 時間処理 | SmoothDampはPause中もUnscaled Clockで入力停止後に追従し、購読破棄後のFrameClock更新を止める。Seconds EnvelopeはPause中のUnscaled時間と大きな1フレームによるAttack / Hold / Release通過、破棄後の停止を確認。Delay / Debounce / Beat Delayの既存試験と合わせEditModeで検証 |
| A11 Grid負荷 | 1025件を同一Beat境界へ予約し、大きく遅れた1フレームで256件だけ発火、期限超過768件とQueue超過1件をそれぞれ診断することを確認 |
| A16 LED状態 | 外部Shared TriggerでToggleBoolを変えた時にState LEDが更新され、Interaction Flash終了後に最新State Layerへ戻ることをPlayModeで確認 |
| A19 Unity Output | Core / Unity / AudioではGameObject・Behaviour・Renderer状態、ObjectGroup clamp、Transform local / world・軸、AudioSource scalar clamp・clip選択、Generic Component 7型の実書込・未設定Target・異型メンバー拒否、Shared Signal 7値型とTrigger、Debug Output 7型のログをEditMode契約テストで確認。AudioSourceのPlay / Pause / Stop / RestartとKeepTime / AutoPlay / Restartのclip切替はPlayModeで確認。Animator / Cinemachineでは、Animator parameter値・Trigger遷移、Float / Int / Bool / Triggerの異型拒否とTarget欠落、Layer Weight clamp、Cinemachine camera index / switch mode・FOV / Dutch / Priority・全単体Target欠落を確認。OSC 8 OutputはUDP loopbackで型タグと値を照合。VFXは11 Value Outputの未設定Target / 未公開Propertyと既存Propertyの誤型拒否、TriggerのTarget / event名を検証し、同梱Flames.vfxの実Float Property `Spawn Rate`へ27.5を書いて読み戻す契約も確認した。Renderer / MaterialのFloat・Int・Vector・Color・Texture値とRenderer Texture Index、全11 Outputの未設定Target・異型Shader Property拒否、Volume 7型の書込、および各型のTarget欠落・誤型parameter拒否を確認。ShaderLab Integer型にSetIntegerを使う修正も実アセットで検証した。Videoは全9 OutputのTarget欠落とClip Indexの空clip listも検証した。Material / Volume leaseとMPB、Video prepare契約は別テスト済み。MIDI LEDのFloat / Color / Indexed Int Outputの色・明るさ・選択Indexと、5種類すべての未知Control / LEDなしControl参照拒否もPlayModeで確認した。全検出OutputノードをValue 7型とTriggerの8契約でEditor Output Pickerへ照合し、適合するPickerにだけ表示されることを網羅検証。AudioSource Clip Indexの空・null clip list、Transport Output 6種の未登録service拒否も追加確認した |
| A21 Inspector保存 | Route複製でRoute / 子Node IDが再生成されること、Copy / Paste後の値の保持と独立性、Undo / Redo、managed-referenceのRoute型 / 値 / IDと同Scene Renderer参照のScene保存 / 再読込をEditModeで確認。複製処理が基底クラスのSerializeReferenceを走査せず子Node IDを共有していた不具合を修正 |
| A22 Editor Validation | 原文第53章の未設定Source / Output、全検出OutputノードのValue / Trigger Picker型適合、Null target、VFX property不足、Animator parameter不足・型違い、Shader property不足・型違い、MIDI Control不足・Control Type違い、Route / Node Stable ID重複、Composite接続不良、Signal循環、逆RangeをStageで検証。Generic Component member不足・型違い、Renderer / Material / Volume / Transformの複数Writer競合も検出。Stageの全255 EditMode testsが255/255通過 |
| A23 Monitor | Source Raw / Modifier / Final / Outputを選択Routeごとに表示し、MIDI raw / logical / LED queuedとTap / MIDI Clock履歴、Transport状態、Route・Frame callback時間とMIDI / OSC毎秒件数を表示。各履歴は4096件、詳細Route選択上限32、Releaseではcapture既定OFF。Stageの測定でcapture OFFのtrace経路は直接Subject 10.50ns/event・0 byteに対しinstrumented 34.16ns/event・0 byte、追加23.65ns/event（20万event、macOS arm64 Editor）。Route履歴の上限・無効化・MIDI / Transportデータ・Source→Modifier→Output履歴もEditModeで検証 |
| A20 OSC / Audio | LASP値とdBFS正規化、FFT sampleRate / sizeからのBand Hz集計、Nyquist clamp、Threshold / Peak Trigger初期化・Hysteresis・80ms CooldownをEditMode 14/14で検証。OSC型・初期 / 最新値、worker→8192 queue→main-thread配送、drop / malformed計数、実UDP loopback input / outputを6/6で検証。さらにmacOS universal Mono / IL2CPP Playerの両方を別プロセスのUDP OSC相手と接続し、各Playerが0.375fを受信してmain-threadで最新値を公開した後、0.625fを相手へ返す双方向通信を確認した。2026-10-23、対話型Unity EditorでBackground Music入力（2ch）を8秒取得し、Runtime FrameClock経由の2,984 normalized samples・peak 0.797を観測、probe 1/1 Passed。音声ファイル・波形は保存していない |
| PlayMode | AudioSourceの再生・clip操作、lifecycle、MIDI Surface分離、Note On velocity=0 Release、3形式のRelative Encoder、Button / Velocity / Fader / Knob / Pitch Bend / Aftertouch、XY→Vector2、LED Bool / Float / Color / Indexed Int / FlashとLayer復帰を通常12 testsが12/12通過（MIDI Raw入力queueのbyte上限回帰を含む）。明示指定の実機試験は別枠 |
| APC mini mk2 | Input / Outputを機器名で検出。実機の左上Pad 1からNote 56を受信し、物理入力probe 1/1通過。型付きPad probe #14は9.214秒でPassedし、VelocityButtonの押下・正規化velocity・Releaseを実機で確認。AKAI Bulk SysExでPad 1を緑点灯し、ユーザーが目視確認。Pad 1 LED probeとClock 24 pulse＋LED queueの共有worker結合probeも各1/1通過。QuickStart assetの全64 Pad input / output note layoutをUnity AssetDatabase testで照合。Fader / Track Button probeは初期の10回で対応入力を確認できず、probe #11は物理操作なしでRaw MIDI 0件。probe #12は90.103秒でFader 1 CC 48の0〜1往復とNote 0の押下・解放を受信したが、Note 0はマトリクス最下段左のClip Padであり、Track Button 1ではなかった。probe #13は15.210秒でPassedし、Fader 1の型付きAbsoluteFader更新とTrack Button 1（Note 100 / 0x64）の型付きButton押下・解放を確認。XML: `/private/tmp/MondmadeVJ0-stage/a15-fader-track-probe-13.xml`。probe #14 XML: `/private/tmp/MondmadeVJ0-stage/a15-pad-typed-probe-14.xml`。probe #12 XML: `/private/tmp/MondmadeVJ0-stage/a15-fader-track-probe-12.xml`。probe #11 XML: `/private/tmp/MondmadeVJ0-stage/a15-fader-track-probe-11.xml`。RtMidi input / outputはport 0 `APC mini mk2 Control`、port 1 `APC mini mk2 Notes`と列挙され、本probeはport 0を選択。公式[Communications Protocol](https://cdn.inmusicbrands.com/akai/attachments/APC%20mini%20mk2%20-%20Communication%20Protocol%20-%20v1.0.pdf)はFader 1をCC 0x30 / channel 0 / Port 0、Track Button 1をNote 0x64 / channel 0 / Port 0と定義し、Profileの割当は一致する。Editorの「Add APC mini mk2 Surface」は同名Surfaceが使うordinalを避け、`Control` portを優先し、`Notes` portへ誤割当しない。選択ロジックはEditMode 2/2通過。Input System(Minis) backendが同名Surfaceへ曖昧割当をせず、RtMidiRawへ切り替える診断を行う契約もPlayModeで検証。Note On velocity=0 Release、Relative Encoder各形式、2 Surfaceの分離はPlayMode契約試験済み。二台目APC mini mk2を使う物理確認は対象外。 |
| A14 MIDI Clock | 現行40ms先行queue・8 pulse capacity・3ms spin・期限直前までの動的worker待機と、1周期未満の遅延pulseを送信workerへ渡す修正を実装。61秒全LED・1000 Route smokeではBPM 120 / 174 / 240の各fps 59.993 / 59.984 / 59.997、LED 9.999 / 9.997 / 10.000Hz、各約48,784 LED control states（約800/s）、Clock欠落 / output drop 0、p99 0.001ms。累積期限ずれ・最大遅延は各0.777 / 7.488 / 2.655ms、0.779 / 7.489 / 2.655ms。174 / 240 BPMが1ms累積期限ずれ目標を超えてA14未合格。174 BPMの2ms spin比較はfps 59.985、欠落 / output drop 0、p99 0.001ms、最大遅延8.121ms、累積期限ずれ7.013msで不採用。1ms spinの全速度61秒比較ではfps 59.993 / 59.986 / 59.984、p99 0.224 / 0.229 / 0.246ms、最大遅延0.941 / 6.257 / 3.294ms、累積ずれ0.686 / 6.129 / 2.762ms、240 BPMにClock欠落2回が出たため不採用。0.5ms spinの174 BPM試行でも累積ずれ2.495msで不採用。174 BPMで生成workerだけをAboveNormal / UserInitiatedへ下げる比較は累積ずれ4.359ms、Normal / Defaultでは2.574ms、BelowNormal / Utilityでは7.476msで未達。生成workerをNormal / UserInitiatedにすると2.188ms、Normal / Utilityにすると2.050ms、後者を1ms spinと組み合わせても2.077msで、すべて1ms目標未達のため不採用。8ms spin比較では240 BPMに2 Clock欠落、5ms / 4ms候補ではFPS・最大frame間隔悪化と欠落 / seek再同期が出たため不採用。Stage OSXEditor / Apple M1 / macOS 26.1.0の実機で規定の600秒×3速度を実施。120 BPM: fps 59.996、Route 59,996 writes/s、LED 10.000Hz / 479,984 controls、Clock 28,801対約28,800、missed 0 / dropped 0、p99 0.0010ms、最大5.8020ms、累積ずれ5.7989ms。174 BPM: fps 59.994、Route 59,994/s、LED 10.000Hz / 479,984 controls、Clock 41,756対約41,760、missed 3 / dropped 0、p99 0.0010ms、最大12.5650ms、累積ずれ12.5641ms。240 BPM: fps 59.991、Route 59,991/s、LED 10.000Hz / 479,984 controls、Clock 57,589対約57,600、missed 10 / dropped 0、p99 0.0010ms、最大10.3140ms、累積ずれ10.3135ms。各相とも累積ずれ1ms超過、174 / 240 BPMは欠落、174 / 240 BPMは最大遅延10ms超過のため不合格。実行XML: /private/tmp/MondmadeVJ0-stage/a14-full-acceptance.xml。macOS固有のMach期限待機・thread time-constraint policyを加えた61秒全LED短縮比較も3速度で実施したが、policy＋0.5ms spinはClock欠落0 / 0 / 1、最大遅延2.583 / 3.278 / 9.431ms、累積ずれ2.579 / 3.277 / 9.430ms。policyなしMach wait＋0.5ms spinは欠落0 / 0 / 3、最大3.726 / 7.834 / 4.895ms、ずれ3.722 / 7.833 / 4.894msで、いずれも未達のため実装を戻した。短縮比較XML: /private/tmp/MondmadeVJ0-stage/a14-mach-policy-smoke.xml、a14-mach-spin-smoke.xml、a14-mach-wait-smoke.xml。さらにStandaloneOSX Playerでも同じ61秒短縮probeを実行。120 / 174 / 240 BPMのfpsは59.377 / 59.559 / 59.575、最大frame間隔92.310 / 31.294 / 24.728ms、Route writes/s 59,377 / 59,559 / 59,575、LED 10Hz・約800 states/s、Clock欠落0 / 0 / 0、p99 0.096 / 0.031 / 0.006ms、最大遅延・累積ずれ4.565 / 5.808 / 4.705ms。smoke XMLは通過したが、10分×3および累積ずれ1msのassertはfull modeでのみ行うためA14合格を示さない。XML: /private/tmp/MondmadeVJ0-stage/a14-player-smoke.xml。外部受信機測定は未実施。CoreMIDI timestamped Clockの別backendを追加。APC mini mk2 Stage OSXEditor / Apple M1 / macOS 26.1.0で8ms先行予約を使った61秒×3速度smokeを実施し、120 / 174 / 240 BPMのfps 59.993 / 59.979 / 59.976、Route 59,993 / 59,979 / 59,976 writes/s、LED 9.999Hz・各48,784 controls、Clock欠落 / dropは全相0、p99 1.216 / 1.234 / 1.193ms、最大呼出し遅延4.114 / 4.296 / 4.753ms、累積呼出しずれ3.219 / 4.011 / 4.304ms。短縮smokeはXML Passedだが累積ずれ1msを超過し10分×3の代替でもないためA14未合格。XML: /private/tmp/MondmadeVJ0-stage/codemidi-safe8-a14-smoke.xml。CoreMIDIの期限付き出力が実機でいつ受信されたかは外部受信機未測定。  20秒×3速度のCoreMIDI予約lead比較も実施。10msは累積submission drift 8.1666 / 0.9441 / 6.7979ms（BPM 120 / 174 / 240）、p99 1.247 / 1.257 / 1.219ms、最大8.168 / 1.281 / 6.799ms、欠落0。12msはdrift 2.1217 / 2.1987 / 4.2602ms、p99 1.235 / 1.175 / 1.219ms、最大2.406 / 2.201 / 4.262ms、欠落0で、どちらも1ms目標未達かつ速度間で一貫しない。20msではBPM120にseek resync 30 / missed 1 / max frame 82.645ms / drift 20.8293ms、BPM174 / 240の最大遅延も9.341 / 15.707msで不採用。A17で追加した1秒間隔のPort presence pollingを含む2026-10-23のA14 20秒×3速度smokeはXML Passed。120 / 174 / 240 BPMのfps 59.793 / 59.907 / 59.870、LED 9.991 / 9.993 / 9.995Hz、Clock欠落 / output drop 0、p99 1.221 / 1.102 / 0.920ms、最大遅延8.996 / 5.788 / 5.350ms、累積submission drift 5.3545 / 5.5087 / 5.3444ms。smokeは累積ずれ基準を判定せず、10分×3速度のA14合格を示さない。続いてCoreMIDI submission deadlineの1ms前にworkerを起こしspinする候補を61秒×3速度で比較した。120 / 174 / 240 BPMのp99は0.195 / 0.130 / 0.006ms、最大遅延6.589 / 5.802 / 13.356ms、累積drift 5.8905 / 5.7997 / 10.5880ms、Clock欠落 / output dropは0。240 BPMで最大遅延10ms超過しsmoke失敗、全速度のdriftも1ms超過のため実装を戻した。製品設定は8msのまま |
| A14最新判定 | 2026-10-24、ユーザー判断で累積期限ずれ上限を5msへ改訂。最新10分×3速度の測定値4.5406 / 3.7213 / 4.6805msは改訂値内で、A14送信側の受入条件に合格。保存XMLは従来の1ms assertionで実行したためFailedだが、failureは3件の累積ずれだけ。外部受信機の実到着時刻は別観測として未確認で、送信側合格を妨げない |
| PlayMode合計 | 現作業ツリーの通常PlayModeは12/12通過。Explicit probeの実機結果は個別に記録する。APC mini mk2 USB再接続probeは1/1 Passed: 実機Pad 1入力後、切断中もPad押下、Fader 0.7、Toggle true、Hold trueを保持。再接続でOutput接続世代が進み、Panic中の80 LED状態を全OFFで再送、Panic解除後にPad 1のDesired値21（緑）を再送した。A20 Background Music probeは8秒中2,984 normalized samples・peak 0.797でPassed。最新A14 10分×3速度は累積ずれ上限5msへ改訂後の送信側条件に合格（詳細は下記）。
| macOS Player | 検証対象commit `a99e571094e6b58fc9282208ae5e3366ecebf398` の全依存Git UPM consumerからStandaloneOSX universal Mono Player build成功（x86_64 + arm64）。2026-10-24、source commit `215b48c` の修正後QuickStart Sceneからuniversal IL2CPP Player build成功（errors 0 / warnings 1）。実行ファイル、`GameAssembly.dylib`、`RtMidi.bundle`がx86_64 + arm64 universal、codesign valid。Player logにmissing script / scene corruption / AOT例外はなく、AOT accessorは0.625を返し、RtMidi statusはConnected。Artifacts: `/private/tmp/MondmadeVJ0-18d832c-macos-universal-il2cpp-aot.app`, `/private/tmp/MondmadeVJ0-aot-il2cpp-build-retry.log`, `/private/tmp/MondmadeVJ0-18d832c-il2cpp-aot-runtime.log`。Cloud Diagnostics symbol upload設定のwarning 1件はbuildを妨げていない |
| Windows Player | 検証対象commit `a99e571094e6b58fc9282208ae5e3366ecebf398` の全依存Git UPM consumerからStandaloneWindows64 x64 Mono Player build成功。後続StageでのWindows x64 Mono cross-buildも成功した。今回のA24受入ではWindows上の実行・MIDI機器・IL2CPPを対象外とする |
| 任意依存なし構成 | R3 NuGet 1.3.0、System.Runtime.CompilerServices.Unsafe 6.0.0、NuGetForUnity、Unity標準moduleだけの隔離Projectでlocal UPM import・compile成功。Optional Integrationの欠落参照エラーなし。以前のpackage sourceでのmacOS universal IL2CPP buildも成功し、現行source commit `215b48c` の全依存Stageではuniversal IL2CPP buildとPlayer起動を再確認 |
| ローカルGit UPM URL | 検証対象commit `a99e571094e6b58fc9282208ae5e3366ecebf398` を指定した`git+file` consumerで全依存・部分依存・Core必須依存だけの構成を検証した。全依存: EditMode 252/252、PlayMode 11 passed / 7 Explicit skipped。部分依存: EditMode 188 passed / 2 optional skipped、PlayMode 11 passed / 6 Explicit skipped。Core必須依存だけ: EditMode 127 passed / 3 skipped、PlayMode 2/2 passed。各consumerのlock fileが同じ完全SHAを解決した |
| 実装検証対象SHA Git UPM consumer | Input System / MIDI / OSC構成の隔離consumerで`b170336c3dd842077aca4d337a97c9c129c37b8e`を完全SHA指定し、Unity 6000.3.8f1で検証。`packages-lock.json`も同SHAを記録。EditMode 191 passed / 2 skipped / 0 failed、PlayMode 12 passed / 6 Explicit skipped / 0 failed。universal macOS Mono Player build成功、`lipo`でx86_64 + arm64を確認。consumer Playerは起動試験していない。XML: `/private/tmp/MondmadeVJ0-current-head-git-upm-editmode.xml`, `/private/tmp/MondmadeVJ0-current-head-git-upm-playmode.xml`。Player: `/private/tmp/MondmadeVJ0-current-head-git-upm-macos.app`。ローカルGit UPMの検証であり、公開HTTPS URLからの取得を示さない |
| 全依存構成 | 検証対象commit `a99e571094e6b58fc9282208ae5e3366ecebf398` の全依存Git UPM consumerでEditMode 252/252、PlayMode 11 passed / 7 Explicit skipped。最新source commit `215b48c` を同期したStageではEditMode 255/255、PlayMode 12 passed / 7 Explicit skipped、macOS universal IL2CPP Player buildが成功。macOS universal Mono Playerも別Stageでbuild済み。Windows上の実行・実機MIDI、Windows IL2CPPは今回の受入対象外 |
| 検証済みSHA全依存Git UPM | 検証対象commit `a99e571094e6b58fc9282208ae5e3366ecebf398` の全依存consumerからEditMode 252/252、PlayMode 11 passed / 7 Explicit skippedとmacOS universal Mono / Windows x64 Mono Player buildを確認。macOS universal IL2CPP build・Player起動、AOT accessor、stripping、RtMidi接続は後続の現行source commit `215b48c` のStageで検証 |
| MIDI / OSC部分構成 | 検証対象commitのInput System＋MIDI＋OSC構成（Lasp / Cinemachine / URP / VFX Graphなし）をGit UPM consumerで検証。EditMode 188 passed / 2 optional skipped、PlayMode 11 passed / 6 Explicit skipped |
| ローカル完成判定 | 宣言したA01〜A24のローカル受入範囲を検証済み。A14送信側10分×3速度は累積ずれ≤5msで合格。外部受信機による実到着時刻は参考観測として未測定で送信側合格を妨げない。Windows上の実行・MIDI・IL2CPPと二台目APC mini mk2の物理確認は今回の受入対象外。APC mini mk2のA17実機Pad・USB切断 / 再接続は完了 |
| リリース確認 | 公開HTTPS UPM導入は、現行commitをpushしrelease tagを作成する場合に、その後実施する。公開リポジトリへのpushとtag作成は未実施。これはローカルライブラリの実装・受入とは別の公開作業 |

### A20 OSCメモリ上限回帰

2026-10-24のメモリ調査で、受信OSC addressをすべて保持する最新値cacheに生涯上限がないことを確認した。unique addressが増え続ける入力ではcacheが継続成長するため、cacheをLRU 8192件・合計262144 UTF-16 code unitsに制限し、受信待ちQueueも8192件・address合計524288 UTF-16 code unitsに制限した。上限超過時はcacheから古い値を退避し、Queueは新しいpacketをdropするが、cache対象外のpacketを含めてQueueに受理されたeventはMain Threadへ配送する。過大address・LRU退避・総文字数上限のOSC Source / QueueテストをStageで追加確認し、対象テスト10/10通過。XML: `/private/tmp/MondmadeVJ0-memory-osc-tests-final.xml`。macOS全体ではmemory_pressure 57% free、10秒vm_stat sampleでswapout/pageout増加なし、sysctlで使用中swapは約9.6GiBだった。Unityの過去ログはGC memory consumption約190〜450MB。直接観測した失敗要因はIL2CPP dSYM生成時の`No space left on device`で、disk freeは約3.2GiBだったため、RAM不足とディスク不足を区別して記録する。

### 2026-10-24 A14規定負荷とメモリ観測

最新package commit `7398406` を同期したStage OSXEditor（Apple M1、macOS 26.1.0、Unity 6000.3.8f1、APC mini mk2）で、BPM 120 / 174 / 240を各600秒、1000 Route、LED 10Hz、Monitor capture無効、CoreMIDI timestamped backend（schedule lead 8ms）で連続実行した。結果XML: `/private/tmp/MondmadeVJ0-a14-memory-fix-current-600s.xml`。

| BPM | fps / 最大frame間隔 | Route writes/s | LED更新 / states | Clock sent / expected / missed / dropped | p99 / 最大送信遅延 | 累積期限ずれ | GC 0/1/2 |
| ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 120 | 60.000 / 58.361ms | 60,000 | 10.000Hz / 479,984 | 28,800 / 28,800 / 0 / 0 | 0.006 / 13.796ms | 11.8937ms | 14 / 14 / 14 |
| 174 | 60.000 / 31.253ms | 60,000 | 10.000Hz / 479,984 | 41,760 / 41,760 / 0 / 0 | 0.073 / 4.198ms | 4.1927ms | 13 / 13 / 13 |
| 240 | 60.000 / 38.490ms | 60,000 | 10.000Hz / 479,984 | 57,600 / 57,600 / 0 / 0 | 0.260 / 4.278ms | 2.9398ms | 14 / 14 / 14 |

Clock欠落と共有出力dropは0で、fps・Route・LED・p99条件を満たした。BPM 120では最大遅延13.796msが10ms基準を超過し、累積期限ずれも3速度すべてで1msを超過したためA14未合格。指標は`MIDISend` entryと「期限−schedule lead」の差で、外部receiverの受信時刻ではない。

同じ約30分の実行中、macOSのmemory_pressure空き率は概ね31〜35%、終了直後は50%。Unity batch processのRSSは採取時点で約0.10〜0.69GiBの範囲を上下し、低い値へ戻る動きがあった。使用swapは概ね9.2〜9.4GiBで横ばい、30分を通したRSSやswapの継続的な増加は観測されず、この実行からUnityの持続的なメモリリークは確認できなかった。これは他アプリを含む過去のメモリ不足や別シーンの負荷を否定するものではない。ソース調査ではOSC unique address cacheとMIDI Raw入力queueに保持量が大きくなり得る経路を確認し、OSCはA20節のLRU・queue上限、MIDIはD13節のbyte上限を実装した。ほかの外部入力 / 出力queueとMonitor履歴には容量上限があり、Video・Material / Volume・Input Actionの一時資源はDisposeまたはlease解放に結び付いていることを確認した。別問題としてData volumeは使用率100%表示・空き約3.1GiBで、過去にIL2CPP dSYM生成が`No space left on device`で失敗している。

### 2026-10-24 最新Package A14再測定とメモリ観測

最新Package source `1e63f60` を同期したStage OSXEditor（Apple M1、macOS 26.1.0、Unity 6000.3.8f1、APC mini mk2）で厳格なA14 full runを再実施した。BPM 120 / 174 / 240を各600秒、1000 Route、LED全control 10Hz、Monitor capture無効、CoreMIDI timestamped backend（schedule lead 8ms）で連続測定。総実行時間は1800.219秒、XML: `/private/tmp/MondmadeVJ0-a14-latest-10minx3.xml`。

| BPM | fps / 最大frame間隔 | Route writes/s | LED Hz / control states | Clock sent / expected / missed / dropped | p99 / 最大遅延 / 累積期限ずれ | GC 0/1/2 |
| ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 120 | 59.999 / 25.909ms | 59,999 | 10.000 / 479,984 | 28,801 / 28,800 / 0 / 0 | 0.006 / 4.542 / 4.5406ms | 62 / 62 / 62 |
| 174 | 59.967 / 355.677ms | 59,967 | 9.996 / 479,824 | 41,760 / 41,760 / 0 / 0 | 0.142 / 3.724 / 3.7213ms | 62 / 62 / 62 |
| 240 | 59.999 / 30.978ms | 59,999 | 10.000 / 479,984 | 57,601 / 57,601 / 0 / 0 | 0.056 / 4.682 / 4.6805ms | 61 / 61 / 61 |

3速度ともfps・Route書込み・LED更新、p99・最大遅延、Clock欠落0・出力drop 0の条件を満たした。ユーザー判断で累積期限ずれ上限を5msへ改訂し、4.5406 / 3.7213 / 4.6805msは全速度で新基準内のため、A14送信側条件は合格と再評価した。保存XMLのFailedは旧1ms assertionに対する結果で、failure内容はこの3件の累積ずれだけである。測定値はCoreMIDIの`MIDISend` entryと「期限−schedule lead」の差であり、受信側の実到着時刻ではない。

同じ約30分の実行中、Unity batch processのRSSは採取時点で約0.29〜1.03GiBを上下し、増加後に約0.5GiB以下へ戻った。memory_pressure空き率の採取値は37〜56%、終了時56%。このA14負荷ではRSSの持続的増加やメモリ不足を再現せず、継続的なUnityリークは観測していない。ユーザーからもChatGPTとUnityの同時使用時に起きた一時的負荷との見立てを確認し、追加のメモリ調査は不要とした。この測定時のData volume空きは試験前約3.0GiB、終了後約2.8GiBでdSYM生成を妨げたが、後続buildで生成済みのUnity scratchを整理してuniversal IL2CPP buildを完了した。これはRAM不足ではなく一時的なディスク容量不足だった。

### D13 MIDI Raw入力のbyte budget

2026-10-24、個数8192のClock queueと最大64KiBのSysExを組み合わせると、Raw MIDI bufferだけで最大512MiB保持できる設定上限を確認した。通常Controlの挙動とSysEx単体の64KiB上限を維持しつつ、通常 / Clock queue共通でArrayPoolから借りた配列の実サイズ合計を8MiBに制限した。超過packetは該当queueのdrop countへ加算してPoolへ戻し、dequeue完了またはShutdownでもbyte予算を回収する。StageのMIDI Hub PlayMode契約9/9が通過し、byte上限時のdrop、drain後とShutdown後の予算解放を専用回帰で確認した。最新Package同期後のStage全体はEditMode 255/255、通常PlayMode 12/12通過（Explicit 7件はハードウェアprobeのためskip）。XML: `/private/tmp/MondmadeVJ0-editmode-memory-midi-bytebudget-final.xml`, `/private/tmp/MondmadeVJ0-playmode-midi-bytebudget-final.xml`。

### 2026-10-24 検証対象commitのGit UPM・Player検証

commit `a99e571094e6b58fc9282208ae5e3366ecebf398` を完全SHAで指定した`git+file` UPM consumerで確認した。全依存構成はEditMode 252/252、PlayMode 11 passed / 7 Explicit skipped。部分依存構成はEditMode 188 passed / 2 optional skipped、PlayMode 11 passed / 6 Explicit skipped。Core必須依存だけの構成はEditMode 127 passed / 3 skipped、PlayMode 2/2 passed。各consumerの`packages-lock.json`は同じSHAを記録する。

全依存consumerからmacOS universal Mono（x86_64 + arm64）とWindows x64 MonoのPlayer buildが成功した。当時の現行Stage sourceではmacOS universal IL2CPP buildがdSYM生成中の`No space left on device`で停止したが、scratch整理後の後続buildとPlayer起動は成功した（下記記録）。公開HTTPS URLはpushとrelease tag作成を行っていないため未検証。Stage上のprobe出力: `/private/tmp/MondmadeVJ0-giturl-0d79994-full/a99e571-editmode.xml`, `/private/tmp/MondmadeVJ0-giturl-0d79994-full/a99e571-playmode.xml`, `/private/tmp/MondmadeVJ0-giturl-current/a99e571-editmode.xml`, `/private/tmp/MondmadeVJ0-giturl-current/a99e571-playmode.xml`, `/private/tmp/MondmadeVJ0-giturl-baseonly/a99e571-editmode.xml`, `/private/tmp/MondmadeVJ0-giturl-baseonly/a99e571-playmode.xml`。

#### 2026-10-24 現行sourceのmacOS universal IL2CPP Player / AOT検証

QuickStart Sceneの`RouteHost`はRuntime source file内の2つ目の`MonoBehaviour`で、Sceneはclass名だけを持つScene-local `MonoScript` placeholderを参照していた。この形式はEditorでは動いてもPlayerでclassを解決できず、起動時に`level0 is corrupted! [Position out of bounds!]`とmissing-script警告が出た。`RouteHost`を単独の`RouteHost.cs`へ分離し、Sceneの`m_Script`をPackage asset GUIDへ修正した。これはUnityのscript serializationを直す修正で、APIやRoute動作は変更していない。

修正後のsource commit `215b48c` をStageへ同期し、Unity 6000.3.8f1 / macOS universal IL2CPP Playerをbuildした。Build結果はSucceeded（errors 0 / warnings 1）。唯一のwarningはCloud Diagnostics symbol uploadに認証・設定がない旨で、Player build自体には影響しない。Player実行ファイル、`GameAssembly.dylib`、`RtMidi.bundle`はそれぞれx86_64 + arm64のuniversal binaryで、codesignもvalidだった。Player logにmissing script、corrupted scene、Error / Exceptionはなく、controlled shutdown後のprocess exitは0。

配布Sceneへ追加しないStage専用AOT probeで`ComponentAccessorGenerator.Add`を用いて`Il2CppAotAccessorTarget.Gain`のfactory delegateを生成し、`ComponentMemberFloatOutput`経由でIL2CPP Player内の実値0.625を確認した。Unityのmanaged strippingを有効にしたbuildでも実行できた。RtMidiのネイティブportを含むPlayerはruntime logで`MONDMADE_IL2CPP_MIDI_STATUS=Connected`を報告した。別のA20 probeではmacOS universal Mono / IL2CPP Playerと別プロセスOSC peer間の型付き双方向送受信も確認済み。AOT結果は`MONDMADE_AOT_ACCESSOR_RESULT=Passed actual=0.625`。Artifacts: `/private/tmp/MondmadeVJ0-18d832c-macos-universal-il2cpp-aot.app`, build log `/private/tmp/MondmadeVJ0-aot-il2cpp-build-retry.log`, runtime log `/private/tmp/MondmadeVJ0-18d832c-il2cpp-aot-runtime.log`。

Package source同期後のStage全テストはEditMode 255/255 passed、通常PlayMode 12/12 passed・7 Explicit skipped。XML: `/private/tmp/MondmadeVJ0-routehost-split-editmode.xml`, `/private/tmp/MondmadeVJ0-routehost-split-playmode.xml`。A24で定めたmacOS Player、native MIDI、AOT accessor、strippingのローカル確認は完了した。Windows上のPlayer実行は受入対象外。公開HTTPS UPM導入はpush・release tagを行う場合のリリース確認として扱う。

上記は検証した環境に限る。今回の受入範囲でAcceptance A01〜A24のローカル検証は完了している。公開HTTPS URL導入確認は公開push・release tagを行う場合のリリース確認として実施する。

### A14計測点補正

CoreMIDIのsubmission計測を`MIDISend`直前へ移した。修正後のOSXEditor / Apple M1 / macOS 26.1.0 / APC mini mk2実機による61秒×3速度・全LED / 1000 Route smokeは、120 / 174 / 240 BPMの順にfps 59.973 / 59.982 / 59.986、Clock missed / dropped 0 / 0 / 0、p99 1.112 / 1.126 / 0.843ms、最大submission lateness 3.936 / 3.037 / 5.182ms、累積submission drift 3.9339 / 2.9742 / 5.1804msだった。LED 9.998 / 10.000 / 9.998Hz、各48,784 control statesを送信。短縮smokeは成功したが累積ずれは全速度で1msを超え、10分条件でもないためA14は未合格。新計測点でのXML: `/private/tmp/MondmadeVJ0-stage/a14-metric-correction-smoke.xml`。従来の10分probeで記録したfps / LED / missed pulse値は有効だが、p99・最大・累積値は`MIDISend` entryではなく呼出し前のworker時刻で算出していたため、新しい送信呼出し受入記録として扱わない。

### A14追加比較：CoreMIDI送信バッファ再利用

Clock送信ごとのnative allocationをなくし、`MIDIPacketList`とpacket data領域を接続中に共有workerから再利用する。Stage OSXEditor / Apple M1 / macOS 26.1.0のAPC mini mk2実機で、全LED・1000 Route・各61秒の120 / 174 / 240 BPM smokeを実施した。XMLはPassed、FPS 59.994 / 59.979 / 59.984、Clock missed / dropped 0 / 0、submission p99 1.202 / 1.216 / 1.196ms、最大遅延4.196 / 3.060 / 4.909ms、累積submission drift 3.1108 / 2.5123 / 4.9079ms。関連MIDI EditMode 33/33、全EditMode 169/169も通過。過去測定に対する一貫した精度改善は見られず、A14の10分条件・1ms drift・外部receiverのtarget時刻測定も未達のため、完了とは扱わない。XML: `/private/tmp/MondmadeVJ0-stage/codemidi-buffer-a14-smoke.xml` / `/private/tmp/MondmadeVJ0-stage/post-coremidi-buffer-editmode.xml`。

### A14追加比較：Port presence monitor分離

A17切断検知の1秒ごとのMIDI port enumerationをHighest-priority送信workerからBelowNormalのmonitor threadへ移す案を試した。Unity 6000.3.8f1 OSXEditor / Apple M1 / macOS 26.1.0 / APC mini mk2実機、全LED・1000 Route、各61秒のsmokeでは、120 / 174 / 240 BPMの順にfps 59.572 / 59.912 / 59.884、Clock missed / output dropped 0 / 0、p99 lateness 8.898 / 1.115 / 0.605ms、最大lateness 19.558 / 12.916 / 11.378ms、累積submission drift 14.1362 / 12.8197 / 5.7785msだった。120 BPMはClock 2,950送信・約2,928期待、seek resync 84回となり、p99と最大遅延も基準超過。174 / 240 BPMも最大10ms超過。smokeは失敗し、10分×3速度のA14条件も満たさないため、monitor thread分離の変更は採用せず元の実装へ戻した。これはA14精度の改善を示さず、外部受信機での実際の到着時刻も未計測。測定値: Unity Editor PlayMode test log `A14_RESULT`、保存先 `/Users/redmond/Library/Application Support/DefaultCompany/MondmadeVJ0/TestResults.xml`。

### A14再同期判定の修正

2026-10-23、Clock scheduleは開始時の拍位置から絶対beat-zero epochを作る一方、期待拍の算出でも開始拍を再加算していた問題を修正した。開始拍が0以外の場合に一定の位相差を作り、不要な再同期を起こすため、期待拍を絶対epochから算出する。さらに`IMusicalTransport.TimelineRevision`の変化をseekとして検知し、拍差が閾値未満の明示的なseekも再同期する。回帰テストを追加し、MIDI Clock関連EditMode 35/35、全EditMode 173/173、通常PlayMode 8/8が通過した。

修正後にAPC mini mk2実機・Apple M1 / macOS 26.1.0の全LED / 1000 Routeを使った61秒×3速度smokeを実施した。120 / 174 / 240 BPMの順にFPS 59.812 / 59.875 / 59.860、LED 9.999 / 9.998 / 9.999Hz、Clock missed / dropped 0 / 0、p99 submission lateness 1.265 / 1.265 / 1.250ms、最大lateness 11.232 / 15.022 / 11.536ms、累積submission drift 8.5552 / 14.4638 / 10.1650msだった。240 BPMでseek resync 2回。最大latenessは10ms基準、累積ずれは1ms基準を超え、smokeは不合格。600秒×3速度の正式負荷は修正後に未実施で、外部受信機の到着時刻も未計測のためA14は未合格。Unity Test Runner XML: `/Users/redmond/Library/Application Support/DefaultCompany/MondmadeVJ0/TestResults.xml`。

### A14追加比較：3msの送信スピン窓

2026-10-24、CoreMIDIの8ms先行予約に対し、共有出力workerが期限直前の3msをspin待機する方式を20秒×3速度で再計測した。1000 Route・APC mini mk2全LEDを10Hzで更新し、Monitor captureを無効化したOSXEditor / Apple M1 / macOS 26.1.0のsmokeはUnity Test Runner上でPassedだが、smokeでは累積ずれ上限を判定しない。120 / 174 / 240 BPMの順にFPS 59.935 / 59.921 / 59.935、LED 9.998 / 9.995 / 9.997Hz、各15,984 LED control states、Clock missed / output drop 0 / 0、p99 submission lateness 0.013 / 0.012 / 0.111ms、最大lateness 4.059 / 1.210 / 7.964ms、累積submission drift 3.5227 / 1.2057 / 7.9616ms。p99・最大・欠落はこの短縮試験で目標内だが、3速度とも累積ずれ1msを超過し、10分×3速度でも外部受信機測定でもないためA14未合格。GC collectionsは各相1 / 1 / 1。RuntimeにはLED control composite keyのフレームごとの再生成をなくす変更を含めた。CoreMIDI `offline` propertyによる切断検出は利用可能時に使い、取得不能時はRtMidi port enumerationへfallbackする。現作業ツリーのEditMode 173/173、通常PlayMode 8/8も通過した。測定後に通常PlayModeを実行したため、標準`TestResults.xml`は後続結果で上書きされた。

### A14 CoreMIDI仮想受信先probe

2026-10-24、CoreMIDI `MIDISend`へMach host-time timestamp付きのMIDI Clockを直接送り、`kMIDIPropertyAdvanceScheduleTimeMuSec=0`の仮想destination callback時刻と各packet timestampを比較した。20秒ずつの120 / 174 / 240 BPMで予定数960 / 1,392 / 1,920 packetをすべて受信した。callback entryとtimestampとの差はp99絶対値1.911 / 0.074 / 0.077ms、最大絶対値16.345 / 0.113 / 4.514msだった。これはOS内virtual endpointのcallback配送を測るソフトウェアprobeで、APC mini mk2のUSB送信経路や外部受信機の実測ではない。OSのtimestamp schedulingを別経路で確認したが、受信callbackの最大値も10ms目標を超えた速度があるため、A14合格の証明には使わない。Probe source: `/private/tmp/midi_virtual_timing_probe.c`。

### A14現行実装commitの規定10分負荷再試験

2026-10-24、実装commit `e436983`（以降のcommitは文書のみの変更）で、Stage OSXEditor / Apple M1 / macOS 26.1.0 / APC mini mk2実機の規定負荷を実施した。各BPMを600秒ずつ測定し、1000 Route、全LEDを10Hz更新、Monitor capture無効、CoreMIDI timestamped backend（8ms schedule lead）を使用した。

| BPM | fps / 最大frame間隔 | Route writes/s | LED更新 / states | Clock sent / expected / missed / dropped | p99 / 最大送信遅延 | 累積送信期限ずれ | GC 0/1/2 |
| ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 120 | 59.945 / 59.032ms | 59,945 | 10.000Hz / 479,984 | 28,801 / 28,800 / 0 / 0 | 0.155 / 10.249ms | 5.5708ms | 30 / 30 / 30 |
| 174 | 59.940 / 66.693ms | 59,940 | 10.000Hz / 479,984 | 41,759 / 41,760 / 0 / 0 | 0.145 / 10.552ms | 10.5492ms | 30 / 30 / 30 |
| 240 | 59.931 / 65.257ms | 59,931 | 10.000Hz / 479,984 | 57,600 / 57,600 / 0 / 0 | 0.138 / 10.321ms | 7.0446ms | 29 / 29 / 29 |

Clock欠落・共有出力dropはなく、p99・Route・LED条件を満たした。一方、全速度で最大送信遅延10msと累積期限ずれ1msを超えたため、A14は未合格。CoreMIDI指標は`MIDISend` entryと「期限−schedule lead」の差で、外部receiverが受信した実時刻ではない。外部受信機での到着測定も別途未実施。Unity Test Runner XML: `/Users/redmond/Library/Application Support/DefaultCompany/MondmadeVJ0/TestResults.xml`。

### A14 先行予約時間の短縮比較

2026-10-24、CoreMIDIのschedule leadを既定8msから6msへ変更した短縮smokeを、現行Stageコードで120 / 174 / 240 BPM各20秒実施した。FPSは59.980 / 59.958 / 59.957、Clock missed / droppedは各0、p99遅延は0.007 / 0.013 / 0.242ms、最大遅延は1.204 / 7.787 / 2.685ms、累積期限ずれは1.2024 / 7.7803 / 2.6841ms。3速度とも1msの累積ずれ条件を超え、10分負荷でもないためA14未合格。8msから一貫した改善を示さなかったので、6ms候補は採用せず既定8msを維持した。XML: `/private/tmp/MondmadeVJ0-stage/a14-lead6-smoke-rerun.xml`.

### A14 先行予約とspin窓の追加短縮smoke

同日、schedule lead 7msを120 / 174 / 240 BPM各20秒で2回計測した。初回はEditorの外れ値を含み、FPS 58.558 / 59.964 / 59.936、Clock missed 2 / 0 / 0、p99遅延8.764 / 0.007 / 0.005ms、最大遅延25.486 / 0.708 / 3.063ms、累積ずれ21.9245 / 0.7059 / 3.0619msだった。再試行はFPS 59.979 / 59.958 / 59.956、Clock missed / droppedは全相0、p99遅延0.006 / 0.006 / 0.005ms、最大遅延2.680 / 2.600 / 5.265ms、累積ずれ2.6781 / 2.5981 / 5.2642msだった。試行間のばらつきがあり、再試行でも累積ずれ1msを3速度すべてで超えたため不採用とし、既定8msへ戻した。XML: `/private/tmp/MondmadeVJ0-stage/a14-lead7-smoke.xml`、`/private/tmp/MondmadeVJ0-stage/a14-lead7-rerun.xml`。

CoreMIDI lead 8msを維持し、共有workerのspin窓だけを1msへ変えた20秒smokeでは、FPS 59.979 / 59.958 / 59.882、Clock missed / droppedは全相0、p99遅延0.248 / 0.144 / 0.008ms、最大遅延2.880 / 1.175 / 5.590ms、累積ずれ2.8785 / 1.1727 / 5.5889msだった。spin窓0.5msではFPS 59.913 / 59.958 / 59.940、Clock missed / droppedは全相0、p99遅延0.623 / 0.469 / 0.461ms、最大遅延4.388 / 0.644 / 3.155ms、累積ずれ4.3868 / 0.5006 / 2.7607msだった。どちらも10分条件ではなく、累積ずれ条件を満たしたのは0.5msの174 BPMだけなので不採用とした。コードは既定の3ms spin窓へ戻した。XML: `/private/tmp/MondmadeVJ0-stage/a14-spin1ms-smoke.xml`、`/private/tmp/MondmadeVJ0-stage/a14-spin0_5ms-smoke.xml`。

### A14追加比較：共有出力workerのQoSを一段下げる

CoreMIDI Clock生成workerはUserInteractive / Highestのまま、LED・Clockを送る共有出力workerをmacOS UserInitiated / AboveNormalへ下げる61秒×3速度smokeを実機で比較した。BPM 120 / 174 / 240の順にFPSは59.993 / 59.984 / 59.986、p99遅延0.006 / 0.154 / 0.009ms、最大遅延3.548 / 3.058 / 3.794ms、累積期限ずれ3.5465 / 3.0557 / 3.7933ms、Clock missed / output droppedは全相0 / 0だった。結果XMLはPassedだが累積ずれは3速度とも1msを超え、10分条件も満たさない。過去61秒測定より240 BPMで改善が見られたものの、全速度での一貫した改善を確認できず、優先度変更は採用せず既定設定へ戻した。XML: `/private/tmp/MondmadeVJ0-a14-output-worker-priority-61s.xml`。

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
