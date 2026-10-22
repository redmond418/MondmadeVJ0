# 設計判断と動作契約

更新日：2026-10-23（JST）。[移植元](reference/conversation-design.md)の補足仕様。D01〜D20はユーザーからの設計一任を受けて本作業で確定した。MIDI切断中の値保持、Gridの分数が1拍を基準にすることはユーザーの明示指定。それ以外は本書で採用した設計判断であり、元会話の決定と区別する。Runtime / Editor実装を行い、Unity 6000.3.8f1 macOS arm64 Editorの隔離CheckoutでEditMode 147件・通常PlayMode 7件、APC Pad 1物理入力と緑色LED点灯、ClockとLEDの同一worker実機結合、macOS universalのMono / IL2CPPおよびWindows x64 Mono Player buildを検証済み。D16ではMaterial / Volume cloneの共有lease、Transform / Generic / Shader書込競合、Video prepare待ち・timeout・非同期Faultを契約テストで検証した。D20ではLasp dBFS・FFT bin / Hz・Trigger状態とOSC UDP / 型・queue契約を検証した。切断・再接続probeは実機確認待ち。Windows IL2CPP module、10分MIDI Clock精度、公開Git URL導入、Player上の全Integration受け入れも継続中であり、検証結果は[実装・検証計画](implementation-plan.md)に記録する。

## 採用済みの判断

| 判断 | 根拠 |
| --- | --- |
| 名前はMondmadeVJ0、Hybrid構成 | 最終設計1・4・5・65章 |
| 8 Route型、Value初期値、Trigger分離 | 2・3章 |
| Outputは購読を所有せずRouteから書く | 3章 |
| ToggleはStateful Source、型変換もSource | 9・12・15章。古い案のToggle Modifierを採用しない |
| Runtime MIDI Learn・動的Binding・ノード編集・中央Parameter Registryを外す | ユーザーによるOmochworlDの位置付け修正と最終設計1・4・49章 |
| Editor MIDI Learnは含める | 49・61章 |
| 全機能一式を完成要件とする | 最後のユーザー指定と61〜65章 |
| CoreにUniTask・LitMotionを必須化しない | 25・60章 |

## 原文の表記・契約を整える箇所

| 項目 | 整理内容 |
| --- | --- |
| `SignalValueSource<T>` / `SignalValueSource` | 同じSource群のgeneric APIとカタログ略記。Unity Inspectorには7具体型を公開し、Triggerは別Source |
| `IntSignalOutput` / `SignalIntOutput` | 本文例は前者、28・64章の一覧は後者。カタログは`SignalIntOutput`に統一し、旧表記を別機能に数えない |
| `MidiIndexedLedOutput` / `MidiLedIndexedIntOutput` | 後者へ統一。型はInt RouteからのIndex |
| `GradientColorModifier` | 17章に列挙されるが、同章本文はFloat→ColorをSourceに置き、63章ではModifierから除外。`FloatToGradientColorSource`を採用し、同型変換原則を維持 |
| `SampleHoldVectorSource`等のVector略記 | Vector2 / Vector3 / Vector4へ展開し、`SampleHoldVector2Source`等の具体型名にする。Modifierも`AddVector2Modifier`等とする |
| Output初期化順 | 現在値が同期的に通知されるため、OutputのInitialize後に最終Streamを購読する必要がある |
| Sourceの現在値とModifier | Value Modifierは初期値を同期的に1回通す。Smoothはその値から開始する。Delay / DebounceはTrigger専用なので初期Valueを遅延しない |
| Service RegistryとParameter Registry | Runtime内の依存注入用Service Registryは必要。除外された中央Parameter Registryとは役割が異なる |

## D01：保存・配布・ソース配置

[パッケージ構成と導入](packaging.md)の単一UPM構成を採用する。機能別UPM案は導入の手間を減らすため撤回し、機能別asmdefだけを維持する。名前空間は`MondmadeVJ0`以下。開発ソースは`Packages/com.redmond.mondmade-vj0/`、アプリ・結合検証は`Assets/MondmadeVJ0.Dev/`。Scene / Prefabに置く`RouteHost`が8型のRoute一覧を所有し、NodeをSerializeReferenceで保存する。Route名と別にGUIDを持ち、NodeにもGUIDを付ける。複製時は再採番する。

