# 標準機能カタログ

更新日：2026-10-23（JST）。[移植元](reference/conversation-design.md)第62〜64章の全項目を収録する。**全項目が完成要件。** Runtime / Editor機能の実装は揃った。各行の「実装済み・検証中」は、EditMode契約テスト、Unityライフサイクル、接続機器、Player上での受け入れ確認が残る状態を示す。検証完了までは機能完成として数えない。

VectorはVector2 / Vector3 / Vector4の具体型へ展開した。SignalValueSourceも`SignalFloatSource`等の具体型として実装した。表はInspectorで選択するC#型名を記載する。名称整理・Gradientの扱い・8 Route型とVFX Matrix / Texture / UIntの接続は[設計補足](design-notes.md)を参照する。表の状態はコードの実装状況を示し、受け入れ条件の完了とは区別する。

## Source

### 基本

| 設計名 | 状態 |
| --- | --- |
| `ConstantFloatSource` | 実装済み・検証中 |
| `ConstantIntSource` | 実装済み・検証中 |
| `ConstantBoolSource` | 実装済み・検証中 |
| `ConstantVector2Source` | 実装済み・検証中 |
| `ConstantVector3Source` | 実装済み・検証中 |
| `ConstantVector4Source` | 実装済み・検証中 |
| `ConstantColorSource` | 実装済み・検証中 |
| `SignalFloatSource` | 実装済み・検証中 |
| `SignalIntSource` | 実装済み・検証中 |
| `SignalBoolSource` | 実装済み・検証中 |
| `SignalVector2Source` | 実装済み・検証中 |
| `SignalVector3Source` | 実装済み・検証中 |
| `SignalVector4Source` | 実装済み・検証中 |
| `SignalColorSource` | 実装済み・検証中 |
| `SignalTriggerSource` | 実装済み・検証中 |

### Input System

| 設計名 | 状態 |
| --- | --- |
| `InputActionFloatSource` | 実装済み・検証中 |
| `InputActionBoolSource` | 実装済み・検証中 |
| `InputActionVector2Source` | 実装済み・検証中 |
| `InputActionVector3Source` | 実装済み・検証中 |
| `InputActionTriggerSource` | 実装済み・検証中 |
| `InputActionReleaseTriggerSource` | 実装済み・検証中 |
| `InputActionLastPressedIndexSource` | 実装済み・検証中 |

### MIDI

| 設計名 | 状態 |
| --- | --- |
| `MidiAbsoluteFloatSource` | 実装済み・検証中 |
| `MidiRelativeFloatSource` | 実装済み・検証中 |
| `MidiVelocityFloatSource` | 実装済み・検証中 |
| `MidiXYVector2Source` | 実装済み・検証中 |
| `MidiButtonBoolSource` | 実装済み・検証中 |
| `MidiButtonGroupIndexSource` | 実装済み・検証中 |
| `MidiButtonTriggerSource` | 実装済み・検証中 |
| `MidiButtonReleaseTriggerSource` | 実装済み・検証中 |

### Stateful

| 設計名 | 状態 |
| --- | --- |
| `ToggleBoolSource` | 実装済み・検証中 |
| `SetResetBoolSource` | 実装済み・検証中 |
| `PulseBoolSource` | 実装済み・検証中 |
| `StepIntSource` | 実装済み・検証中 |
| `IncrementDecrementIntSource` | 実装済み・検証中 |
| `TriggerValueFloatSource` | 実装済み・検証中 |
| `TriggerValueIntSource` | 実装済み・検証中 |
| `TriggeredRandomFloatSource` | 実装済み・検証中 |
| `TriggeredRandomIntSource` | 実装済み・検証中 |
| `SampleHoldFloatSource` | 実装済み・検証中 |
| `SampleHoldVector2Source` | 実装済み・検証中 |
| `SampleHoldVector3Source` | 実装済み・検証中 |
| `SampleHoldVector4Source` | 実装済み・検証中 |
| `TriggerEnvelopeFloatSource` | 実装済み・検証中 |

### Composite

