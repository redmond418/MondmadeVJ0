# MondmadeVJ0 仕様

更新日：2026-10-27（JST）。[会話の最終設計](reference/conversation-design.md)全65章を実装仕様として整理した。現状は[プロジェクト調査](project-audit.md)、クラス一覧は[機能カタログ](feature-catalog.md)、詳細契約は[設計判断](design-notes.md)、配置・導入は[パッケージ構成](packaging.md)を参照する。従来のローカル受入範囲A01〜A24は検証済み。今回追加したFloat / Vector積分ModifierはUnity Editorでコンパイルし、速度×Unscaled deltaTimeの積算・再Bind時のFloat状態保持をEditModeテストで確認した。Runtime作成メニューからQuickStart ModuleとRouteHostが子オブジェクトとして登録されることもEditorで確認した。Windows上でのPlayer実行は今回の受入対象外。現行commitをpush・release tagした後の公開HTTPS UPM導入確認はリリース作業に属し、ローカル機能完成とは分けて扱う。

## 1. 目的と完成範囲

VJ制作で繰り返し書く制御コードをInspector上の短いRouteに置き換える。通常は静的なPipeline、複数入力だけComposite Source、Route間で状態を共有する場合だけShared Signal Componentを使う。

```text
Input / Time → Source → Modifiers → Route State → Outputs
                                                ├ Visual
                                                └ MIDI LED Feedback
```

完成範囲にはCore、8種類のRoute、Composite、Shared Signal、FrameClock、Transport、Tap、Subdivision、Quantization、MIDI Clock入出力、Input System、MIDI Surface、APC mini mk2 / Generic Driver、LED Layer、再接続、Editor MIDI Learn、OSC入出力、Lasp、各Unity Output、Inspector、Validation、各Monitorをすべて含む。実装順序は完成範囲を縮小するものではない。

以下は本基盤の対象外とする。

- ランタイムで配線を変更するノードエディタ、Runtime MIDI Learn、Bank/Pageによる動的Binding
- 選択Parameterの中央Registryとランタイム検索・差し替え
- グラフ／Binding／Automationのセッション保存・復元、Snapshot Morph
- 高レベルなBeat Automation管理機構（Phase・Envelopeなどを通常のRouteで組む）
- 上記の動的Bindingを前提としたSoft Takeover

Editor MIDI Learnは完成要件。物理Controlを選択する編集補助として提供する。

## 2. 型とデータフロー

| Route | 値・用途 |
| --- | --- |
| `FloatRoute` | float：強度、速度、Phase、Audio |
| `IntRoute` | int：Index、Camera、Texture、Mode |
| `BoolRoute` | bool：Enable、Hold、Toggle結果 |
| `Vector2Route` | XY、UV |
| `Vector3Route` | Transform、VFX Vector |
| `Vector4Route` | Shader / VFXのVector |
| `ColorRoute` | Color |
| `TriggerRoute` | 履歴を持たない一回の発火 |

Value Sourceは購読時に現在値を必ず1回、その後に変更値を通知する。`false`、`0`、黒色、ゼロVectorも有効な初期値。購読前のSubjectへの送信で代用しない。Triggerは購読だけでは発火せず、過去のTriggerを再送しない。

Modifierは同型から同型への変換だけを担当する。ToggleはTrigger→boolのStateful Source、Float→IntやFloat→Gradient Colorは型変換Sourceとする。

## 3. Core APIとRouteの責務

Runtime APIは下記の契約をR3の`Observable<T>`と`Unit`で実装する。実際の宣言・追加契約はパッケージ内の`Contracts.cs`、`Routes.cs`、`RouteContext.cs`を参照する。

```csharp
public interface IValueSource<T>
{
    Observable<T> Build(RouteContext context);
}
public interface IValueModifier<T>
{
    Observable<T> Apply(Observable<T> source, RouteContext context);
}
public interface IValueOutput<T>
{
    void Initialize(RouteContext context);
    void Write(T value);
}
public interface ITriggerSource
{
    Observable<Unit> Build(RouteContext context);
}
public interface ITriggerModifier
{
    Observable<Unit> Apply(Observable<Unit> source, RouteContext context);
}
public interface ITriggerOutput
{
    void Initialize(RouteContext context);
    void Invoke();
}
```

Unityのシリアライズには具体型Routeを使い、共通RuntimeでBindingを処理する。Routeは名前、有効状態、`SerializeReference`のSource・Modifier列・Output列を持つ。