再利用の単位はPrefab。Sourceツリーを保存するScriptableObjectは標準では作らない。ScriptableObjectはSurface Profile・Paletteなどの不変設定に使用する。異なるSceneのObject参照を含む共通設定Assetを作らない。

## D02：RuntimeとService境界

Coreに`IFrameClock`、`IMusicalTransport`、`IRuntimeModule`と`MondmadeVJ0Runtime`を置く。`RouteContext.Transport`の型は`IMusicalTransport`とし、Transport実装Assemblyを参照しない。CoreのDefault ClockはUnscaled、Transport ModuleがDSP Clockを選択する。

RuntimeがInspectorで明示参照したModule一覧を順に登録し、依存Serviceの不足・重複をValidationする。すべてのModule初期化が済んでからRouteをBindする。Core内RouteはTransport未導入でも使え、拍依存NodeだけがService不足として診断される。SingletonやScene全体検索は使わない。

1フレームは「時刻採取→外部QueueのsnapshotをMain Threadへ配送→Transport / FrameClockの更新→フレーム末Trigger合成→LED送信」の順。外部入力は受信時刻を持ち、その時点のBeat位置へ変換してQuantizeする。配送中に追加された外部イベントは次フレームへ回す。

## D03：状態とLifecycle

NodeのSerializedデータは設定専用。Runtime状態はRoute / Node GUIDをキーとするHost所有のメモリ領域、購読・タイマー・一時資源はBindごとのScopeに置く。DisableでScopeを破棄し、状態はHostが存在する間保持する。Enableで再Bindし、保持値を現在値として1回配信する。Disable中のTriggerは再送しない。Destroy / Play終了で状態を破棄する。ファイルへのセッション保存は行わない。

Smoothの現在値、Toggle、Step、乱数生成器の状態は保持する。途中のEnvelope / PulseはDisable時に中断し、最終出力値を保持する。Enableだけでは再開せず、次Triggerで開始する。Delay / Quantize / Debounceの保留Triggerは破棄する。SignalはComponent単位で同じ寿命とし、保持した値を再購読時に返す。

Bind失敗はそのScopeを逆順でrollbackする。Disposeは複数回呼べる。OutputはInitialize完了後に購読し、同期初期値を受ける。設定変更によるEditor再Bindは状態を初期化する。明示的なReset State操作も用意するが、Enableや機器再接続で暗黙に呼ばない。

## D04：未受信・切断・再接続

**MIDIコントローラー切断では、Route・Signal・物理Controlの最後の値を保持する。** BoolのHoldもReleaseを捏造せず保持する。接続状態だけDisconnectedへ変える。まだ受信していないControlはProfileの既定値（数値0、bool false）、OSCはSource設定のInitial Valueを使う。Velocityの初期値も0。すべて購読時に1回通知する。

再接続で0やfalseを送らず、最新LED DesiredをFull Resyncする。実機から次のValueを受信した時点で通常どおり反映する。実機が再接続時に送る本物のReleaseは受け入れる。物理Note On / Offを受けた場合は値が保持値と同じでもPress / Release Triggerは発行する。MIDI入力でないPhase / Audio / Envelopeは各自のClockに従い動作を継続する。OSCも受信途絶では保持する。

## D05：Compositeの初期値・競合

全子Sourceの初期値が揃うまで内部通知を抑制し、合成初期値を1回だけ発行する。初期値を変更イベントとして数えない。SelectはConditionと両枝を購読し、非選択枝の最新値も保持するが、その更新ではOutputを変更しない。

MostRecentには`initialSourceIndex`（既定0）を持たせる。変更が発生するまではその入力を採用する。同じ値の反復通知は「変更」に数えない。変更はMain Threadの配送連番で順序付けし、後の変更を採用する。外部Queueは単調時刻、同時刻は受信連番で整列。Valueの重複排除は型の完全一致で行い、floatのepsilonはCompare専用とする。

## D06：Trigger合成