| 設計名 | 状態 |
| --- | --- |
| `FloatBinarySource` | 実装済み・検証中 |
| `IntBinarySource` | 実装済み・検証中 |
| `BoolLogicSource` | 実装済み・検証中 |
| `FloatCompareBoolSource` | 実装済み・検証中 |
| `IntCompareBoolSource` | 実装済み・検証中 |
| `FloatSelectSource` | 実装済み・検証中 |
| `IntSelectSource` | 実装済み・検証中 |
| `BoolSelectSource` | 実装済み・検証中 |
| `Vector2SelectSource` | 実装済み・検証中 |
| `Vector3SelectSource` | 実装済み・検証中 |
| `Vector4SelectSource` | 実装済み・検証中 |
| `ColorSelectSource` | 実装済み・検証中 |
| `MostRecentFloatSource` | 実装済み・検証中 |
| `MostRecentIntSource` | 実装済み・検証中 |
| `MostRecentBoolSource` | 実装済み・検証中 |
| `MostRecentVector2Source` | 実装済み・検証中 |
| `MostRecentVector3Source` | 実装済み・検証中 |
| `MostRecentVector4Source` | 実装済み・検証中 |
| `MergeTriggerSource` | 実装済み・検証中 |
| `AnyTriggerSource` | 実装済み・検証中 |
| `AllTriggerSource` | 実装済み・検証中 |

### 型変換

| 設計名 | 状態 |
| --- | --- |
| `IntToFloatSource` | 実装済み・検証中 |
| `BoolToFloatSource` | 実装済み・検証中 |
| `BoolToIntSource` | 実装済み・検証中 |
| `FloatToIntSource` | 実装済み・検証中 |
| `FloatToBoolSource` | 実装済み・検証中 |
| `IndexToFloatSource` | 実装済み・検証中 |
| `IndexToIntSource` | 実装済み・検証中 |
| `IndexToBoolSource` | 実装済み・検証中 |
| `IndexToColorSource` | 実装済み・検証中 |
| `Vector2ToFloatSource` | 実装済み・検証中 |
| `Vector3ToFloatSource` | 実装済み・検証中 |
| `Vector4ToFloatSource` | 実装済み・検証中 |
| `FloatToVector2Source` | 実装済み・検証中 |
| `FloatToVector3Source` | 実装済み・検証中 |
| `FloatToVector4Source` | 実装済み・検証中 |
| `FloatToGradientColorSource` | 実装済み・検証中 |

### Transport

| 設計名 | 状態 |
| --- | --- |
| `TransportBpmFloatSource` | 実装済み・検証中 |
| `TransportBaseBpmFloatSource` | 実装済み・検証中 |
| `TransportMultiplierFloatSource` | 実装済み・検証中 |
| `BeatPhaseFloatSource` | 実装済み・検証中 |
| `BarPhaseFloatSource` | 実装済み・検証中 |
| `SubdivisionPhaseFloatSource` | 実装済み・検証中 |
| `BeatIndexIntSource` | 実装済み・検証中 |
| `BarIndexIntSource` | 実装済み・検証中 |
| `BeatTriggerSource` | 実装済み・検証中 |
| `BarTriggerSource` | 実装済み・検証中 |
| `SubdivisionTriggerSource` | 実装済み・検証中 |

### Audio

| 設計名 | 状態 |
| --- | --- |
| `LaspLevelFloatSource` | 実装済み・検証中 |
| `LaspBandFloatSource` | 実装済み・検証中 |
| `LaspPeakFloatSource` | 実装済み・検証中 |
| `LaspSpectrumBinFloatSource` | 実装済み・検証中 |
| `AudioThresholdTriggerSource` | 実装済み・検証中 |
| `AudioPeakTriggerSource` | 実装済み・検証中 |

### OSC

| 設計名 | 状態 |
| --- | --- |
| `OscFloatSource` | 実装済み・検証中 |
| `OscIntSource` | 実装済み・検証中 |
| `OscBoolSource` | 実装済み・検証中 |
| `OscVector2Source` | 実装済み・検証中 |
| `OscVector3Source` | 実装済み・検証中 |
| `OscVector4Source` | 実装済み・検証中 |
| `OscColorSource` | 実装済み・検証中 |
| `OscTriggerSource` | 実装済み・検証中 |

## Modifier

### Float