Routeが最終Streamを一度だけ購読し、最終値を保持して全Outputへ配る。OutputはStreamを購読しない。Outputが増えてもSource・Modifier・乱数・Envelopeを二重実行しない。同期初期値を受ける前にOutputをInitializeする。購読・状態・フレーム更新の破棄と再Bind時の所有権は[設計補足](design-notes.md)で扱う。

Shared SignalはFloat / Int / Bool / Vector2 / Vector3 / Vector4 / Color / TriggerのComponent。単一Routeの複数Outputで済む場合には追加しない。Signalを読むSourceと書くOutputを提供し、循環参照はValidation対象とする。

## 4. Runtimeと時間

Sceneには通常 `MondmadeVJ0Runtime` を1つ置くが、複数の独立したRuntimeも共存できる。FrameClock、Transport、MidiHub、OSC、AudioなどのServiceへは `RouteContext` を通じてアクセスする。静的Singletonの検索を前提にしない。

FrameClockはRuntime単位で共通化し、time / unscaledTime / dspTimeとdeltaTime / unscaledDeltaTimeを配信する。Smooth、Envelope、Audio、Phase Sourceは個別にEveryUpdateを作らない。

CoreがRuntime RootとClock / Transportの抽象契約を持ち、Integration Moduleを明示参照して初期化する。1フレームは時刻採取→外部Queue配送→Transport / FrameClock→末尾Trigger合成→LED送信の順。Disableで購読を破棄し状態を保持、Enableで再Bindする。Source / Modifier例外はRoute単位、Output例外はOutput単位で隔離する。詳細はD02・D03・D19に従う。

## 5. Source・Modifier

標準クラス名は[機能カタログ](feature-catalog.md)に全件列挙する。Vectorなどの略記は対応する具体型を用意する。

- 基本：各Value型のConstant、Shared Signal読み出し
- Input System：float / bool / Vector2 / Vector3、performed / canceled Trigger、LastPressedIndex。InputAction Assetを直接参照し、必要ActionのEnableを管理する
- MIDI：Absolute / Relative float、Velocity、Button Hold、Button Group Index、Press / Release Trigger。Toggleなどの論理状態を物理入力Sourceへ混在させない
- Stateful：Toggle、SetReset、Pulse、Step、IncrementDecrement、TriggerValue、TriggeredRandom、SampleHold、TriggerEnvelope
- Composite：Float / Int二項演算、Bool論理、Compare、Select、MostRecent、Trigger合成
- 型変換：数値・Bool・Index Lookup・Vector抽出／組立・Float→Gradient Color
- Transport / Lasp / OSC：各Serviceの通常Source

型変換SourceのInputは、変換前の型を出力するSourceを内包する。`FloatToVector2Source` / `FloatToVector3Source` / `FloatToVector4Source`は入力floatを全成分へ複製する（例：`f`→`Vector3(f, f, f)`）。1成分だけを入力値で置換する場合は`FloatToVectorNComponentSource`を使い、固定成分と対象軸を指定する。InspectorはInput型に適合するSourceだけを選択肢に表示する。

Float演算はAdd / Subtract / Multiply / Divide / Min / Max / Modulo / Power、Int演算はPowerを除く同系列。Compareは6種の比較とfloatのepsilon、SelectはCondition / True / Falseを持つ。MostRecentは最後に変更された入力を採用する。

EnvelopeはStart / Peak / End、Attack / Hold / Release、AnimationCurve、Restart / Continue / IgnoreWhilePlaying、Seconds / Beatsを持つ。

ModifierはFloatの演算・Range・Curve・DeadZone・Quantize・Smooth・AttackRelease・SlewRate・積分・波形、Intの演算・Clamp・Wrap・Lookup、BoolのNot、Vectorの演算・積分・Normalize・Magnitude制限・Smooth・Remap、ColorのMultiply・Intensity・Alpha・HSV、TriggerのCooldown・Delay・EveryNth・RandomChance・Gate・Quantize・Debounceを揃える。`IntegrateFloatModifier`と`IntegrateVector2/3/4Modifier`は速度入力にdeltaTimeを掛けた値を累積する。ColorにFloat入力を持たせない。

## 6. Musical Transport

保持状態はBase BPM、Multiplier、Effective BPM、Beat Position / Index / Phase、Bar Index / Phase、Time Signature。内部はdouble精度のClockとepochを使用する。

```text
beatPosition = (currentTime - epochTime) * effectiveBpm / 60
beatIndex = floor(beatPosition)
beatPhase = beatPosition - floor(beatPosition)
```