- Merge：到着した全イベントをその順序で流す。同一フレームの別入力も各1回。
- Any：同一FrameClockフレーム内の1件以上を、フレーム末に1回へまとめる。再入による次フレーム予約は発生源を保持し、循環参照はValidationで禁止する。
- All：各入力が`windowSeconds`（既定0.1秒）以内に1回以上発火すると1回出す。最初の発火から窓を開始し、成功または期限切れで全フラグを消す。期限ちょうどは窓に含め、期限後の入力は新しい窓の先頭になる。同じ入力の連打は1フラグ。

空入力列はValidation Error。Allの初期化や再Enableでは発火しない。

## D07：数値・Index境界

設定の逆Range、空Lookup、epsilon負値、非有限の定数、量子化段数2未満はValidation Error。実行時の0除算、0 Modulo、Powerの非実数結果、NaN / Infinityは当該Nodeの更新を棄却し、最後の有効値を維持する。最初から不正なら型のゼロ値を使用し診断する。Intはlong中間値で計算しint範囲に飽和する。`int.MinValue`のAbsは`int.MaxValue`。

Moduloは正の除数に対して0以上の結果とする。Wrapは両端を含む整数範囲、Repeat / Phaseは終端を含まない範囲。Lookup / Camera / Clip / TextureのIndexは端へClampする。Float→IntのRoundは中間値を偶数へ丸め、Floor / Ceilは名称どおり。Float比較の既定epsilonは0.00001、等値は絶対差がepsilon以下、厳密不等号はepsilon幅を超えた差とする。ゼロVectorのNormalizeはゼロ。

ColorはHDR値を保持し、Alphaを変えるModifier以外で暗黙Clampしない。Gradient入力とMIDI正規化値のみ0〜1へClamp。DeadZoneは0〜1入力用で、閾値以下を0、残りを0〜1へ再Mapする。Remapは設定した両端を使い、出力Clampは既定OFFとする。

## D08：Envelope・平滑化・時間処理

秒単位の既定ClockはUnscaled、拍単位はEffective Beat Position。SmoothはSource初期値から始め、入力が止まってもFrameClockで追従する。時間定数0は即時反映、負値はValidation Error。指数平滑は`1 - exp(-dt / tau)`、AttackReleaseは上昇／下降に別tau、SlewRateは1秒あたりの最大変化量とする。

EnvelopeのRestartはStartへ戻しAttackを再開、Continueは現在出力をAttack開始値としてPeakへ指定Attack時間で進め直す。IgnoreWhilePlayingはAttack / Hold / Release中の入力を無視する。Attack / Releaseは各区間0〜1でCurveを評価、0時間区間は直ちに通過する。Pulse再Triggerは終了期限を延長する。

Delayは全入力を保持し、同期限は到着順。Debounceは末尾発火、Cooldownは先頭を通して期間中を破棄、Gateは通過時のBoolを読む。EveryNthは初期count=0でN件目に最初の発火。RandomChanceは0〜1の確率。待機QueueはNodeあたり1024件、超過は新規分を棄却し診断する。

Random SourceはNodeごとのseedを設定可能にし、既定はRoute / Node GUIDから安定生成する。DisableでPRNG状態を保持、明示Resetで同じseedからやり直す。SampleHoldはTrigger時点の最新Valueを採取し、初期値は設定値（既定ゼロ）。SetReset同時入力は配送順で後勝ち、Stepは初期値からstep加算しinclusive範囲でWrapする。

## D09：Beat・Grid・拍子

**1 beat＝四分音符の長さ。Gridの`1/4`＝1拍の1/4。** 拍子の分母が変わってもBPM / beatの時間単位は変えない。既定拍子は4/4、Bar長は`numerator * 4 / denominator` beat。6/8なら3 beatで1 Bar。拍子変更は現在のBar境界を新拍子の原点として適用し、Beat位置は変えない。

Gridはbeat数の有理数で保存し、`1/2T = (1/2)*(2/3) = 1/3 beat`、`1/4T = 1/6 beat`。Bar Gridだけは現在の拍子から算出する。Nudgeは±1/32 beat、Forwardが正、Backwardが負、0より前には進めない。ResetはPosition=0・Bar原点=0とし、Reset自体でBeat Triggerを発行しない。Indexは0始まり、Inspectorの小節・拍表示は1始まりとする。