| 設計名 | 状態 |
| --- | --- |
| `AddFloatModifier` | 実装済み・検証中 |
| `MultiplyFloatModifier` | 実装済み・検証中 |
| `RemapFloatModifier` | 実装済み・検証中 |
| `ClampFloatModifier` | 実装済み・検証中 |
| `Invert01FloatModifier` | 実装済み・検証中 |
| `AbsFloatModifier` | 実装済み・検証中 |
| `SignFloatModifier` | 実装済み・検証中 |
| `PowerFloatModifier` | 実装済み・検証中 |
| `CurveFloatModifier` | 実装済み・検証中 |
| `DeadZoneFloatModifier` | 実装済み・検証中 |
| `QuantizeFloatModifier` | 実装済み・検証中 |
| `FloorFloatModifier` | 実装済み・検証中 |
| `CeilFloatModifier` | 実装済み・検証中 |
| `RoundFloatModifier` | 実装済み・検証中 |
| `RepeatFloatModifier` | 実装済み・検証中 |
| `PingPongFloatModifier` | 実装済み・検証中 |
| `SmoothDampFloatModifier` | 実装済み・検証中 |
| `ExponentialSmoothFloatModifier` | 実装済み・検証中 |
| `AttackReleaseFloatModifier` | 実装済み・検証中 |
| `SlewRateFloatModifier` | 実装済み・検証中 |
| `WaveShapeFloatModifier` | 実装済み・検証中 |

### Int

| 設計名 | 状態 |
| --- | --- |
| `AddIntModifier` | 実装済み・検証中 |
| `MultiplyIntModifier` | 実装済み・検証中 |
| `ClampIntModifier` | 実装済み・検証中 |
| `WrapIntModifier` | 実装済み・検証中 |
| `ModuloIntModifier` | 実装済み・検証中 |
| `LookupIntModifier` | 実装済み・検証中 |
| `AbsIntModifier` | 実装済み・検証中 |

### Bool

| 設計名 | 状態 |
| --- | --- |
| `NotBoolModifier` | 実装済み・検証中 |

### Vector

| 設計名 | 状態 |
| --- | --- |
| `AddVector2Modifier` | 実装済み・検証中 |
| `AddVector3Modifier` | 実装済み・検証中 |
| `AddVector4Modifier` | 実装済み・検証中 |
| `MultiplyVector2Modifier` | 実装済み・検証中 |
| `MultiplyVector3Modifier` | 実装済み・検証中 |
| `MultiplyVector4Modifier` | 実装済み・検証中 |
| `NormalizeVectorModifier` | 実装済み・検証中 |
| `ClampMagnitudeVectorModifier` | 実装済み・検証中 |
| `SmoothDampVectorModifier` | 実装済み・検証中 |
| `RemapVectorModifier` | 実装済み・検証中 |
| `AbsoluteVectorModifier` | 実装済み・検証中 |

### Color

| 設計名 | 状態 |
| --- | --- |
| `MultiplyColorModifier` | 実装済み・検証中 |
| `IntensityColorModifier` | 実装済み・検証中 |
| `AlphaColorModifier` | 実装済み・検証中 |
| `HSVColorModifier` | 実装済み・検証中 |

### Trigger

| 設計名 | 状態 |
| --- | --- |
| `CooldownTriggerModifier` | 実装済み・検証中 |
| `DelayTriggerModifier` | 実装済み・検証中 |
| `EveryNthTriggerModifier` | 実装済み・検証中 |
| `RandomChanceTriggerModifier` | 実装済み・検証中 |
| `GateTriggerModifier` | 実装済み・検証中 |
| `QuantizeTriggerModifier` | 実装済み・検証中 |
| `DebounceTriggerModifier` | 実装済み・検証中 |

## Output

### Core

| 設計名 | 状態 |
| --- | --- |
| `DebugFloatOutput` | 実装済み・検証中 |
| `DebugIntOutput` | 実装済み・検証中 |
| `DebugBoolOutput` | 実装済み・検証中 |
| `DebugVector2Output` | 実装済み・検証中 |
| `DebugVector3Output` | 実装済み・検証中 |
| `DebugVector4Output` | 実装済み・検証中 |
| `DebugColorOutput` | 実装済み・検証中 |
| `DebugTriggerOutput` | 実装済み・検証中 |
| `SignalFloatOutput` | 実装済み・検証中 |
| `SignalIntOutput` | 実装済み・検証中 |
| `SignalBoolOutput` | 実装済み・検証中 |
| `SignalVector2Output` | 実装済み・検証中 |
| `SignalVector3Output` | 実装済み・検証中 |
| `SignalVector4Output` | 実装済み・検証中 |
| `SignalColorOutput` | 実装済み・検証中 |
| `SignalTriggerOutput` | 実装済み・検証中 |

