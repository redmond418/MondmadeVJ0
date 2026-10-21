# 標準機能カタログ

更新日：2026-10-22（JST）。[移植元](reference/conversation-design.md)第62〜64章の全項目を収録する。**すべて完成要件であり、現在は全項目未実装。** ここでの名前は設計名であり、実在するC#クラスを意味しない。

VectorはVector2 / Vector3 / Vector4のファミリー略記。SignalValueSourceも各Value型の具体型へ展開する。名称整理・Gradientの扱い・8 Route型とVFX Matrix / Texture / UIntの接続は[設計補足](design-notes.md)を参照する。機能を実装して検証したときだけ状態を更新する。

## Source

### 基本

| 設計名 | 状態 |
| --- | --- |
| `ConstantFloatSource` | 未実装 |
| `ConstantIntSource` | 未実装 |
| `ConstantBoolSource` | 未実装 |
| `ConstantVector2Source` | 未実装 |
| `ConstantVector3Source` | 未実装 |
| `ConstantVector4Source` | 未実装 |
| `ConstantColorSource` | 未実装 |
| `SignalValueSource` | 未実装 |
| `SignalTriggerSource` | 未実装 |

### Input System

| 設計名 | 状態 |
| --- | --- |
| `InputActionFloatSource` | 未実装 |
| `InputActionBoolSource` | 未実装 |
| `InputActionVector2Source` | 未実装 |
| `InputActionVector3Source` | 未実装 |
| `InputActionTriggerSource` | 未実装 |
| `InputActionReleaseTriggerSource` | 未実装 |
| `InputActionLastPressedIndexSource` | 未実装 |

### MIDI

| 設計名 | 状態 |
| --- | --- |
| `MidiAbsoluteFloatSource` | 未実装 |
| `MidiRelativeFloatSource` | 未実装 |
| `MidiVelocityFloatSource` | 未実装 |
| `MidiButtonBoolSource` | 未実装 |
| `MidiButtonGroupIndexSource` | 未実装 |
| `MidiButtonTriggerSource` | 未実装 |
| `MidiButtonReleaseTriggerSource` | 未実装 |

### Stateful

| 設計名 | 状態 |
| --- | --- |
| `ToggleBoolSource` | 未実装 |
| `SetResetBoolSource` | 未実装 |
| `PulseBoolSource` | 未実装 |
| `StepIntSource` | 未実装 |
| `IncrementDecrementIntSource` | 未実装 |
| `TriggerValueFloatSource` | 未実装 |
| `TriggerValueIntSource` | 未実装 |
| `TriggeredRandomFloatSource` | 未実装 |
| `TriggeredRandomIntSource` | 未実装 |
| `SampleHoldFloatSource` | 未実装 |
| `SampleHoldVectorSource` | 未実装 |
| `TriggerEnvelopeFloatSource` | 未実装 |

### Composite

| 設計名 | 状態 |
| --- | --- |
| `FloatBinarySource` | 未実装 |
| `IntBinarySource` | 未実装 |
| `BoolLogicSource` | 未実装 |
| `FloatCompareBoolSource` | 未実装 |
| `IntCompareBoolSource` | 未実装 |
| `FloatSelectSource` | 未実装 |
| `IntSelectSource` | 未実装 |
| `BoolSelectSource` | 未実装 |
| `Vector2SelectSource` | 未実装 |
| `Vector3SelectSource` | 未実装 |
| `Vector4SelectSource` | 未実装 |
| `ColorSelectSource` | 未実装 |
| `MostRecentFloatSource` | 未実装 |
| `MostRecentIntSource` | 未実装 |
| `MostRecentBoolSource` | 未実装 |
| `MostRecentVectorSource` | 未実装 |
| `MergeTriggerSource` | 未実装 |
| `AnyTriggerSource` | 未実装 |
| `AllTriggerSource` | 未実装 |

### 型変換