## D10：Quantize・遅延・Stop

Quantizeは入力のBeat位置を基準に次のGridへ予約する。境界との差が0.0000001 beat以内なら現在境界を採用する。それ以外は未来の最初の境界。Bar Gridの原点は現在のBar原点に合わせ、拍子変更とSeek後の小節位置を反映する。予約は秒ではなくBeat位置を持ち、BPM変更に追従する。同一境界への別Triggerはまとめず到着順にすべて実行する（集約したい場合はAnyを接続）。

通常のBeat / Subdivision通知はフレームが跨いだ境界を順番に通知する。Nodeあたり1フレーム256件を上限とし、超過分は破棄して現在位置へ追い付き、欠落数をMonitorへ記録する。Quantize予約はNodeあたり1024件、実行もフレーム256件まで。超過した期限済み予約は過去Triggerとして破棄する。

StopではTransport位置を凍結しQuantize予約を破棄。停止中の新規Quantize入力も棄却し診断する。Continueは保持位置から再開する。Reset / Seek / SPP / Clock切替では予約と境界検出履歴を消し、新位置から再開して過去のBeatを再生しない。Transportの`TimelineRevision`はStop・Reset・Seek・Clock切替・外部Clockの初回取得で増加し、予約NodeはFrame境界だけに依存せず古い予約を破棄する。外部Clockの喪失・再取得はfree-run位置と通常補正を継続する。

## D11：外部同期・MIDI Clock

MIDI Clockは24 pulse / quarter note、SPPは1単位=1/4 beatとして扱う。Startは0へSeekしてRun、Continueは保持位置でRun、Stopは凍結。Master送信はClock / Start / Continue / Stop / SPPを備える。SPP送信範囲は14 bit、範囲外は送信せず診断する。

受信時にOSの単調Clockを採取し、native timestampは補助診断として残す。Main Threadで採取したDSP / 単調Clockの対応から時刻変換する。直近24 Clock間隔のMedianでBPMを推定し、毎pulseの位相誤差を最大1/96 beatずつ補正する。Seek / Startだけ即時位置合わせする。Clock受信後1秒間はAcquiring、以後Locked。最後の受信から0.5秒途絶でLostとし最後のTempoでfree-run、再取得時は通常の補正へ戻す。明示Stop後はfree-runしない。

外部の四分音符位置とライブラリのEffective Beatを区別する。Multiplier変更時はEffective Beatを連続に保ち、以後の速度だけ変える。MIDI Master出力はEffective BPM / Beatを送るが、External MIDIモードでは同じPortへのClock再送を禁止する。

Master送信は、Port ordinalと取得時port名をキーにした共有出力dispatcherの専用workerで行う。MidiHubとMidiClockSenderは同じ出力leaseを参照カウントし、同じRtMidi.MidiOut handleを開く。同一Portの送信はこのworkerだけが行い、Unity APIはworkerから呼ばない。Clock schedulerと共有output workerは`ThreadPriority.Highest`を要求する（OSが拒む場合は通常priorityで続行）。transport controlを先に送り、ClockをLEDより優先する。LEDはSurface / Control単位で最新値へ集約し、workerは各LED message後に優先queueを再確認して分割配送する。Clock schedulerは絶対期限の4ms前から共有queueへpulseを渡し、output workerは期限まで待ってから送信する。4msのqueue lookaheadはoutput workerの最終2ms待機窓より前にClockをqueueへ渡す。APC mini mk2の15秒・全LED・1000 Route短縮probeではBPM 120 / 174 / 240各5秒で欠落 / drop 0、p99 0.001〜0.004ms、最大遅延0.003〜0.006ms、累積ずれ0.0029〜0.0053msだった。10分ずつの負荷条件と外部受信機での計測は別途受け入れ対象とする。Stop / Seek / tempo変更時はschedule世代を進めてqueue内・worker待機中の古いpulseを破棄し、送信中の1件が完了するまで世代変更を直列化する。Clockの期限を次pulse周期以上過ぎて送れないpulseは欠落として数え、burstで追い付かない。切断時は未送信のClock / transport controlを破棄し、LEDのDesiredだけを保持する。再接続時は同じordinalとport名を確認して開き、接続世代を更新する。Hubは最新DesiredをFull Resyncする。