### Unity Object

| 設計名 | 状態 |
| --- | --- |
| `GameObjectActiveBoolOutput` | 実装済み・検証中 |
| `BehaviourEnabledBoolOutput` | 実装済み・検証中 |
| `RendererEnabledBoolOutput` | 実装済み・検証中 |
| `ObjectGroupIndexIntOutput` | 実装済み・検証中 |
| `TransformPositionVector3Output` | 実装済み・検証中 |
| `TransformRotationVector3Output` | 実装済み・検証中 |
| `TransformScaleVector3Output` | 実装済み・検証中 |
| `TransformPositionAxisFloatOutput` | 実装済み・検証中 |
| `TransformRotationAxisFloatOutput` | 実装済み・検証中 |
| `TransformScaleAxisFloatOutput` | 実装済み・検証中 |

### Transport

| 設計名 | 状態 |
| --- | --- |
| `TransportTapTriggerOutput` | 実装済み・検証中 |
| `TransportResetPhaseTriggerOutput` | 実装済み・検証中 |
| `TransportBpmFloatOutput` | 実装済み・検証中 |
| `TransportMultiplierFloatOutput` | 実装済み・検証中 |
| `TransportNudgeForwardTriggerOutput` | 実装済み・検証中 |
| `TransportNudgeBackwardTriggerOutput` | 実装済み・検証中 |

### MIDI

| 設計名 | 状態 |
| --- | --- |
| `MidiLedBoolOutput` | 実装済み・検証中 |
| `MidiLedIndexedIntOutput` | 実装済み・検証中 |
| `MidiLedValueFloatOutput` | 実装済み・検証中 |
| `MidiLedColorOutput` | 実装済み・検証中 |
| `MidiLedTriggerFlashOutput` | 実装済み・検証中 |

### Animator

| 設計名 | 状態 |
| --- | --- |
| `AnimatorFloatOutput` | 実装済み・検証中 |
| `AnimatorIntOutput` | 実装済み・検証中 |
| `AnimatorBoolOutput` | 実装済み・検証中 |
| `AnimatorTriggerOutput` | 実装済み・検証中 |
| `AnimatorLayerWeightFloatOutput` | 実装済み・検証中 |
| `AnimatorStateTriggerOutput` | 実装済み・検証中 |

### VFX Graph

| 設計名 | 状態 |
| --- | --- |
| `VFXFloatOutput` | 実装済み・検証中 |
| `VFXIntOutput` | 実装済み・検証中 |
| `VFXUIntOutput` | 実装済み・検証中 |
| `VFXBoolOutput` | 実装済み・検証中 |
| `VFXVector2Output` | 実装済み・検証中 |
| `VFXVector3Output` | 実装済み・検証中 |
| `VFXVector4Output` | 実装済み・検証中 |
| `VFXColorOutput` | 実装済み・検証中 |
| `VFXMatrixOutput` | 実装済み・検証中 |
| `VFXTextureOutput` | 実装済み・検証中 |
| `VFXTextureIndexOutput` | 実装済み・検証中 |
| `VFXTriggerEventOutput` | 実装済み・検証中 |

### Renderer / Material

| 設計名 | 状態 |
| --- | --- |
| `RendererFloatOutput` | 実装済み・検証中 |
| `RendererIntOutput` | 実装済み・検証中 |
| `RendererVectorOutput` | 実装済み・検証中 |
| `RendererColorOutput` | 実装済み・検証中 |
| `RendererTextureOutput` | 実装済み・検証中 |
| `RendererTextureIndexOutput` | 実装済み・検証中 |
| `MaterialFloatOutput` | 実装済み・検証中 |
| `MaterialIntOutput` | 実装済み・検証中 |
| `MaterialVectorOutput` | 実装済み・検証中 |
| `MaterialColorOutput` | 実装済み・検証中 |
| `MaterialTextureOutput` | 実装済み・検証中 |