| 設計名 | 状態 |
| --- | --- |
| `IntToFloatSource` | 未実装 |
| `BoolToFloatSource` | 未実装 |
| `BoolToIntSource` | 未実装 |
| `FloatToIntSource` | 未実装 |
| `FloatToBoolSource` | 未実装 |
| `IndexToFloatSource` | 未実装 |
| `IndexToIntSource` | 未実装 |
| `IndexToBoolSource` | 未実装 |
| `IndexToColorSource` | 未実装 |
| `Vector2ToFloatSource` | 未実装 |
| `Vector3ToFloatSource` | 未実装 |
| `Vector4ToFloatSource` | 未実装 |
| `FloatToVector2Source` | 未実装 |
| `FloatToVector3Source` | 未実装 |
| `FloatToVector4Source` | 未実装 |
| `FloatToGradientColorSource` | 未実装 |

### Transport

| 設計名 | 状態 |
| --- | --- |
| `TransportBpmFloatSource` | 未実装 |
| `TransportBaseBpmFloatSource` | 未実装 |
| `TransportMultiplierFloatSource` | 未実装 |
| `BeatPhaseFloatSource` | 未実装 |
| `BarPhaseFloatSource` | 未実装 |
| `SubdivisionPhaseFloatSource` | 未実装 |
| `BeatIndexIntSource` | 未実装 |
| `BarIndexIntSource` | 未実装 |
| `BeatTriggerSource` | 未実装 |
| `BarTriggerSource` | 未実装 |
| `SubdivisionTriggerSource` | 未実装 |

### Audio

| 設計名 | 状態 |
| --- | --- |
| `LaspLevelFloatSource` | 未実装 |
| `LaspBandFloatSource` | 未実装 |
| `LaspPeakFloatSource` | 未実装 |
| `LaspSpectrumBinFloatSource` | 未実装 |
| `AudioThresholdTriggerSource` | 未実装 |
| `AudioPeakTriggerSource` | 未実装 |

### OSC

| 設計名 | 状態 |
| --- | --- |
| `OscFloatSource` | 未実装 |
| `OscIntSource` | 未実装 |
| `OscBoolSource` | 未実装 |
| `OscVector2Source` | 未実装 |
| `OscVector3Source` | 未実装 |
| `OscVector4Source` | 未実装 |
| `OscColorSource` | 未実装 |
| `OscTriggerSource` | 未実装 |

## Modifier

### Float

| 設計名 | 状態 |
| --- | --- |
| `AddFloatModifier` | 未実装 |
| `MultiplyFloatModifier` | 未実装 |
| `RemapFloatModifier` | 未実装 |
| `ClampFloatModifier` | 未実装 |
| `Invert01FloatModifier` | 未実装 |
| `AbsFloatModifier` | 未実装 |
| `SignFloatModifier` | 未実装 |
| `PowerFloatModifier` | 未実装 |
| `CurveFloatModifier` | 未実装 |
| `DeadZoneFloatModifier` | 未実装 |
| `QuantizeFloatModifier` | 未実装 |
| `FloorFloatModifier` | 未実装 |
| `CeilFloatModifier` | 未実装 |
| `RoundFloatModifier` | 未実装 |
| `RepeatFloatModifier` | 未実装 |
| `PingPongFloatModifier` | 未実装 |
| `SmoothDampFloatModifier` | 未実装 |
| `ExponentialSmoothFloatModifier` | 未実装 |
| `AttackReleaseFloatModifier` | 未実装 |
| `SlewRateFloatModifier` | 未実装 |
| `WaveShapeFloatModifier` | 未実装 |

### Int

| 設計名 | 状態 |
| --- | --- |
| `AddIntModifier` | 未実装 |
| `MultiplyIntModifier` | 未実装 |
| `ClampIntModifier` | 未実装 |
| `WrapIntModifier` | 未実装 |
| `ModuloIntModifier` | 未実装 |
| `LookupIntModifier` | 未実装 |
| `AbsIntModifier` | 未実装 |

### Bool

| 設計名 | 状態 |
| --- | --- |
| `NotBoolModifier` | 未実装 |

### Vector

| 設計名 | 状態 |
| --- | --- |
| `AddVectorModifier` | 未実装 |
| `MultiplyVectorScalarModifier` | 未実装 |
| `MultiplyVectorComponentsModifier` | 未実装 |
| `NormalizeVectorModifier` | 未実装 |
| `ClampMagnitudeVectorModifier` | 未実装 |
| `SmoothDampVectorModifier` | 未実装 |
| `RemapVectorModifier` | 未実装 |
| `AbsoluteVectorModifier` | 未実装 |