Clockの送信呼出し遅延は、workerが`SendMessage`を呼ぶ直前に取得したStopwatch時刻とpulseの絶対期限との差で、最大値とp99を計る。p99は1µs幅のhistogramから保守的に丸め、Monitorからの読取は最大1秒ごとに更新する。欠落数にはschedulerが飛ばしたpulse、queue内で置換されたpulse、workerの期限待機中に次pulse周期以上古くなったpulseを含める。累積期限ずれは同じscheduler世代の最初のClock呼出しを基準に、実際の経過時間と理想期限間隔との差の最大絶対値とする。BPM変更・Seek・再接続後は別世代として基準を置き直す。これはハードリアルタイム保証ではない。

採用する合格目標は、BPM 120 / 174 / 240・各10分、60fps、1000 Route、LED全灯更新10HzのmacOS arm64 / Windows x64環境で、送信呼出し時刻の誤差p99≤2ms、最大≤10ms、欠落0、累積期限ずれ≤1ms。外部受信機での実測も記録し、USB / 機器遅延は別列にする。未達は実装不合格としてbackendまたはschedulerを改善し、機能完成扱いにしない。OS設定と計測機器は結果に必ず添付する。

Tapは直近8間隔のMedian、最初のTapは履歴開始のみ、2秒未入力で破棄。許容BPMは30〜300。3間隔以上の履歴がある場合、Medianとの差が20%超の入力は外れ値として履歴へ入れず、新しい連続2間隔が互いに20%以内ならテンポ変更とみなして履歴を切り替える。TapによるBPM更新は位相を保持する。

## D12：InputAction所有権

Input System ModuleがAction単位のleaseを参照カウントする。最初のlease時に既にEnableなら借用し、最後のlease解放でもDisableしない。ModuleがEnableしたActionだけ最後のleaseでDisableする。PlayerInputが管理するActionは常に借用指定とし、ModuleはEnable / Disableしない。外部管理と自動管理の混在はValidation Error。performed / canceledはTrigger、HoldはReadValueから初期値を採取する。

## D13：MIDI backendと配送

標準Control入力はMinis 1.3.2、MIDI OUTとRaw Clock入力はRtMidi 2.2.0を採用する。通常Controlを両方から二重配信しない。RtMidi raw入力はClock / Start / Continue / Stop / SPP・SysEx用に別経路で扱い、通常Note / CCは無視する。排他的なPort制限がある環境では、SurfaceのInput ModeをRawへ切り替え、同じProfile decoderへNote / CCを配送する。既定はMinis、Clock専用PortはRaw。同一Portを両backendで開けない場合はValidationでRaw指定を案内する。

RtMidiは`IgnoreTypes(sysex:false, time:false, sense:true)`で必要なメッセージを有効化する。callbackのSpanはその場で所有bufferへコピーし、Unity API / R3購読者にはcallbackから触らない。通常Queue容量8192、Clock専用8192、最大SysEx長64KiB、超過は新規分を棄却し件数を診断。Clock Queue overflowでは同期状態をAcquiringへ戻す。通常配送はフレーム4096件までとし残りを次フレームへ保持する。

Portを1秒ごとに再列挙し、失敗時は1秒間隔で再接続する。Surface Instance GUID、機器名、同名機器の明示ordinalで識別し、曖昧な自動割当は行わない。永続的な実機serialを取得できない場合、差し直しで同名機器を物理的に識別できるとは保証せず、Monitorから再指定する。Note On velocity=0はRelease。

対象はUnity 6000.3系以降のmacOS arm64とWindows x64のEditor / Standalone。PlayerはMonoとIL2CPPを検証対象とする。WebGL / mobile / Linuxは本仕様のサポート対象外。R3等の採用版は[導入仕様](packaging.md)に固定する。OS対応は設計上の対象であり、試験済みという意味ではない。

## D14：Profile・LED・Panic