BPM変更時は現在Beat Positionを保存してepochを再計算し、Phaseを連続にする。Beat発生ごとに現在フレーム時刻を次の基準へ置き換えない。Beat・Bar・Subdivision・Quantizationを同じTransportから導出する。

ClockはUnity Unscaled Time、Audio DSP Time（通常優先）、External MIDI、External OSCを扱う。外部BPM更新とPhase同期は別機能として扱う。MIDI ClockはClock / Start / Continue / Stop / Song Position Pointerを受信し、Tempo・Phase・Running状態を復元する。MasterとしてのMIDI Clock送信も完成範囲。

Tap TempoにはSample Count、BPM上下限、Reset Timeout、Outlier Rejectionを持たせ、複数間隔のMedianまたはTrimmed Meanで推定する。Tap、Phase Reset、BPM、Multiplier、NudgeをOutputとして公開する。

QuantizeはBar、Beat、1/2・1/4・1/8・1/16と各三連Gridに対応する。1 beatは四分音符、`1/4`は1拍の1/4、三連は指定長×2/3。Bar GridとBar Subdivisionの位相はTransportの現在Bar原点を使い、拍子変更とSeek後の小節位置を反映する。境界上の入力はその境界へ、それ以外は次の境界へ予約する。Stop / Seekで保留Triggerを破棄する。外部Clock喪失時は最後のTempoでfree-runし、再取得時は通常補正へ戻る。明示Stopでは凍結する。詳細と検証目標はD09〜D11に従う。

## 7. MIDI SurfaceとFeedback

```text
MidiHub → MidiSurface → MidiSurfaceProfile → Logical Controls → Source / Output
Route State → LED Output → Layer合成 → Driver → MIDI OUT
```

ProfileはScriptableObjectで、物理レイアウトだけを定義する。ControlにはStable ID、表示名、Group、Control Type、Input Address、Feedback Address / Capability、Editor位置を持たせる。標準APC mini mk2 Profileは、64 Pad、8 Track、8 Scene、Shift、9 Faderの計90 Controlを持つ。Padの`[x,y]`座標は左下を`[0,0]`とし、yが上向きに増える。RouteはSurface InstanceとLogical Control IDを参照し、Note / CC番号を直接保持しない。

Control TypeはButton、Velocity Button、Absolute Fader / Knob、Relative Encoder、XY、Pitch Bend、Aftertouch。互換Sourceだけを選択可能にする。複数Surfaceと同一Profileの複数実機に対応する。

XY Sourceは同一Surfaceの2つのXY ControlをX / Y軸として合成し、初期状態を含むVector2を通知する。Control ReferenceのPickerは各SourceのControl Type制約に合う候補だけを表示し、不一致の保存値は自動で差し替えない。

ProfileとProtocol Driverを分離し、Generic / APC mini mk2 Driverを標準提供する。APCのRGB Palette、Brightness、Blink、Pulse、Mono LEDはDriverが扱う。上位はColor / Brightness / Off・Solid・Blink・Pulseで指定し、機種別のMIDI値を知らない。

LEDはRouteの最終状態を表示する。Bool、Indexed Int、Float、Color、Trigger Flashに対応し、映像用のToggleやIndexをLED側で再計算しない。標準LayerはBase / State / Beat / Interaction / Alert。Priorityを持ち、追加Layerにも対応する。Flash終了時は下位Layerが自然に見える。

Desired状態とLast Sent状態を比較し、変化時だけ送信する。同一フレーム・同一Controlへの更新は最終状態へ集約する。MIDI切断中はControl・Route・Signalの値とLED Desiredを保持し、再接続時に初期化せずFull Resyncする。Port照合はExact / Contains / Regex / Manual、状態はDisconnected / Connecting / Connected / Error。Editor Learnは受信入力を既存Logical Controlへ解決する。

標準入力はMinis 1.3.2、Raw Clock入力と出力はRtMidi 2.2.0。Raw / Minisの入力経路はSurfaceごとに選択し、同じ通常Controlを二重配信しない。受信callbackではUnity APIを呼ばずMain ThreadへQueue配送する。再接続と同名機器の扱いはD13・D14に従う。

## 8. IntegrationとOutput