### Color

| 設計名 | 状態 |
| --- | --- |
| `MultiplyColorModifier` | 未実装 |
| `IntensityColorModifier` | 未実装 |
| `AlphaColorModifier` | 未実装 |
| `HSVColorModifier` | 未実装 |

### Trigger

| 設計名 | 状態 |
| --- | --- |
| `CooldownTriggerModifier` | 未実装 |
| `DelayTriggerModifier` | 未実装 |
| `EveryNthTriggerModifier` | 未実装 |
| `RandomChanceTriggerModifier` | 未実装 |
| `GateTriggerModifier` | 未実装 |
| `QuantizeTriggerModifier` | 未実装 |
| `DebounceTriggerModifier` | 未実装 |

## Output

### Core

| 設計名 | 状態 |
| --- | --- |
| `DebugFloatOutput` | 未実装 |
| `DebugIntOutput` | 未実装 |
| `DebugBoolOutput` | 未実装 |
| `DebugVectorOutput` | 未実装 |
| `DebugColorOutput` | 未実装 |
| `DebugTriggerOutput` | 未実装 |
| `SignalFloatOutput` | 未実装 |
| `SignalIntOutput` | 未実装 |
| `SignalBoolOutput` | 未実装 |
| `SignalVector2Output` | 未実装 |
| `SignalVector3Output` | 未実装 |
| `SignalVector4Output` | 未実装 |
| `SignalColorOutput` | 未実装 |
| `SignalTriggerOutput` | 未実装 |

### Unity Object

| 設計名 | 状態 |
| --- | --- |
| `GameObjectActiveBoolOutput` | 未実装 |
| `BehaviourEnabledBoolOutput` | 未実装 |
| `RendererEnabledBoolOutput` | 未実装 |
| `ObjectGroupIndexIntOutput` | 未実装 |
| `TransformPositionVector3Output` | 未実装 |
| `TransformRotationVector3Output` | 未実装 |
| `TransformScaleVector3Output` | 未実装 |
| `TransformPositionAxisFloatOutput` | 未実装 |
| `TransformRotationAxisFloatOutput` | 未実装 |
| `TransformScaleAxisFloatOutput` | 未実装 |

### Transport

| 設計名 | 状態 |
| --- | --- |
| `TransportTapTriggerOutput` | 未実装 |
| `TransportResetPhaseTriggerOutput` | 未実装 |
| `TransportBpmFloatOutput` | 未実装 |
| `TransportMultiplierFloatOutput` | 未実装 |
| `TransportNudgeForwardTriggerOutput` | 未実装 |
| `TransportNudgeBackwardTriggerOutput` | 未実装 |

### MIDI

| 設計名 | 状態 |
| --- | --- |
| `MidiLedBoolOutput` | 未実装 |
| `MidiLedIndexedIntOutput` | 未実装 |
| `MidiLedValueFloatOutput` | 未実装 |
| `MidiLedColorOutput` | 未実装 |
| `MidiLedTriggerFlashOutput` | 未実装 |

### Animator

| 設計名 | 状態 |
| --- | --- |
| `AnimatorFloatOutput` | 未実装 |
| `AnimatorIntOutput` | 未実装 |
| `AnimatorBoolOutput` | 未実装 |
| `AnimatorTriggerOutput` | 未実装 |
| `AnimatorLayerWeightFloatOutput` | 未実装 |
| `AnimatorStateTriggerOutput` | 未実装 |

### VFX Graph

| 設計名 | 状態 |
| --- | --- |
| `VFXFloatOutput` | 未実装 |
| `VFXIntOutput` | 未実装 |
| `VFXUIntOutput` | 未実装 |
| `VFXBoolOutput` | 未実装 |
| `VFXVector2Output` | 未実装 |
| `VFXVector3Output` | 未実装 |
| `VFXVector4Output` | 未実装 |
| `VFXColorOutput` | 未実装 |
| `VFXMatrixOutput` | 未実装 |
| `VFXTextureOutput` | 未実装 |
| `VFXTextureIndexOutput` | 未実装 |
| `VFXTriggerEventOutput` | 未実装 |

