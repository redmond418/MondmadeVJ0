# 実装と検証

更新日：2026-10-22（JST）。[仕様](spec.md)と[機能カタログ](feature-catalog.md)のすべてを完成させるための作業一覧。以下の順序は依存関係を示し、提供機能を段階的に削るリリース区分ではない。D01〜D20の設計判断は確定済み。コード実装・動作検証は未着手。

## 実装作業

### 基盤・依存

- [x] [設計判断](design-notes.md)D01〜D03・D18と[導入仕様](packaging.md)：保存形式、namespace、配布単位、Runtime / Transportの依存境界を確定
- [ ] 単一UPMフォルダPackages/com.redmond.mondmade-vj0、package.json、機能別Runtime / Editor / Tests asmdef、Samples~を作成
- [ ] R3導入手順、外部依存の固定一覧とSetup Windowを作成し、MondmadeVJ0がGit URL1つで導入・更新できることを検証
- [ ] 任意IntegrationのVersion Defines / Define Constraintsを設定し、外部依存なし・一部のみ・全依存ありでimportを検証
- [ ] 必要パッケージの採用版と対象OS・Player backendを記録。R3、Unity Integration、MIDI backendの依存を解決
- [ ] 同一UPM内でCoreとIntegration、Editor、Testsをasmdefで分離。R3のみの最小依存でもコンパイル可能にする
- [ ] 8 Route具体型、generic API、RouteContext、Runtime Root、共通Binding Scope
- [ ] Value初期値、Trigger非Replay、最終Stream単一購読、最終値保持、Output初期化順
- [ ] 共通FrameClock、Service初期化、Enable / Disable / Destroy、例外隔離と診断
- [ ] 各Value / TriggerのShared Signal、Source / Output、循環検出

### Source・Modifier

- [ ] カタログの基本・Stateful・Composite・型変換Sourceを全件実装
- [ ] Float / Int / Bool / Vector / Color / Trigger Modifierを全件実装
- [x] D04〜D08：初期値・同時入力・Trigger合成・数値境界・時間処理・再Bindの意味を確定
- [ ] Input System Source全件と共用ActionのEnable管理（D12）

### Transport

- [ ] double Clock / epoch、BPM / Multiplier、Beat / Bar / 拍子、Phase連続性
- [ ] Tap Tempo、Nudge / Reset、各Transport Source / Output
- [ ] 通常・三連Subdivision、Quantize SchedulerとTrigger Modifier
- [ ] DSP / Unscaled / External MIDI / External OSCのClock切替
- [ ] MIDI Clock受信と送信、Running / SPP、外部同期状態の復元
- [x] D09〜D11：Grid単位、遅延・停止・Seek・外部切断・Clock精度の契約と合格目標を確定

### MIDI

- [ ] MidiHub、Surface Instance、Profile、Logical Control Reference
- [ ] Generic / APC mini mk2 Driver、物理InputとFeedbackの単一定義
- [ ] Absolute / Relative / Velocity / Hold / Group Index / Press / Release Source
- [ ] Bool / Index / Float / Color / FlashのLED Output
- [ ] Base / State / Beat / Interaction / Alertと追加Layer、Priority合成
- [ ] 差分送信、更新Queue集約、Port検出、複数機器、切断・再接続・Full Resync
- [ ] D13〜D14：Realtime Messageとnative backend、実機Protocol、縮退規則を検証

### Integration・Output

- [ ] Unity Object / Transform、Debug / Signal Output
- [ ] Animator / VFX Graph / Renderer・Material / Volume Output全件
- [ ] Cinemachine / VideoPlayer / AudioSource / Generic Component Output全件
- [ ] OSC全型の入出力と外部Transport同期のAdapter
- [ ] OSC Jack forkへType Tag取得APIを追加し、Git commitを固定。i/fの型不一致棄却と4-float Transport同期パケットを検証
- [ ] Lasp Level / Band / Peak / SpectrumとAudio Trigger
- [x] D15〜D17・D20：UInt / Matrix / Textureの接続、資源所有権、IL2CPP accessor、Audio / OSC値の契約を確定

### Editor・運用

- [ ] 型別Node選択、検索、Route要約、複製、Copy / Paste、並べ替え、Disable / Delete
- [ ] VFX / Shader / Animator / Volume / Component Memberの候補選択
- [ ] ProfileのControl候補とEditor MIDI Learn
- [ ] Play前Validation、InspectorのValid / Warning / Error、Route名付き診断
- [ ] Runtime / MIDI / Transport / Performance Monitorと中間値の展開
- [x] D19：処理中の失敗隔離、ログ抑制、観測コストと合格目標を確定
- [ ] 使用例・セットアップ手順・対象OSとPlayer検証結果を追加

## 受け入れ条件

現時点ではテストコードのない設計段階であり、以下は実装時の検証仕様。結果を取得する前にチェック済みにしない。単体契約はEditMode、UnityライフサイクルはPlayMode、外部連携は実機とPlayerで確認する。

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