| 対象 | 完成範囲・契約 |
| --- | --- |
| Core | 各型のDebug / Signal Output、Trigger Output |
| Unity Object | Active / Enabled、Object Group Index、Transform Position / Rotation / Scale、各Axis |
| Animator | Float / Int / Bool / Trigger、Layer Weight、State再生。ControllerからParameter候補を選択 |
| VFX Graph | Float / Int / UInt / Bool / Vector2–4 / Color / Matrix / Texture / Texture Index / Event。Exposed Propertyの型で候補を絞る |
| Renderer / Material | float / int / vector / color / texture、RendererのTexture Index。Rendererには原則MPBを使用し、Material Assetを変更するOutputは別にする |
| Volume | Float / Int / Bool / Color / Vector、Component→Parameter選択。Override方針はAlways / Follow Existing / Automatically |
| Cinemachine | Camera Index、FOV、Dutch、Priority。切替方式はPriority / GameObject Active |
| VideoPlayer | Speed / Play / Time / Normalized Time / Clip Indexと再生操作Trigger。Clip切替はKeep State / Auto Play / Restart |
| AudioSource | Volume / Pitch / Pan / Play / Time / Clip Index、再生操作Trigger |
| Generic Component | public field / propertyへの各Value型の書込み。初期化時にAccessorを解決し、毎フレームのReflection検索を避ける |
| OSC Jack | Float / Int / Bool / Vector2–4 / Color / Triggerの入出力。Type Tag API入りの固定forkを使い、外部Transport同期Adapterは`/mondmade/transport`を処理する |
| Lasp | Level / Band / Peak / Spectrum Bin、Threshold / Peak Trigger。BandはLow / Mid / High / Custom Hz、TriggerはThreshold / Hysteresis / Cooldown |

Audio Sourceに平滑化を内蔵せずModifierを接続する。Property候補選択を基本とし、VFX・Shader・Animatorの文字列指定はFallback。UIntはInt入力の非負変換、TextureはIntから固定参照／配列Lookup、MatrixはVector3をTRSの1要素へ割当てて合成する。8 Route型を維持し、D15〜D17・D20の確定契約に従う。

## 9. Editorと観測

InspectorはRoute要約と折りたたみ詳細、型に合うSource / Modifier / Outputの検索・追加、RouteとNodeのCopy / Paste、Route複製・並べ替え・無効化・削除を提供する。

APC mini mk2 Simulatorは8×8 Pad、右側のScene Launch、下側のTrack Buttonと9 Fader、ShiftのEditorWindowを提供する。Play Modeの仮想操作はMidiHubの通常入力Queueへ入り、SceneのRouteとLED Feedbackを通る。Edit Modeは物理MIDIポートを開かない一時MidiHubを使い、入力状態とLED色を単体でプレビューできる。Edit Mode MIDI LearnもSimulatorの仮想入力を受け取り、RouteのControl ReferenceやProfileのMIDI Addressを学習できる。MidiHubへ仮想Surfaceを追加すると、物理Controllerがなくても次回Play ModeからRouteを含めて試せる。

Play前のValidationは未設定Source / Output、Null Target、対象Propertyの不存在・型不一致、MIDI Controlの不存在、Stable ID重複、Compositeの接続不良、Signal循環、無効Rangeを検出し、Valid / Warning / Errorを一覧表示する。

Runtime MonitorはSource Raw値、各Modifier中間値、Final値、Output状態。MIDI Monitorは論理Control / StyleとRaw Channel / Note / CC / Value。Transport MonitorはClock、BPM、Position / Phase、外部同期、Clock Stability、Tap履歴。Performance MonitorはRoute / Source / フレーム更新Node数、MIDI / OSC件数、処理時間を扱い、Editor / Development Buildで有効化できるようにする。

## 10. Assemblyと依存境界

原文第60章のCore / Transport / InputSystem / Midi / Osc / Audio.Lasp / VFXGraph / Rendering / Cinemachine / Video / Animator / Editorを分離する。CoreはR3を使い、UniTask・LitMotionを必須にしない。EnvelopeとSmoothはFrameClockで実装する。LitMotion連携を加える場合は独立Integrationとする。

「標準提供する機能」と「全利用プロジェクトの必須依存」は区別する。単一UPMパッケージへ全機能を同梱し、内部のRuntime / Editor assemblyを機能別に分割する。LaspやCinemachineなどの任意外部依存がない場合は、該当Integration assemblyをVersion DefinesとDefine Constraintsで無効にする。Coreは具体的Transportや任意Integrationへ依存しない。

ソースは`Packages/com.redmond.mondmade-vj0/`で開発し、利用プロジェクトにはGit URLを1つ登録して導入する。Scene / Prefab上のRouteHostが設定を所有し、開発用Sceneは`Assets/MondmadeVJ0.Dev/`に置く。[パッケージ構成と導入](packaging.md)を配置・依存解決の正とする。