ProfileはInput / Feedback address、Controlの型、範囲、座標、Driver IDを持ち、Driverはメッセージdecode / encode・Capabilityを担当する。Relative EncoderはTwosComplement / BinaryOffset / SignMagnitudeをProfileで明示し、deltaを加算して設定範囲へClampする。形式の自動推測はしない。

XY Controlは同じSurface上で個別のControl IDを持つX / Y軸として定義し、`MidiXYVector2Source`が2つをVector2へ合成する。各Controlの初期値もRouteの初期Vectorへ反映する。MIDI Source / TriggerのControl Reference PickerはnodeごとのControl Type制約で候補を絞り、既存の不一致IDを黙って置き換えない。

Paletteはメーカー対応表のsRGB色をlinear RGBへ変換し、二乗距離最小、同距離は小さいPalette番号を採用する。Brightnessは対応段階への最近傍（同距離は暗い方）。MonoはOffまたはbrightness=0で消灯、それ以外は点灯。Blink / Pulse非対応時はSolidへ縮退し、InspectorへCapability警告を表示する。

Layer Priorityの既定値はBase=0、State=100、Beat=200、Interaction=300、Alert=400。最高Priorityを採用し、同値は設定上の後のLayerを採用する。同一Layer・同一Controlは後の書込が勝つ。Layer書込にはowner tokenを持ち、Scope破棄でownerの寄与だけを除去する。差分比較はDriver変換後の送信payloadを使う。

切断中もDesiredを保持。Panic / All Offは送信をOFFで上書きするラッチとしRoute StateとDesiredを消さない。明示Resumeで解除してFull Resyncする。再接続でもPanicは保持する。通常終了では接続中の既知LEDへOFFを送りPortを閉じる。

## D15：UInt・Texture・Matrix

Route型は8種類を維持する。VFXUIntOutputはInt入力を受け、負値を0へClampしてuintへ変換する。Texture OutputはInt Routeを入力にし、Fixed ReferenceまたはTexture配列Lookupを設定する。Fixedは初期値受信時を含め同じ参照を設定し、配列はD07のClamp。TextureIndex名のOutputは配列Lookup専用のメニューとして残す。Renderer / Materialも同契約。

VFXMatrixOutputはVector3 Routeを入力とし、TRS合成のTranslation / Euler Degrees / Scaleのうち1つを動かす。残りはOutputの固定値（既定：position=0、rotation=0、scale=1）。生成するMatrixはlocal TRS。任意Matrixを渡す追加Routeは設けず、Transform.localToWorldMatrixが必要ならGeneric生成Accessorを通じた専用プロジェクト拡張とする。

## D16：Unity Outputの資源と値

Rendererはmaterial slotを明示し、既存MPBを取得→対象Propertyだけ更新→再設定する。Disableで他Propertyを消さず、映像の最後の値も巻き戻さない。Material OutputはInstanceが既定で、Renderer / slot参照を必須とし、初期化時にcloneを割当、Destroy時に参照を戻してcloneを破棄する。Material参照へ直接書くShared Assetモードは明示指定とInspector警告付きで提供する。VolumeもRuntime cloneを既定とし、共有Profileを暗黙変更しない。同じRenderer slot / VolumeのcloneはRuntime内のServiceで共有・lease管理し、Outputごとに重複cloneしない。

Volume OverrideはAlways＝overrideStateをtrueにして書込、Follow Existing＝overrideStateを変更せず値を書込、Automatically＝初期化時の値と異なる間true・一致時false。Component.activeは維持し、無効Componentへの書込はMonitorに表示する。

Transform Position / RotationはLocalが既定、Worldを選択可能。Rotation値はEuler Degrees、ScaleはLocalのみ。Axis Outputは他軸を保持する。同一target / property / material slotへの複数Value writerはValidation Error。入力をMostRecent等で1つのRouteへ合成する。Transform全Vectorと同軸Axisも競合に数え、別Axis同士は許可する。MIDI LEDはLayer合成を目的とするためこの制約から除外しD14に従う。Generic memberと専用Outputが同じPropertyを操作する場合も検出可能な範囲で診断する。

