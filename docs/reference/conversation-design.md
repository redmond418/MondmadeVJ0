# 移植元：Unity基盤機能提案

出典：[Unity基盤機能提案](https://chatgpt.com/c/6a8ad379-fa88-83e9-94e6-47198eba6440)。取得日：2026-10-22（JST）。

会話の最終回答内「MondmadeVJ0 詳細設計」全65章を保存する。第1〜41章はチャット取得ツール、第42〜65章は同一会話のブラウザ表示から取得。後半は表示テキストをMarkdownへ変換しており、装飾・区切り線を整形している。本文の矛盾は保存し、実装用の整理は [spec.md](../spec.md) と [design-notes.md](../design-notes.md) に記す。これは履歴資料であり、実装状況を表すものではない。

---

# MondmadeVJ0 詳細設計

## 1. 設計方針

このライブラリの名前は **MondmadeVJ0** とする。

MondmadeVJ0が担当する範囲は、

```text
Input / Time
    ↓
Source
    ↓
Modifier
    ↓
Route State
    ↓
Outputs
```

である。

ノードエディタやランタイムMIDI Learnのような、ライブ中に配線構造を変更する仕組みは導入しない。

通常のVJ制作ではInspector上で配線を確定させ、

```text
MIDI Fader
→ Remap
→ Smooth
→ VFX Parameter
```

のようなRouteを短時間で大量に作れることを最優先にする。

ただし、

```text
A * B
ButtonA && ButtonB
Trigger + Random
```

のような複数入力処理は、Composite SourceとしてSource内部でツリーを構成できるようにする。

また、同じ状態を複数Routeから共有する必要がある場合だけ、Shared Signal Componentを使用する。

---

# 2. 型システム

MondmadeVJ0では以下の型を正式対応する。

| Route | 主用途 |
| --- | --- |
| `FloatRoute` | Fader、強度、速度、Phase、Audio |
| `IntRoute` | Index、Camera、Texture、Mode |
| `BoolRoute` | Enable、Invert、Toggle |
| `Vector2Route` | XY操作、UV、位置 |
| `Vector3Route` | Transform、VFX Vector |
| `Vector4Route` | Shader/VFX汎用Vector |
| `ColorRoute` | 色 |
| `TriggerRoute` | Spawn、Tap、Randomize、切替 |

`Trigger`だけはValueと完全に別扱いにする。

Value Sourceには、

> 購読された瞬間に必ず現在値を1回流す

という契約を設ける。

したがって、

```text
Bool = false
Int = 0
Float = 0
```

のような初期状態もOutputへ確実に反映される。

Subjectへ先に初期値を送ってしまい、後から購読した側が受け取れない構造は作らない。

---

# 3. Core API

Unity側では具体型を使うが、内部APIはgenericにする。

```csharp
public interface IValueSource<T>
{
    Observable<T> Build(RouteContext context);
}
```

`Build()`が返すObservableは必ず、

```text
Current Value
↓
Changed Value
↓
Changed Value
...
```

という順序になる。

Modifierは、同じ型から同じ型への変換だけを担当する。

```csharp
public interface IValueModifier<T>
{
    Observable<T> Apply(
        Observable<T> source,
        RouteContext context);
}
```

OutputはObservable自体を知らない。

```csharp
public interface IValueOutput<T>
{
    void Initialize(RouteContext context);
    void Write(T value);
}
```

Triggerも別APIにする。

```csharp
public interface ITriggerSource
{
    Observable<Unit> Build(RouteContext context);
}

public interface ITriggerModifier
{
    Observable<Unit> Apply(
        Observable<Unit> source,
        RouteContext context);
}

public interface ITriggerOutput
{
    void Initialize(RouteContext context);
    void Invoke();
}
```

OutputがSubscribeを所有しないことが重要である。

Route側で一度だけ購読し、

```csharp
stream.Subscribe(value =>
{
    foreach (var output in outputs)
        output.Write(value);
});
```

とする。

これにより、Outputが3個あってもSourceやModifierの計算は1回だけになる。

---

# 4. Route

例えば`FloatRoute`は概念的には次のようになる。

```csharp
[Serializable]
public sealed class FloatRoute : IRoute
{
    public string name;
    public bool enabled = true;

    [SerializeReference]
    public IValueSource<float> source;

    [SerializeReference]
    public List<IValueModifier<float>> modifiers;

    [SerializeReference]
    public List<IValueOutput<float>> outputs;
}
```

実際にはBinding処理を共通Runtimeへ抜き、Float、Int、Boolなどでコードを重複させない。

Inspectorでは、次のように表示する。

```text
Camera Move Speed

SOURCE
  APC mini mk2
  Fader 3

MODIFIERS
  Remap
    0..1 → 0..8

  Smooth Damp
    Time: 0.12

OUTPUTS
  PerlinOrbitMover
    Move Speed

  Debug Monitor
```

RouteにはRuntime上で最終値を保持するが、外部から自由に検索・変更できるParameter Registryにはしない。

---

# 5. Shared Signal

Shared Signalは通常使用しない。

例えば、

```text
MIDI
 ↓
Camera Index
 ├→ Cinemachine
 └→ MIDI LED
```

なら、1つのRouteで処理する。

一方、

```text
MIDI ─┐
      ├→ Camera State
OSC  ─┘
          ↓
      Camera処理
```

のように複数Route間で状態を共有したい場合だけ、

```text
FloatSignal
IntSignal
BoolSignal
Vector2Signal
Vector3Signal
Vector4Signal
ColorSignal
TriggerSignal
```

というMonoBehaviourを使用する。

例えば、

```text
Route A
MIDI Button Group
 ↓
IntSignalOutput
 ↓
[CameraIndexSignal]

Route B
CameraIndexSignal
 ↓
 ├ CinemachineIndexOutput
 └ MidiIndexedLedOutput
```

となる。

これはComponent型の利点だけを限定的に使う仕組みである。

HierarchyがSignalだらけになるのを防ぐため、共有しない値には使用しない。

---

# 6. 基本Source

SourceはMondmadeVJ0の主要機能として充実させる。

| Source | 内容 |
| --- | --- |
| `ConstantFloatSource` | 固定float |
| `ConstantIntSource` | 固定int |
| `ConstantBoolSource` | 固定bool |
| `ConstantVector2Source` | 固定Vector2 |
| `ConstantVector3Source` | 固定Vector3 |
| `ConstantVector4Source` | 固定Vector4 |
| `ConstantColorSource` | 固定Color |
| `SignalValueSource<T>` | Shared Signalを読む |

ConstantはComposite Sourceを組む際にも使用する。

---

# 7. Unity Input System Source

| Source | 内容 |
| --- | --- |
| `InputActionFloatSource` | Axis / float |
| `InputActionBoolSource` | Hold状態 |
| `InputActionVector2Source` | Stick / Pointer |
| `InputActionVector3Source` | Vector3 Action |
| `InputActionTriggerSource` | performed時Trigger |
| `InputActionReleaseTriggerSource` | canceled時Trigger |
| `InputActionLastPressedIndexSource` | 複数Actionの最後に押されたIndex |

InputAction Assetの参照を直接保持し、Runtimeで必要なActionだけEnableする。

---

# 8. MIDI Source

MIDIの物理仕様は、後述するSurface層で吸収する。

RouteからNote番号やCC番号を直接指定することは禁止する。

## Value

| Source | 内容 |
| --- | --- |
| `MidiAbsoluteFloatSource` | Fader / Knob → 0..1 |
| `MidiRelativeFloatSource` | Relative Encoder |
| `MidiButtonBoolSource` | 押している間true |
| `MidiVelocityFloatSource` | Note Velocity → 0..1 |
| `MidiButtonGroupIndexSource` | 指定Button群の最後に押されたIndex |

## Trigger

| Source | 内容 |
| --- | --- |
| `MidiButtonTriggerSource` | Note On |
| `MidiButtonReleaseTriggerSource` | Note Off |

例えば、

```text
Surface: APC mini mk2
Control: Fader / 3
```

と選択するだけでよい。

---

# 9. Stateful Source

VJ制作で頻出する状態処理をSourceとして正式化する。

| Source | 入力 | 出力 |
| --- | --- | --- |
| `ToggleBoolSource` | Trigger | bool |
| `SetResetBoolSource` | Set Trigger + Reset Trigger | bool |
| `PulseBoolSource` | Trigger | 一定時間true |
| `StepIntSource` | Trigger | 0→1→2→… |
| `IncrementDecrementIntSource` | + Trigger / - Trigger | int |
| `TriggerValueFloatSource` | Trigger | 指定floatをLatch |
| `TriggerValueIntSource` | Trigger | 指定intをLatch |
| `TriggeredRandomFloatSource` | Trigger | Random float |
| `TriggeredRandomIntSource` | Trigger | Random int |
| `SampleHoldFloatSource` | Value + Trigger | Trigger時の値を保持 |
| `SampleHoldVectorSource` | Value + Trigger | Trigger時のVectorを保持 |
| `TriggerEnvelopeFloatSource` | Trigger | 時間変化するfloat |

例えばToggleは、

```text
MidiButtonTrigger
        ↓
ToggleBoolSource
        ↓
Bool Route
```

となる。

MIDI Source自身にToggle機能は持たせない。

**物理入力と論理状態を分離する**ことを原則とする。

---

# 10. Trigger Envelope

Trigger EnvelopeはVJ用途で頻繁に使用するため、標準機能にする。

```text
Trigger
 ↓
TriggerEnvelopeFloatSource
 ↓
0 → 1 → 0
```

設定項目は次のとおり。

```text
Start
Peak
End

Attack
Hold
Release

AnimationCurve

Retrigger:
  Restart
  Continue
  IgnoreWhilePlaying
```

時間単位は、

```text
Seconds
Beats
```

の両方に対応する。

これにより、

```text
Beat
↓
0 → 1 → 0
↓
VFX Emission
```

を簡単に構成できる。

高レベルなAutomationシステムは導入しないが、低レベルなEnvelope Sourceは汎用機能として提供する。

---

# 11. Composite Source

複数入力が必要な場合だけComposite Sourceを使用する。

## Float演算

```text
FloatBinarySource

A
B

Operation:
  Add
  Subtract
  Multiply
  Divide
  Min
  Max
  Modulo
  Power
```

単純なFloat演算は1クラスに集約する。

## Int演算

```text
IntBinarySource

Add
Subtract
Multiply
Divide
Modulo
Min
Max
```

## Bool

```text
BoolLogicSource

A
B

AND
OR
XOR
```

NOTは単項なのでModifierにする。

## Compare

```text
FloatCompareBoolSource
IntCompareBoolSource
```

Operatorは次のとおり。

```text
==
!=
<
<=
>
>=
```

floatにはepsilonも設定できる。

## Conditional

```text
FloatSelectSource
IntSelectSource
BoolSelectSource
Vector2SelectSource
Vector3SelectSource
Vector4SelectSource
ColorSelectSource
```

構造は、

```text
Condition
True Value
False Value
```

とする。

## Most Recent

```text
MostRecentFloatSource
MostRecentIntSource
MostRecentBoolSource
MostRecentVectorSource
```

複数入力のうち最後に変更されたものを採用する。

例えば、

```text
MIDI Fader ─┐
Keyboard   ─┼→ MostRecent → Value
OSC        ─┘
```

という用途に使用する。

## Trigger合成

```text
MergeTriggerSource
AnyTriggerSource
AllTriggerSource
```

複数Triggerをまとめて扱う場合に使用する。

---

# 12. 型変換Source

Modifierは同型変換に限定するため、型変換はこちらに置く。

```text
IntToFloatSource
BoolToFloatSource
BoolToIntSource

FloatToIntSource
  Floor
  Round
  Ceil

FloatToBoolSource
  Compare Threshold

IndexToFloatSource
IndexToIntSource
IndexToBoolSource
IndexToColorSource

Vector2ToFloatSource
Vector3ToFloatSource
Vector4ToFloatSource

FloatToVector2Source
FloatToVector3Source
FloatToVector4Source
```

VectorからFloatを取り出す場合は、

```text
X
Y
Z
W
Magnitude
SqrMagnitude
```

などを選択する。

`IndexToFloatSource`には、

```text
Index 0 → 0
Index 1 → 0.5
Index 2 → 1
```

のような配列を指定できる。

---

# 13. Float Modifier

Float Modifierは最も豊富にする。

| Modifier | 用途 |
| --- | --- |
| `AddFloatModifier` | +定数 |
| `MultiplyFloatModifier` | ×定数 |
| `RemapFloatModifier` | Range変換 |
| `ClampFloatModifier` | Clamp |
| `Invert01FloatModifier` | 1-x |
| `AbsFloatModifier` | abs |
| `SignFloatModifier` | sign |
| `PowerFloatModifier` | pow |
| `CurveFloatModifier` | AnimationCurve |
| `DeadZoneFloatModifier` | デッドゾーン |
| `QuantizeFloatModifier` | N段階に丸める |
| `FloorFloatModifier` | floorだがfloat維持 |
| `CeilFloatModifier` | ceilだがfloat維持 |
| `RoundFloatModifier` | round |
| `RepeatFloatModifier` | repeat |
| `PingPongFloatModifier` | ping-pong |
| `SmoothDampFloatModifier` | SmoothDamp |
| `ExponentialSmoothFloatModifier` | 指数平滑 |
| `AttackReleaseFloatModifier` | 上昇下降で速度を変える |
| `SlewRateFloatModifier` | 1秒あたり変化量制限 |
| `WaveShapeFloatModifier` | Phase→波形 |

### SmoothDamp

カメラや操作パラメータなど、慣性的な追従に使用する。

### ExponentialSmooth

FaderやAudioの微細なノイズ除去に使用する。

### Attack / Release

```text
上昇: 0.03 sec
下降: 0.4 sec
```

のように上昇と下降で別々の速度を指定する。

Audio Reactiveで特に使用頻度が高い。

### WaveShape

0..1のPhaseを、

```text
Sine
Triangle
Saw
Reverse Saw
Square
```

へ変換する。

これにより、

```text
BeatPhase
 ↓
WaveShape: Sine
 ↓
Parameter
```

だけでBPM同期LFOを構成できる。

---

# 14. Int Modifier

```text
AddIntModifier
MultiplyIntModifier
ClampIntModifier
WrapIntModifier
ModuloIntModifier
LookupIntModifier
AbsIntModifier
```

`Wrap`は、

```text
min=0
max=4
```

の場合、

```text
4 + 1 → 0
```

となる。

Camera、Mode、Texture選択に使用する。

---

# 15. Bool Modifier

```text
NotBoolModifier
```

Boolの時間依存処理はStateful Source側へ置く。

ToggleはTriggerからBoolを生成するため、Modifierではない。

---

# 16. Vector Modifier

Vector2、Vector3、Vector4に対して必要なModifierを用意する。

```text
AddVectorModifier
MultiplyVectorScalarModifier
MultiplyVectorComponentsModifier
NormalizeVectorModifier
ClampMagnitudeVectorModifier
SmoothDampVectorModifier
RemapVectorModifier
AbsoluteVectorModifier
```

Component単位で、

```text
X
Y
Z
W
```

の有効・無効を設定できるModifierも用意する。

mocopi、OSC、XY Pad、Transform制御などにも利用できる。

---

# 17. Color Modifier

```text
MultiplyColorModifier
IntensityColorModifier
AlphaColorModifier
HSVColorModifier
GradientColorModifier
```

HSVでは、

```text
Hue Offset
Saturation Multiply
Value Multiply
```

を設定できる。

`GradientColorModifier`では0..1のFloatではなくColorRoute上のLuminanceなどを変換するのではなく、基本的には別途`FloatToGradientColorSource`として型変換を提供する。

Color ModifierはColorの同型変換だけを担当する。

---

# 18. Trigger Modifier

TriggerにもPipelineを持たせる。

| Modifier | 内容 |
| --- | --- |
| `CooldownTriggerModifier` | 一定時間再発火禁止 |
| `DelayTriggerModifier` | 遅延 |
| `EveryNthTriggerModifier` | N回に1回 |
| `RandomChanceTriggerModifier` | 発火確率 |
| `GateTriggerModifier` | boolがtrueの時だけ通す |
| `QuantizeTriggerModifier` | 次の拍/Gridまで待つ |
| `DebounceTriggerModifier` | 短時間の連続入力をまとめる |

Quantizeでは、

```text
Button Press
 ↓
Quantize: Next Beat
 ↓
Camera Change
```

を構成できる。

Gridは、

```text
1 Bar
1 Beat
1/2
1/4
1/8
1/16
```

に加えて、

```text
1/2T
1/4T
1/8T
1/16T
```

などの三連系も対応する。

---

# 19. Musical Transport

Tempo関連は、

```text
MusicalTransport
```

へ統合する。

保持する状態は、

```text
Base BPM
BPM Multiplier
Effective BPM

Beat Position
Beat Index
Bar Index

Beat Phase
Bar Phase

Time Signature
```

とする。

時間計算ではBeatごとに開始時刻を更新する方式を使用しない。

内部ではdouble精度のClockを使用する。

基本式は、

```text
beatPosition =
(currentTime - epochTime)
* effectiveBpm
/ 60
```

とする。

そこから、

```text
BeatIndex = floor(beatPosition)

BeatPhase =
beatPosition % 1
```

を計算する。

BPM変更時には現在のBeat Positionを保持するようepochを再計算し、不連続なPhase Jumpを発生させない。

---

# 20. Transport Clock Source

Transportの基準時刻は切り替え可能にする。

```text
Unity Unscaled Time
Audio DSP Time
External MIDI Clock
External OSC Sync
```

通常はAudio DSP Timeを優先する。

外部Clock使用時には、BPMだけでなくPhase同期も扱える構造にする。

---

# 21. Tap Tempo

Tap TempoはTransport内部機能として持つ。

設定項目は、

```text
Sample Count
Minimum BPM
Maximum BPM
Reset Timeout
Outlier Rejection
```

とする。

単純に最後の2Tapだけから計算せず、複数Tap間隔のMedianまたはTrimmed Meanを利用して揺れを抑える。

一定時間Tapされなければ履歴を破棄する。

---

# 22. Transport Source

Transportは通常のSourceとしてRouteから利用する。

```text
TransportBpmFloatSource
TransportBaseBpmFloatSource
TransportMultiplierFloatSource

BeatPhaseFloatSource
BarPhaseFloatSource
SubdivisionPhaseFloatSource

BeatIndexIntSource
BarIndexIntSource

BeatTriggerSource
BarTriggerSource
SubdivisionTriggerSource
```

例えば、

```text
Beat Phase
 ↓
Curve
 ↓
VFX
```

という通常のRouteを構成できる。

Beat専用のAutomationシステムは導入しない。

---

# 23. Tempo操作Output

```text
TransportTapTriggerOutput
TransportResetPhaseTriggerOutput
TransportBpmFloatOutput
TransportMultiplierFloatOutput
TransportNudgeForwardTriggerOutput
TransportNudgeBackwardTriggerOutput
```

を用意する。

例えば、

```text
APC Tap Button
 ↓
TransportTapTriggerOutput
```

となる。

BPM倍率も、

```text
APC Button Group
 ↓
IndexToFloat
   [0.5, 1, 2]
 ↓
TransportMultiplierFloatOutput
```

だけで構成できる。

---

# 24. MIDI Clock

MIDI Clock受信に対応する。

対象メッセージは、

```text
Clock
Start
Continue
Stop
Song Position Pointer
```

とする。

受信したMIDI Clockから、

```text
Tempo
Beat Phase
Transport Running State
```

を復元する。

内部Transportとの同期方式として、

```text
Internal
External MIDI
External OSC
```

を切り替えられるようにする。

MIDI Clock送信にも対応し、MondmadeVJ0をMaster Clockとして外部機器を同期できるようにする。

---

# 25. 共通Frame Clock

`SmoothDamp`やAudio Sourceごとに、

```csharp
Observable.EveryUpdate()
```

を生成しない。

Runtime側に、

```text
FrameClock
```

を1つ置く。

```csharp
public readonly struct FrameTime
{
    public double time;
    public double unscaledTime;
    public double dspTime;

    public float deltaTime;
    public float unscaledDeltaTime;
}
```

を毎フレーム配信する。

Smooth、Envelope、Audio、Transport Phaseなど、フレーム更新が必要なNodeはすべてこれを参照する。

---

# 26. Audio Source

Lasp Integrationとして次を実装する。

```text
LaspLevelFloatSource
LaspBandFloatSource
LaspPeakFloatSource
LaspSpectrumBinFloatSource
```

Bandには、

```text
Low
Mid
High
Custom
```

を用意する。

Customでは周波数帯域をHzで指定する。

Audio Triggerとして、

```text
AudioThresholdTriggerSource
AudioPeakTriggerSource
```

を用意する。

設定項目は、

```text
Threshold
Hysteresis
Cooldown
```

とする。

Audio系SourceにもAttack / Release smoothingを内蔵するのではなく、必要に応じてModifierを接続する。

---

# 27. OSC

OSC Jack Integrationとして、

```text
OscFloatSource
OscIntSource
OscBoolSource
OscVector2Source
OscVector3Source
OscVector4Source
OscColorSource
OscTriggerSource
```

を用意する。

Outputも、

```text
OscFloatOutput
OscIntOutput
OscBoolOutput
OscVector2Output
OscVector3Output
OscVector4Output
OscColorOutput
OscTriggerOutput
```

を用意する。

Unity同士、TouchDesigner、別PCのVJシステムなどと連携できる。

---

# 28. Core Output

```text
DebugFloatOutput
DebugIntOutput
DebugBoolOutput
DebugVectorOutput
DebugColorOutput
DebugTriggerOutput

SignalFloatOutput
SignalIntOutput
SignalBoolOutput
SignalVector2Output
SignalVector3Output
SignalVector4Output
SignalColorOutput
SignalTriggerOutput
```

を用意する。

DebugOutputは開発時だけでなくRuntime Monitorにも表示できる。

---

# 29. Unity Object Output

```text
GameObjectActiveBoolOutput
BehaviourEnabledBoolOutput
RendererEnabledBoolOutput

ObjectGroupIndexIntOutput

TransformPositionVector3Output
TransformRotationVector3Output
TransformScaleVector3Output

TransformPositionAxisFloatOutput
TransformRotationAxisFloatOutput
TransformScaleAxisFloatOutput
```

を用意する。

`ObjectGroupIndexIntOutput`は、

```text
0 → Object AだけON
1 → Object BだけON
2 → Object CだけON
```

という切替を実現する。

---

# 30. Animator Output

```text
AnimatorFloatOutput
AnimatorIntOutput
AnimatorBoolOutput
AnimatorTriggerOutput
AnimatorLayerWeightFloatOutput
AnimatorStateTriggerOutput
```

を用意する。

Parameter名はEditor上でAnimator Controllerから選択する。

文字列の手入力はFallbackとしてのみ許可する。

---

# 31. VFX Graph Output

VFX Graphは主要Integrationとする。

```text
VFXFloatOutput
VFXIntOutput
VFXUIntOutput
VFXBoolOutput

VFXVector2Output
VFXVector3Output
VFXVector4Output

VFXColorOutput
VFXMatrixOutput

VFXTextureOutput
VFXTextureIndexOutput

VFXTriggerEventOutput
```

InspectorではVisualEffectを設定すると、そのAssetのExposed Propertiesから型一致するものだけを候補表示する。

```text
Property:
▼ EmissionGain
  Speed
  Scale
  Thickness
```

文字列直接入力もFallbackとして残す。

---

# 32. Material / Shader Output

Rendererに対しては原則として`MaterialPropertyBlock`を使用する。

```text
RendererFloatOutput
RendererIntOutput
RendererVectorOutput
RendererColorOutput
RendererTextureOutput
RendererTextureIndexOutput
```

Material Asset自体を変更する必要がある場合には、

```text
MaterialFloatOutput
MaterialIntOutput
MaterialVectorOutput
MaterialColorOutput
MaterialTextureOutput
```

を使用する。

Shader PropertyはEditorからDropdownで選択する。

---

# 33. Volume Output

Volume用には、

```text
VolumeFloatOutput
VolumeIntOutput
VolumeBoolOutput
VolumeColorOutput
VolumeVectorOutput
```

を用意する。

Inspectorでは、

```text
Volume
 ↓
Component
   ColorAdjustments
 ↓
Parameter
   postExposure
```

のように選択する。

Runtimeでは初期化時に参照とAccessorを解決してCacheする。

毎フレームReflection検索は行わない。

Override Stateについても、

```text
Always Override
Follow Existing Override
Set Override Automatically
```

から選択できるようにする。

---

# 34. Cinemachine Output

```text
CinemachineCameraIndexOutput
CinemachineFovFloatOutput
CinemachineDutchFloatOutput
CinemachinePriorityIntOutput
```

を用意する。

CameraIndexでは、

```text
Camera 0
Camera 1
Camera 2
...
```

から1つを選択する。

Camera切替方式は、

```text
Priority
GameObject Active
```

から選択できるようにする。

---

# 35. VideoPlayer Output

```text
VideoPlayerSpeedFloatOutput
VideoPlayerPlayBoolOutput
VideoPlayerTimeFloatOutput
VideoPlayerNormalizedTimeFloatOutput
VideoPlayerClipIndexIntOutput

VideoPlayerPlayTriggerOutput
VideoPlayerPauseTriggerOutput
VideoPlayerStopTriggerOutput
VideoPlayerRestartTriggerOutput
```

を用意する。

Clip Index変更時の挙動として、

```text
Keep State
Auto Play
Restart
```

を選択可能にする。

---

# 36. AudioSource Output

```text
AudioSourceVolumeFloatOutput
AudioSourcePitchFloatOutput
AudioSourcePanFloatOutput
AudioSourcePlayBoolOutput
AudioSourceTimeFloatOutput
AudioSourceClipIndexIntOutput

AudioSourcePlayTriggerOutput
AudioSourcePauseTriggerOutput
AudioSourceStopTriggerOutput
AudioSourceRestartTriggerOutput
```

を用意する。

---

# 37. MIDI Surface

MIDI SurfaceはRouteとは独立したサブシステムにする。

```text
MidiHub
   ↓
MidiSurface
   ↓
MidiSurfaceProfile
   ↓
Logical Controls
   ↓
Route Source / Output
```

Routeは、

```text
CC 48
Note 56
```

を直接知らない。

---

# 38. MidiSurfaceProfile

MidiSurfaceProfileはScriptableObjectとして保存する。

例えばAPC mini mk2なら、

```text
APC mini mk2

Grid
  Pad[0,0]
  Pad[1,0]
  ...
  Pad[7,7]

Scene
  Scene[0]
  ...

Faders
  Fader[0]
  ...
  Fader[8]

Shift
```

という物理Surfaceとして定義する。

各Controlには、

```text
Stable ID
Display Name
Group
Control Type

MIDI Input Address

MIDI Feedback Address
Feedback Capability

Editor Layout Position
```

を持たせる。

例えば、

```text
ID:
grid/3/5

Type:
Button

Input:
Note 35

Feedback:
RGB Pad
```

となる。

---

# 39. MIDI Control Type

Surface Controlには明示的なControl Typeを持たせる。

```text
Button
Velocity Button
Absolute Fader
Absolute Knob
Relative Encoder
XY Control
Pitch Bend
Aftertouch
```

これによってSource選択時に互換性のないControlを候補から除外できる。

---

# 40. Device Driver

ProfileとProtocolは分離する。

```text
MidiSurfaceProfile
    物理レイアウト

IMidiSurfaceDriver
    MIDI Protocol
```

標準Driverとして、

```text
GenericMidiDriver
ApcMiniMk2Driver
```

を実装する。

APC mini mk2固有の、

```text
RGB Palette
Brightness
Blink
Pulse
Mono LED
```

などはDriver側が知る。

上位層は、

```text
Note velocity 21
```

のような機種固有値を知らない。

---

# 41. LED Style

上位層ではLEDを次のStyleとして扱う。

```csharp
public struct MidiLedStyle
{
    public Color color;
    public float brightness;
    public MidiLedAnimation animation;
}
```

Animationは、

```text
Off
Solid
Blink
Pulse
```

とする。

APC DriverがColorを対応Paletteの最も近い色へ変換する。

Mono LEDの場合はColor情報をCapabilityに応じて縮退させる。

---

# 42. MIDI Feedback Output

Route StateをLEDへ戻す。

```text
MidiLedBoolOutput
MidiLedIndexedIntOutput
MidiLedValueFloatOutput
MidiLedColorOutput
MidiLedTriggerFlashOutput
```

例えばToggleは、

```text
Midi Button
 ↓
ToggleBoolSource
 ↓
Bool Route
 ├→ VFXBoolOutput
 └→ MidiLedBoolOutput
```

となる。

これにより、

> ボタンを押したからLEDが変わる

のではなく、

> Bool Stateが変わったからLEDが変わる

という構造になる。

# 43. Indexed LED

Camera Selectionでは、

```text
MidiButtonGroupIndexSource
 ↓
Int Route
 ├→ CinemachineCameraIndexOutput
 └→ MidiLedIndexedIntOutput
```

となる。

LED Outputには、

```text
Index 0 → Pad A
Index 1 → Pad B
Index 2 → Pad C
```

を設定する。

現在のCameraとLEDは常に同期する。

# 44. LED Layer

Feedback Manager内部ではレイヤー式にする。

標準Layerは、

```text
Base
State
Beat
Interaction
Alert
```

とする。

各LayerはPriorityを持つ。

例えば、

```text
State:
Green

Interaction:
White / 100ms
```

なら、ボタンを押した瞬間だけ白くなり、100ms後には自動的にStateの緑へ戻る。

元のLED状態を保存して復元する処理は不要になる。

任意の追加Layerも登録可能にする。

# 45. Beat LED Feedback

TransportとLED Feedbackを直接結合せず、通常のRouteとして構成する。

例えば、

```text
Beat Trigger
 ↓
MidiLedTriggerFlashOutput
```

あるいは、

```text
Beat Phase
 ↓
Curve
 ↓
MidiLedValueFloatOutput
```

とする。

これによってBeat点滅も通常の制御系として扱える。

# 46. MIDI Output最適化

各Controlについて、

```text
Desired LED State
Last Sent LED State
```

を保持する。

同じ状態ならMIDI OUTしない。

```csharp
if (desired == lastSent)
    skip;
```

接続直後にはFull Resyncを行う。

USB抜き差し後も、

```text
Reconnect
→ Full LED State Send
```

を実行する。

大量更新時にはフレーム内でOutput Queueをまとめ、同一Controlへの複数更新は最後の状態だけを送信する。

# 47. MIDI接続

MidiHubがPortを管理する。

Surfaceには、

```text
Input Device Match
Output Port Match
```

を設定する。

一致方式は、

```text
Exact
Contains
Regex
Manual
```

から選べるようにする。

接続状態は、

```text
Disconnected
Connecting
Connected
Error
```

で保持する。

実行中に切断されてもRoute自体は停止しない。

再接続時にInput購読とLED状態を自動復元する。

# 48. 複数MIDI Surface

複数のMIDI Controllerを同時に使用できるようにする。

```text
MidiHub
 ├ APC mini mk2
 ├ Launch Control
 └ Generic MIDI Device
```

各Source / OutputはSurface IDとControl IDの組み合わせで対象を指定する。

同一Profileの機器を複数台使用する場合も、Surface Instanceを別々に持てるようにする。

# 49. Editor MIDI Learn

**Runtime MIDI Learnは導入しない。**

Editor MIDI Learnは標準機能とする。

Inspectorで、

```text
Control:
[ Learn ]
```

を押してMIDI Controllerを操作すると、該当するLogical Controlを自動選択する。

例えば、

```text
Grid / 3 / 5
```

が設定される。

FaderやKnobにも対応する。

# 50. Generic Property Output

専用Integrationで対応していないComponent用に、

```text
ComponentMemberFloatOutput
ComponentMemberIntOutput
ComponentMemberBoolOutput
ComponentMemberVector2Output
ComponentMemberVector3Output
ComponentMemberVector4Output
ComponentMemberColorOutput
```

をFallbackとして用意する。

Editorで、

```text
Component
 ↓
public field / property
```

を選択する。

Reflectionは初期化時に1回だけ解決してAccessorをCacheする。

毎フレームReflectionは行わない。

頻繁に利用するUnity機能には専用Outputを優先する。

# 51. Runtime Root

Sceneには基本的に、

```text
MondmadeVJ0Runtime
```

を1つ置く。

内部には、

```text
FrameClock
MusicalTransport
MidiHub
Service Registry
```

を持つ。

RouteはStatic Singletonを直接検索しない。

`RouteContext`経由でServiceへアクセスする。

```csharp
public sealed class RouteContext
{
    public FrameClock FrameClock { get; }
    public MusicalTransport Transport { get; }

    public T GetService<T>();
}
```

OSC、Lasp、MIDIなどもServiceとして登録する。

複数の独立したMondmadeVJ0 Runtimeを同じSceneに配置できる設計とする。

# 52. Lifecycle

```text
Awake
  Service初期化

Start
  Route Bind

Update
  FrameClock
  MIDI / OSC Queue処理

OnDisable / Destroy
  Route Dispose
  Subscription解除
```

Runtime中に1つのRouteが壊れても、他のRouteは継続する。

Bind失敗時は、

```text
[CameraSelect]
VFX target is missing.
```

のようにRoute名付きでWarningを出し、そのRouteだけを無効にする。

ライブ中にNullReferenceで全制御系が停止する構造にはしない。

# 53. Validation

Play Modeへ入る前にRouteを検査する。

検査対象は、

```text
Source未設定
Output未設定
Null Target
存在しないVFX Property
型不一致
存在しないAnimator Parameter
存在しないShader Property
存在しないMIDI Control
重複Stable ID
接続不能なComposite Source
循環Signal参照
無効なRange
```

などとする。

Inspector一覧にも、

```text
✓ Valid
⚠ Warning
✕ Error
```

を表示する。

可能な問題はライブ前にEditor上で検出する。

# 54. Inspector

通常時にはツリーをすべて展開しない。

例えば一覧では、

```text
✓ Camera Select
  MIDI Grid[0..3]
  → Int
  → Cinemachine + APC LED

✓ Emission
  Fader 2
  → Remap → Smooth
  → VFX EmissionGain

✓ Beat Flash
  Beat Phase
  → Curve
  → VFX Intensity
```

程度に要約する。

展開すると詳細編集できる。

Source、Modifier、Output追加時には、現在のRoute型に対応するものだけを表示する。

検索にも対応する。

```text
Add Source
> midi
```

と入力すればMIDI系Sourceだけを表示する。

# 55. Routeの複製・並べ替え

VJ制作では類似Routeを大量に作るため、

```text
Duplicate Route
Copy
Paste
Move Up
Move Down
Disable
Delete
```

をInspector上から行えるようにする。

Source、Modifier、Output単位でもCopy/Paste可能にする。

# 56. Runtime Monitor

デバッグ用Windowを用意する。

```text
TRANSPORT
BPM       174
Beat      125
Phase     0.63
Bar       31

MIDI
APC mini mk2   Connected

ROUTES
Emission       3.72
CameraIndex    2
Invert         true
BeatFlash      0.34
```

各Routeについて、

```text
Source Raw Value
Modifierごとの中間値
Final Value
Output状態
```

まで確認できるようにする。

通常表示ではFinal Valueだけを出し、展開時に中間値を表示する。

# 57. MIDI Monitor

MIDI Monitorでは、

```text
IN
APC mini mk2
Grid 3,5
Note On
Velocity 127

OUT
APC mini mk2
Grid 3,5
Green
Solid
100%
```

まで確認できるようにする。

Raw MIDI表示モードでは、

```text
Channel
Note / CC
Value
```

も確認可能にする。

# 58. Transport Monitor

Transport Monitorでは、

```text
Clock Source
BPM
Effective BPM
Beat Position
Beat Phase
Bar
External Clock Status
MIDI Clock Stability
```

を表示する。

Tap Tempo履歴も確認できる。

# 59. Performance Monitor

MondmadeVJ0内の負荷を確認できるようにする。

```text
Active Routes
Active Sources
Frame-updated Nodes

MIDI IN / sec
MIDI OUT / sec
OSC IN / sec

Route Processing Time
```

を計測可能にする。

Development BuildまたはEditorでのみ有効にできるようにし、本番時のオーバーヘッドを抑える。

# 60. Assembly構成

```text
MondmadeVJ0.Core
  R3

MondmadeVJ0.Transport
  Core

MondmadeVJ0.InputSystem
  Core
  Unity Input System

MondmadeVJ0.Midi
  Core
  Minis
  MIDI OUT backend

MondmadeVJ0.Osc
  Core
  OSC Jack

MondmadeVJ0.Audio.Lasp
  Core
  Lasp

MondmadeVJ0.VFXGraph
  Core
  VFX Graph

MondmadeVJ0.Rendering
  Core
  URP / Volume

MondmadeVJ0.Cinemachine
  Core
  Cinemachine

MondmadeVJ0.Video
  Core

MondmadeVJ0.Animator
  Core

MondmadeVJ0.Editor
```

`Core`にはUniTaskやLitMotionを必須依存として入れない。

EnvelopeやSmoothはFrameClockとR3で実装する。

LitMotionと連携するSource / Modifierが必要な場合には、

```text
MondmadeVJ0.LitMotion
```

Integrationとして実装し、Coreとは分離する。

# 61. MondmadeVJ0の完成要件

MondmadeVJ0は、本設計で定義した機能一式を実装した状態を完成形とする。

開発段階による機能区分や「初版」「後日実装」といった分類は設けない。

完成時点で少なくとも以下のSubsystemがすべて利用可能であることを要件とする。

```text
Core Route System
Value / Trigger Type System
Composite Source
Shared Signal

Frame Clock

Musical Transport
Tap Tempo
Subdivision
Quantization
MIDI Clock Input / Output

Unity Input System

MIDI Input
MIDI Surface
APC mini mk2 Driver
Generic MIDI Driver
MIDI LED Feedback
LED Layer
Reconnect / Resync
Editor MIDI Learn

OSC Input / Output

Lasp Audio Input

VFX Graph Output
Material / MPB Output
Volume Output
Cinemachine Output
VideoPlayer Output
AudioSource Output
Animator Output
Transform / GameObject Output
Generic Component Output

Editor Inspector
Validation
Runtime Monitor
MIDI Monitor
Transport Monitor
Performance Monitor
```

これらは「必要になったらプロジェクト側で実装する」機能ではなく、MondmadeVJ0側が提供する標準機能とする。

# 62. 標準搭載Source

完成版MondmadeVJ0には、少なくとも以下を標準搭載する。

### 基本

```text
ConstantFloatSource
ConstantIntSource
ConstantBoolSource
ConstantVector2Source
ConstantVector3Source
ConstantVector4Source
ConstantColorSource

SignalValueSource
SignalTriggerSource
```

### Input System

```text
InputActionFloatSource
InputActionBoolSource
InputActionVector2Source
InputActionVector3Source
InputActionTriggerSource
InputActionReleaseTriggerSource
InputActionLastPressedIndexSource
```

### MIDI

```text
MidiAbsoluteFloatSource
MidiRelativeFloatSource
MidiVelocityFloatSource
MidiButtonBoolSource
MidiButtonGroupIndexSource
MidiButtonTriggerSource
MidiButtonReleaseTriggerSource
```

### Stateful

```text
ToggleBoolSource
SetResetBoolSource
PulseBoolSource

StepIntSource
IncrementDecrementIntSource

TriggerValueFloatSource
TriggerValueIntSource

TriggeredRandomFloatSource
TriggeredRandomIntSource

SampleHoldFloatSource
SampleHoldVectorSource

TriggerEnvelopeFloatSource
```

### Composite

```text
FloatBinarySource
IntBinarySource
BoolLogicSource

FloatCompareBoolSource
IntCompareBoolSource

FloatSelectSource
IntSelectSource
BoolSelectSource
Vector2SelectSource
Vector3SelectSource
Vector4SelectSource
ColorSelectSource

MostRecentFloatSource
MostRecentIntSource
MostRecentBoolSource
MostRecentVectorSource

MergeTriggerSource
AnyTriggerSource
AllTriggerSource
```

### 型変換

```text
IntToFloatSource
BoolToFloatSource
BoolToIntSource
FloatToIntSource
FloatToBoolSource

IndexToFloatSource
IndexToIntSource
IndexToBoolSource
IndexToColorSource

Vector2ToFloatSource
Vector3ToFloatSource
Vector4ToFloatSource

FloatToVector2Source
FloatToVector3Source
FloatToVector4Source
FloatToGradientColorSource
```

### Transport

```text
TransportBpmFloatSource
TransportBaseBpmFloatSource
TransportMultiplierFloatSource

BeatPhaseFloatSource
BarPhaseFloatSource
SubdivisionPhaseFloatSource

BeatIndexIntSource
BarIndexIntSource

BeatTriggerSource
BarTriggerSource
SubdivisionTriggerSource
```

### Audio

```text
LaspLevelFloatSource
LaspBandFloatSource
LaspPeakFloatSource
LaspSpectrumBinFloatSource

AudioThresholdTriggerSource
AudioPeakTriggerSource
```

### OSC

```text
OscFloatSource
OscIntSource
OscBoolSource
OscVector2Source
OscVector3Source
OscVector4Source
OscColorSource
OscTriggerSource
```

# 63. 標準搭載Modifier

### Float

```text
AddFloatModifier
MultiplyFloatModifier
RemapFloatModifier
ClampFloatModifier
Invert01FloatModifier

AbsFloatModifier
SignFloatModifier
PowerFloatModifier

CurveFloatModifier
DeadZoneFloatModifier
QuantizeFloatModifier

FloorFloatModifier
CeilFloatModifier
RoundFloatModifier

RepeatFloatModifier
PingPongFloatModifier

SmoothDampFloatModifier
ExponentialSmoothFloatModifier
AttackReleaseFloatModifier
SlewRateFloatModifier

WaveShapeFloatModifier
```

### Int

```text
AddIntModifier
MultiplyIntModifier
ClampIntModifier
WrapIntModifier
ModuloIntModifier
LookupIntModifier
AbsIntModifier
```

### Bool

```text
NotBoolModifier
```

### Vector

```text
AddVectorModifier
MultiplyVectorScalarModifier
MultiplyVectorComponentsModifier
NormalizeVectorModifier
ClampMagnitudeVectorModifier
SmoothDampVectorModifier
RemapVectorModifier
AbsoluteVectorModifier
```

### Color

```text
MultiplyColorModifier
IntensityColorModifier
AlphaColorModifier
HSVColorModifier
```

### Trigger

```text
CooldownTriggerModifier
DelayTriggerModifier
EveryNthTriggerModifier
RandomChanceTriggerModifier
GateTriggerModifier
QuantizeTriggerModifier
DebounceTriggerModifier
```

# 64. 標準搭載Output

### Core

```text
DebugFloatOutput
DebugIntOutput
DebugBoolOutput
DebugVectorOutput
DebugColorOutput
DebugTriggerOutput

SignalFloatOutput
SignalIntOutput
SignalBoolOutput
SignalVector2Output
SignalVector3Output
SignalVector4Output
SignalColorOutput
SignalTriggerOutput
```

### Unity Object

```text
GameObjectActiveBoolOutput
BehaviourEnabledBoolOutput
RendererEnabledBoolOutput

ObjectGroupIndexIntOutput

TransformPositionVector3Output
TransformRotationVector3Output
TransformScaleVector3Output

TransformPositionAxisFloatOutput
TransformRotationAxisFloatOutput
TransformScaleAxisFloatOutput
```

### Transport

```text
TransportTapTriggerOutput
TransportResetPhaseTriggerOutput
TransportBpmFloatOutput
TransportMultiplierFloatOutput
TransportNudgeForwardTriggerOutput
TransportNudgeBackwardTriggerOutput
```

### MIDI

```text
MidiLedBoolOutput
MidiLedIndexedIntOutput
MidiLedValueFloatOutput
MidiLedColorOutput
MidiLedTriggerFlashOutput
```

### Animator

```text
AnimatorFloatOutput
AnimatorIntOutput
AnimatorBoolOutput
AnimatorTriggerOutput
AnimatorLayerWeightFloatOutput
AnimatorStateTriggerOutput
```

### VFX Graph

```text
VFXFloatOutput
VFXIntOutput
VFXUIntOutput
VFXBoolOutput

VFXVector2Output
VFXVector3Output
VFXVector4Output

VFXColorOutput
VFXMatrixOutput

VFXTextureOutput
VFXTextureIndexOutput

VFXTriggerEventOutput
```

### Renderer / Material

```text
RendererFloatOutput
RendererIntOutput
RendererVectorOutput
RendererColorOutput
RendererTextureOutput
RendererTextureIndexOutput

MaterialFloatOutput
MaterialIntOutput
MaterialVectorOutput
MaterialColorOutput
MaterialTextureOutput
```

### Volume

```text
VolumeFloatOutput
VolumeIntOutput
VolumeBoolOutput
VolumeColorOutput
VolumeVectorOutput
```

### Cinemachine

```text
CinemachineCameraIndexOutput
CinemachineFovFloatOutput
CinemachineDutchFloatOutput
CinemachinePriorityIntOutput
```

### VideoPlayer

```text
VideoPlayerSpeedFloatOutput
VideoPlayerPlayBoolOutput
VideoPlayerTimeFloatOutput
VideoPlayerNormalizedTimeFloatOutput
VideoPlayerClipIndexIntOutput

VideoPlayerPlayTriggerOutput
VideoPlayerPauseTriggerOutput
VideoPlayerStopTriggerOutput
VideoPlayerRestartTriggerOutput
```

### AudioSource

```text
AudioSourceVolumeFloatOutput
AudioSourcePitchFloatOutput
AudioSourcePanFloatOutput
AudioSourcePlayBoolOutput
AudioSourceTimeFloatOutput
AudioSourceClipIndexIntOutput

AudioSourcePlayTriggerOutput
AudioSourcePauseTriggerOutput
AudioSourceStopTriggerOutput
AudioSourceRestartTriggerOutput
```

### OSC

```text
OscFloatOutput
OscIntOutput
OscBoolOutput
OscVector2Output
OscVector3Output
OscVector4Output
OscColorOutput
OscTriggerOutput
```

### Generic Component

```text
ComponentMemberFloatOutput
ComponentMemberIntOutput
ComponentMemberBoolOutput
ComponentMemberVector2Output
ComponentMemberVector3Output
ComponentMemberVector4Output
ComponentMemberColorOutput
```

# 65. 最終的な思想

MondmadeVJ0は、

```text
「何でもできるノードシステム」
```

にはしない。

そうではなく、

```text
「VJ制作で毎回書く制御コードを
Inspector上の短いRouteに置き換える」
```

ためのライブラリにする。

通常は、

```text
Source
↓
Modifier
↓
Outputs
```

だけを見る。

複雑なときだけ、

```text
Composite Source
```

を使う。

共有が必要なときだけ、

```text
Signal
```

を使う。

MIDIについては、

```text
Physical MIDI
↓
Surface
↓
Logical Control
↓
Route
↓
State
↓
Visual + LED Feedback
```

という一方向の責務分離を守る。

Transportについては、

```text
Clock
↓
Musical Transport
↓
Beat / Bar / Phase / Trigger
↓
通常のRoute
```

として扱い、BPM同期専用の別系統を作らない。

この構造により、Vkasaiの分かりやすさとNxPCLabの柔軟性を両立しながら、VJ制作で繰り返し発生する制御処理をMondmadeVJ0へ集約する。

**本設計に記載されたSource、Modifier、Output、Transport、MIDI Surface、Integration、Editor ToolingまでをMondmadeVJ0の標準実装範囲とする。**