### Renderer / Material

| 設計名 | 状態 |
| --- | --- |
| `RendererFloatOutput` | 未実装 |
| `RendererIntOutput` | 未実装 |
| `RendererVectorOutput` | 未実装 |
| `RendererColorOutput` | 未実装 |
| `RendererTextureOutput` | 未実装 |
| `RendererTextureIndexOutput` | 未実装 |
| `MaterialFloatOutput` | 未実装 |
| `MaterialIntOutput` | 未実装 |
| `MaterialVectorOutput` | 未実装 |
| `MaterialColorOutput` | 未実装 |
| `MaterialTextureOutput` | 未実装 |

### Volume

| 設計名 | 状態 |
| --- | --- |
| `VolumeFloatOutput` | 未実装 |
| `VolumeIntOutput` | 未実装 |
| `VolumeBoolOutput` | 未実装 |
| `VolumeColorOutput` | 未実装 |
| `VolumeVectorOutput` | 未実装 |

### Cinemachine

| 設計名 | 状態 |
| --- | --- |
| `CinemachineCameraIndexOutput` | 未実装 |
| `CinemachineFovFloatOutput` | 未実装 |
| `CinemachineDutchFloatOutput` | 未実装 |
| `CinemachinePriorityIntOutput` | 未実装 |

### VideoPlayer

| 設計名 | 状態 |
| --- | --- |
| `VideoPlayerSpeedFloatOutput` | 未実装 |
| `VideoPlayerPlayBoolOutput` | 未実装 |
| `VideoPlayerTimeFloatOutput` | 未実装 |
| `VideoPlayerNormalizedTimeFloatOutput` | 未実装 |
| `VideoPlayerClipIndexIntOutput` | 未実装 |
| `VideoPlayerPlayTriggerOutput` | 未実装 |
| `VideoPlayerPauseTriggerOutput` | 未実装 |
| `VideoPlayerStopTriggerOutput` | 未実装 |
| `VideoPlayerRestartTriggerOutput` | 未実装 |

### AudioSource

| 設計名 | 状態 |
| --- | --- |
| `AudioSourceVolumeFloatOutput` | 未実装 |
| `AudioSourcePitchFloatOutput` | 未実装 |
| `AudioSourcePanFloatOutput` | 未実装 |
| `AudioSourcePlayBoolOutput` | 未実装 |
| `AudioSourceTimeFloatOutput` | 未実装 |
| `AudioSourceClipIndexIntOutput` | 未実装 |
| `AudioSourcePlayTriggerOutput` | 未実装 |
| `AudioSourcePauseTriggerOutput` | 未実装 |
| `AudioSourceStopTriggerOutput` | 未実装 |
| `AudioSourceRestartTriggerOutput` | 未実装 |

### OSC

| 設計名 | 状態 |
| --- | --- |
| `OscFloatOutput` | 未実装 |
| `OscIntOutput` | 未実装 |
| `OscBoolOutput` | 未実装 |
| `OscVector2Output` | 未実装 |
| `OscVector3Output` | 未実装 |
| `OscVector4Output` | 未実装 |
| `OscColorOutput` | 未実装 |
| `OscTriggerOutput` | 未実装 |

### Generic Component

| 設計名 | 状態 |
| --- | --- |
| `ComponentMemberFloatOutput` | 未実装 |
| `ComponentMemberIntOutput` | 未実装 |
| `ComponentMemberBoolOutput` | 未実装 |
| `ComponentMemberVector2Output` | 未実装 |
| `ComponentMemberVector3Output` | 未実装 |
| `ComponentMemberVector4Output` | 未実装 |
| `ComponentMemberColorOutput` | 未実装 |

## 集計の読み方

第62〜64章の設計名はSource 95項目、Modifier 48項目、Output 107項目。これはファミリーを1行で数えた一覧件数であり、具体型展開後のクラス数ではない。Subsystem、Profile、Driver、Inspector、Monitor等はこの件数に含めず、[実装・検証チェックリスト](implementation-plan.md)で追跡する。