Videoは未Prepare中の最新Valueを保持し、prepareCompletedで適用する。TriggerはPlay / Pause / Stop / Restartの最新指示1件を保持する。Prepare失敗または10秒timeoutで該当OutputをFault扱いにする。未対応canSet操作はBind時に診断し無効化。Keep Stateは再生／停止状態と可能な限りの秒位置を維持し、Auto Playは0から再生、Restartも0から再生してPrepareを明示的にやり直す。Audio ClipはKeep Stateで同じ契約を既定とする。

CinemachineはPriority方式を既定とし、選択CameraのPriority=100、非選択=0（値は設定可能）。GameObject Active方式も明示指定可能。初期値0でCamera 0を選び、範囲外はClamp。Null CameraはValidation Error。

## D17：Generic accessorとAOT

Editorで対象型・member・Value型からC#の直接代入Accessorを生成する。既定の生成先は利用プロジェクトの`Assets/MondmadeVJ0.Generated/`で、asmdefを置かずAssembly-CSharpにコンパイルする。これにより利用者のAssembly-CSharpにあるComponentも参照できる。asmdefで管理するプロジェクト向けには既存の生成先assemblyを明示指定でき、必要な参照が不足したらValidationする。Core側のAccessor RegistryへRuntimeInitializeOnLoadMethodでfactoryを登録し、Coreから生成assemblyを参照しない。直接参照とPreserveを生成し、IL2CPPでもReflection / DynamicMethod / Expression.Compileを毎フレーム使わない。

対象はpublicなComponent型のpublic instance fieldまたはpublic setter付きproperty。static / readonly / indexer / obsolete error / nested member pathを除外し、完全一致型だけを選択する。生成はInspector設定保存時とGenerate Accessorsメニューで行い、再コンパイル後に使用する。Build前には設定hashと生成物を照合し、不足・古い場合はBuildを止める。Build callback内でソース生成してコンパイル完了とみなさない。setter例外はOutput単位で隔離する。

## D18：Editor・移行

単一UPM内で、各機能が独立したRuntime / Editor / Tests asmdefを持つ。Core EditorはUnity標準UI Toolkitで作り、Alchemyを必須依存にしない。Integration EditorはそのIntegrationとCore Editorだけを参照する。任意外部依存は各IntegrationのVersion Defines＋Define Constraintsで判定する。Coreから無効なIntegrationを参照せず、条件を巨大な単一asmdefへ集約しない。Samples~の依存型も未導入状態で自動importしない。

SerializeReferenceの型名変更はMovedFrom等で移行し、保存schemaVersionを持つ。欠落型を検出したら元のserialized dataを保ち、保存前警告と復旧手順を表示する。自動的にnullへ書き換えたり別型へ置換したりしない。Integration削除前に参照Routeの一覧を表示する。将来の互換性を壊す変更はmajor versionを上げる。

## D19：失敗隔離とMonitor

Source / ModifierのBind・処理例外はそのRouteをFault状態へし、最後の値を保持して購読を止める。Output単独の失敗はそのOutputだけFaultにし、同Routeの他Outputを継続する。Faultは明示Retryまたは再Enableで復帰を試みる。通信切断・一時的な未受信はFaultではなく接続状態として扱う。

同じ診断は初回と5秒ごとの集計だけをLogへ出す。Runtime Monitorは全Routeの現在Final値とOutput状態を表示し、Raw→Modifier→Final→Outputの値履歴は選択Route最大32件に限る。Route / MIDI / Transportの各履歴はRuntimeあたり4096件で、上書き件数を表示する。MIDIはLogical / Raw / LED送信queue、TransportはTapと受信MIDI Clock pulse / commandを記録し、送信ClockはModule summaryのsent / missed / lateness統計を表示する。Performance MonitorはRoute出力・同じframe内のpipeline stage時間とFrame callback時間のmean / p99 / max、Route / Source / Modifier / Frame callback数、およびMIDI / OSC件数・毎秒率を表示する。frameを跨ぐmodifierの待機時間は処理時間に混ぜない。p99は64段の対数histogramから保守的な上限値として算出する。CaptureはEditor / Development Buildで切替可能、Release Playerでは履歴・計時を既定OFFとし、無効時は値boxing・履歴lock・Stopwatch読取を行わない。macOS arm64 Editorで200,000 eventの無効capture経路は直接Subject 10.50ns / event・0 byteに対して、Trace wrapperが34.16ns / event・0 byteだった。追加23.65ns / eventを受け入れ、1000 Route / 60fpsの単純Float処理時間目標p99≤1msはRelease計測で別途確認する。MIDI / OSCの可変長受信bufferはpoolを利用する。