### Volume

| 設計名 | 状態 |
| --- | --- |
| `VolumeFloatOutput` | 実装済み・検証中 |
| `VolumeIntOutput` | 実装済み・検証中 |
| `VolumeBoolOutput` | 実装済み・検証中 |
| `VolumeColorOutput` | 実装済み・検証中 |
| `VolumeVectorOutput` | 実装済み・検証中 |

### Cinemachine

| 設計名 | 状態 |
| --- | --- |
| `CinemachineCameraIndexOutput` | 実装済み・検証中 |
| `CinemachineFovFloatOutput` | 実装済み・検証中 |
| `CinemachineDutchFloatOutput` | 実装済み・検証中 |
| `CinemachinePriorityIntOutput` | 実装済み・検証中 |

### VideoPlayer

| 設計名 | 状態 |
| --- | --- |
| `VideoPlayerSpeedFloatOutput` | 実装済み・検証中 |
| `VideoPlayerPlayBoolOutput` | 実装済み・検証中 |
| `VideoPlayerTimeFloatOutput` | 実装済み・検証中 |
| `VideoPlayerNormalizedTimeFloatOutput` | 実装済み・検証中 |
| `VideoPlayerClipIndexIntOutput` | 実装済み・検証中 |
| `VideoPlayerPlayTriggerOutput` | 実装済み・検証中 |
| `VideoPlayerPauseTriggerOutput` | 実装済み・検証中 |
| `VideoPlayerStopTriggerOutput` | 実装済み・検証中 |
| `VideoPlayerRestartTriggerOutput` | 実装済み・検証中 |

### AudioSource

| 設計名 | 状態 |
| --- | --- |
| `AudioSourceVolumeFloatOutput` | 実装済み・検証中 |
| `AudioSourcePitchFloatOutput` | 実装済み・検証中 |
| `AudioSourcePanFloatOutput` | 実装済み・検証中 |
| `AudioSourcePlayBoolOutput` | 実装済み・検証中 |
| `AudioSourceTimeFloatOutput` | 実装済み・検証中 |
| `AudioSourceClipIndexIntOutput` | 実装済み・検証中 |
| `AudioSourcePlayTriggerOutput` | 実装済み・検証中 |
| `AudioSourcePauseTriggerOutput` | 実装済み・検証中 |
| `AudioSourceStopTriggerOutput` | 実装済み・検証中 |
| `AudioSourceRestartTriggerOutput` | 実装済み・検証中 |

### OSC

| 設計名 | 状態 |
| --- | --- |
| `OscFloatOutput` | 実装済み・検証中 |
| `OscIntOutput` | 実装済み・検証中 |
| `OscBoolOutput` | 実装済み・検証中 |
| `OscVector2Output` | 実装済み・検証中 |
| `OscVector3Output` | 実装済み・検証中 |
| `OscVector4Output` | 実装済み・検証中 |
| `OscColorOutput` | 実装済み・検証中 |
| `OscTriggerOutput` | 実装済み・検証中 |

### Generic Component

| 設計名 | 状態 |
| --- | --- |
| `ComponentMemberFloatOutput` | 実装済み・検証中 |
| `ComponentMemberIntOutput` | 実装済み・検証中 |
| `ComponentMemberBoolOutput` | 実装済み・検証中 |
| `ComponentMemberVector2Output` | 実装済み・検証中 |
| `ComponentMemberVector3Output` | 実装済み・検証中 |
| `ComponentMemberVector4Output` | 実装済み・検証中 |
| `ComponentMemberColorOutput` | 実装済み・検証中 |

## 集計の読み方

第62〜64章の設計一覧はSource 95項目、Modifier 48項目、Output 107項目。VectorやSignalのファミリーを具体型へ展開すると本表はSource 105クラス、Modifier 51クラス、Output 109クラスになる（計265クラス名）。Subsystem、Profile、Driver、Inspector、Monitor等は本表に含めず、[実装・検証チェックリスト](implementation-plan.md)で追跡する。本表の全クラス名がRuntime実装に存在することを照合した。