## D20：Audio・OSC

LaspLevelはAudioLevelTracker.inputLevelのraw dBFSを読み、Sourceのfloor=-60dBFS / ceiling=0dBFSで0〜1へ正規化する。tracker内のhold / fallを重ねない。LaspPeakは最新音声bufferの絶対振幅最大値を同じdB範囲へ変換する。無入力・切断は0（AudioはMIDIの値保持指定と別契約）。

SpectrumはLaspのlinear-frequency spectrumSpanを利用し、bin中心周波数を`binIndex * sampleRate / FFTSize`で対応付ける。SpectrumBinは選択binの値を0〜1へClamp。Bandは割当済みSpectrumAnalyzerのlinear spectrumについて、中心周波数が`[lowHz, highHz)`に入るbinの算術平均を返す。Low=20〜200Hz、Mid=200〜2000Hz、High=2000〜12000Hz、Customは正の増加Hz範囲。上限はNyquistで切り詰め、範囲内binがない場合は0。Band / Spectrumの入力が無効・切断中の値は0。追加のAttack / ReleaseはModifier側だけに置く。LaspPeakは最新bufferの絶対振幅最大値へ`20*log10(amplitude)`を適用してLevelと同じdB範囲で正規化し、hold / fallを足さない。Peak Triggerの閾値も同じ正規化値を使う。AudioPeakTriggerはThresholdを上抜けた後、FrameClockで観測した値が下降し始めた最初の更新で1回発火し、Threshold-Hysteresis以下で再arm。ThresholdTriggerは上抜け時に発火する。両者とも購読初回は検出状態の初期化だけを行い発火せず、初回値が閾値以上なら一旦reset閾値以下になるまでarmしない。Cooldown既定80ms、Hysteresis既定0.1。OSC入力Queueは8192 packet、Main Thread配送は4096 packet/frame。Queue満杯時は新しいpacketをdropし、counterを増やす。

OSCはaddressを完全一致させ、float=f、int=i、boolはiの0/1、Vectorは2〜4個のf、Colorはlinear RGBAの4個のf、Triggerは引数なし。T/F・double・暗黙の型変換・部分一致は標準wire契約に含めず、不正型は棄却して診断する。SourceにInitial Value、Outputに送信先とaddressを明示する。

upstream OSC Jack 2.0.0の公開Getterはi/fを暗黙変換し、Type Tag自体を公開しないため、厳密な型検査には不足する。配布元はredmond418/OscJack forkとし、`OscDataHandle.GetElementTypeTag(index)`を追加した固定commit `7ef8fa692030079d2ad6d1a314680b96fbd9a328`を採用した。Runtime Adapterは型タグを厳密に検査する。fork APIのPlayer / IL2CPP動作、外部機器との実ネットワーク受け入れは未完。UDP loopbackでは実Type Tagと送受信を確認済み。無関係な機能はforkへ追加しない。

OSC callbackも所有buffer＋Main Thread Queueへ渡し、容量8192・配送4096件/frame・超過新規分棄却。自動echoは実装しない。同じRuntimeで入力→出力の同じendpoint / addressへの直接loopはValidation Error。別PCを跨ぐ構成のloopは自動検出できないため、送受信addressを分ける。外部Transport同期は`/mondmade/transport`へ`BPM(float), wholeBeat(float), fractionalBeat(float), running(float)`の4個のfを1パケットで30Hz以上で受信し、受信時刻を基準に同期する。wholeBeatは0〜16777215の整数、fractionalBeatは0以上1未満、runningは0/1とし、内部ではdoubleへ復元する。範囲外パケットは棄却する。送信側Clockとネットワーク遅延の絶対補償は行わず、D11と同じLost / 再取得規則を使う。
