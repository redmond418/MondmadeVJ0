# 標準機能カタログ

更新日：2026-10-24（JST）。[移植元](reference/conversation-design.md)第62〜64章の全項目を収録する。**全項目が完成要件。** 下表はInspectorで選ぶRuntimeクラスの実装一覧であり、各クラスの独立した動作検証済みという意味ではない。各行の状態は実装状況だけを示し、動作契約と受け入れ結果は[実装・検証計画](implementation-plan.md)に記録する。

VectorはVector2 / Vector3 / Vector4の具体型へ展開した。SignalValueSourceも`SignalFloatSource`等の具体型として実装した。表はInspectorで選択するC#型名を記載する。名称整理・Gradientの扱い・8 Route型とVFX Matrix / Texture / UIntの接続は[設計補足](design-notes.md)を参照する。Source 106、Modifier 51、Output 109の計266クラス名をRuntime宣言と照合済み。クラス数はSubsystem、Profile、Driver、Inspector、Monitor等を含まず、動作受け入れの代替にもならない。

## Source

### 基本

| 設計名 | 状態 |
| --- | --- |
| `ConstantFloatSource` | 実装済み |
| `ConstantIntSource` | 実装済み |
| `ConstantBoolSource` | 実装済み |
| `ConstantVector2Source` | 実装済み |
| `ConstantVector3Source` | 実装済み |
| `ConstantVector4Source` | 実装済み |
| `ConstantColorSource` | 実装済み |
| `SignalFloatSource` | 実装済み |
| `SignalIntSource` | 実装済み |
| `SignalBoolSource` | 実装済み |
| `SignalVector2Source` | 実装済み |
| `SignalVector3Source` | 実装済み |
| `SignalVector4Source` | 実装済み |
| `SignalColorSource` | 実装済み |
| `SignalTriggerSource` | 実装済み |

### Input System

| 設計名 | 状態 |
| --- | --- |
| `InputActionFloatSource` | 実装済み |
| `InputActionBoolSource` | 実装済み |
| `InputActionVector2Source` | 実装済み |
| `InputActionVector3Source` | 実装済み |
| `InputActionTriggerSource` | 実装済み |
| `InputActionReleaseTriggerSource` | 実装済み |
| `InputActionLastPressedIndexSource` | 実装済み |

### MIDI

| 設計名 | 状態 |
| --- | --- |
| `MidiAbsoluteFloatSource` | 実装済み |
| `MidiRelativeFloatSource` | 実装済み |
| `MidiVelocityFloatSource` | 実装済み |
| `MidiXYVector2Source` | 実装済み |
| `MidiButtonBoolSource` | 実装済み |
| `MidiButtonGroupIndexSource` | 実装済み |
| `MidiButtonTriggerSource` | 実装済み |
| `MidiButtonReleaseTriggerSource` | 実装済み |

### Stateful

| 設計名 | 状態 |
| --- | --- |
| `ToggleBoolSource` | 実装済み |
| `SetResetBoolSource` | 実装済み |
| `PulseBoolSource` | 実装済み |
| `StepIntSource` | 実装済み |
| `IncrementDecrementIntSource` | 実装済み |
| `TriggerValueFloatSource` | 実装済み |
| `TriggerValueIntSource` | 実装済み |
| `TriggeredRandomFloatSource` | 実装済み |
| `TriggeredRandomIntSource` | 実装済み |
| `SampleHoldFloatSource` | 実装済み |
| `SampleHoldVector2Source` | 実装済み |
| `SampleHoldVector3Source` | 実装済み |
| `SampleHoldVector4Source` | 実装済み |
| `TriggerEnvelopeFloatSource` | 実装済み |

### Composite

| 設計名 | 状態 |
| --- | --- |
| `FloatBinarySource` | 実装済み |
| `IntBinarySource` | 実装済み |
| `BoolLogicSource` | 実装済み |
| `FloatCompareBoolSource` | 実装済み |
| `IntCompareBoolSource` | 実装済み |
| `FloatSelectSource` | 実装済み |
| `IntSelectSource` | 実装済み |
| `BoolSelectSource` | 実装済み |
| `Vector2SelectSource` | 実装済み |
| `Vector3SelectSource` | 実装済み |
| `Vector4SelectSource` | 実装済み |
| `ColorSelectSource` | 実装済み |
| `MostRecentFloatSource` | 実装済み |
| `MostRecentIntSource` | 実装済み |
| `MostRecentBoolSource` | 実装済み |
| `MostRecentVector2Source` | 実装済み |
| `MostRecentVector3Source` | 実装済み |
| `MostRecentVector4Source` | 実装済み |
| `MergeTriggerSource` | 実装済み |
| `AnyTriggerSource` | 実装済み |
| `AllTriggerSource` | 実装済み |

### 型変換

| 設計名 | 状態 |
| --- | --- |
| `IntToFloatSource` | 実装済み |
| `BoolToFloatSource` | 実装済み |
| `BoolToIntSource` | 実装済み |
| `FloatToIntSource` | 実装済み |
| `FloatToBoolSource` | 実装済み |
| `IndexToFloatSource` | 実装済み |
| `IndexToIntSource` | 実装済み |
| `IndexToBoolSource` | 実装済み |
| `IndexToColorSource` | 実装済み |
| `Vector2ToFloatSource` | 実装済み |
| `Vector3ToFloatSource` | 実装済み |
| `Vector4ToFloatSource` | 実装済み |
| `FloatToVector2Source` | 実装済み |
| `FloatToVector3Source` | 実装済み |
| `FloatToVector4Source` | 実装済み |
| `FloatToVector2ComponentSource` | 実装済み |
| `FloatToVector3ComponentSource` | 実装済み |
| `FloatToVector4ComponentSource` | 実装済み |
| `FloatToGradientColorSource` | 実装済み |

### Transport

| 設計名 | 状態 |
| --- | --- |
| `TransportBpmFloatSource` | 実装済み |
| `TransportBaseBpmFloatSource` | 実装済み |
| `TransportMultiplierFloatSource` | 実装済み |
| `BeatPhaseFloatSource` | 実装済み |
| `BarPhaseFloatSource` | 実装済み |
| `SubdivisionPhaseFloatSource` | 実装済み |
| `BeatIndexIntSource` | 実装済み |
| `BarIndexIntSource` | 実装済み |
| `BeatTriggerSource` | 実装済み |
| `BarTriggerSource` | 実装済み |
| `SubdivisionTriggerSource` | 実装済み |

### Audio

| 設計名 | 状態 |
| --- | --- |
| `LaspLevelFloatSource` | 実装済み |
| `LaspBandFloatSource` | 実装済み |
| `LaspPeakFloatSource` | 実装済み |
| `LaspSpectrumBinFloatSource` | 実装済み |
| `AudioThresholdTriggerSource` | 実装済み |
| `AudioPeakTriggerSource` | 実装済み |

### OSC

| 設計名 | 状態 |
| --- | --- |
| `OscFloatSource` | 実装済み |
| `OscIntSource` | 実装済み |
| `OscBoolSource` | 実装済み |
| `OscVector2Source` | 実装済み |
| `OscVector3Source` | 実装済み |
| `OscVector4Source` | 実装済み |
| `OscColorSource` | 実装済み |
| `OscTriggerSource` | 実装済み |

## Modifier

### Float

| 設計名 | 状態 |
| --- | --- |
| `AddFloatModifier` | 実装済み |
| `MultiplyFloatModifier` | 実装済み |
| `RemapFloatModifier` | 実装済み |
| `ClampFloatModifier` | 実装済み |
| `Invert01FloatModifier` | 実装済み |
| `AbsFloatModifier` | 実装済み |
| `SignFloatModifier` | 実装済み |
| `PowerFloatModifier` | 実装済み |
| `CurveFloatModifier` | 実装済み |
| `DeadZoneFloatModifier` | 実装済み |
| `QuantizeFloatModifier` | 実装済み |
| `FloorFloatModifier` | 実装済み |
| `CeilFloatModifier` | 実装済み |
| `RoundFloatModifier` | 実装済み |
| `RepeatFloatModifier` | 実装済み |
| `PingPongFloatModifier` | 実装済み |
| `SmoothDampFloatModifier` | 実装済み |
| `ExponentialSmoothFloatModifier` | 実装済み |
| `AttackReleaseFloatModifier` | 実装済み |
| `SlewRateFloatModifier` | 実装済み |
| `WaveShapeFloatModifier` | 実装済み |

### Int

| 設計名 | 状態 |
| --- | --- |
| `AddIntModifier` | 実装済み |
| `MultiplyIntModifier` | 実装済み |
| `ClampIntModifier` | 実装済み |
| `WrapIntModifier` | 実装済み |
| `ModuloIntModifier` | 実装済み |
| `LookupIntModifier` | 実装済み |
| `AbsIntModifier` | 実装済み |

### Bool

| 設計名 | 状態 |
| --- | --- |
| `NotBoolModifier` | 実装済み |

### Vector

| 設計名 | 状態 |
| --- | --- |
| `AddVector2Modifier` | 実装済み |
| `AddVector3Modifier` | 実装済み |
| `AddVector4Modifier` | 実装済み |
| `MultiplyVector2Modifier` | 実装済み |
| `MultiplyVector3Modifier` | 実装済み |
| `MultiplyVector4Modifier` | 実装済み |
| `NormalizeVectorModifier` | 実装済み |
| `ClampMagnitudeVectorModifier` | 実装済み |
| `SmoothDampVectorModifier` | 実装済み |
| `RemapVectorModifier` | 実装済み |
| `AbsoluteVectorModifier` | 実装済み |

### Color

| 設計名 | 状態 |
| --- | --- |
| `MultiplyColorModifier` | 実装済み |
| `IntensityColorModifier` | 実装済み |
| `AlphaColorModifier` | 実装済み |
| `HSVColorModifier` | 実装済み |

### Trigger

| 設計名 | 状態 |
| --- | --- |
| `CooldownTriggerModifier` | 実装済み |
| `DelayTriggerModifier` | 実装済み |
| `EveryNthTriggerModifier` | 実装済み |
| `RandomChanceTriggerModifier` | 実装済み |
| `GateTriggerModifier` | 実装済み |
| `QuantizeTriggerModifier` | 実装済み |
| `DebounceTriggerModifier` | 実装済み |

## Output

### Core

| 設計名 | 状態 |
| --- | --- |
| `DebugFloatOutput` | 実装済み |
| `DebugIntOutput` | 実装済み |
| `DebugBoolOutput` | 実装済み |
| `DebugVector2Output` | 実装済み |
| `DebugVector3Output` | 実装済み |
| `DebugVector4Output` | 実装済み |
| `DebugColorOutput` | 実装済み |
| `DebugTriggerOutput` | 実装済み |
| `SignalFloatOutput` | 実装済み |
| `SignalIntOutput` | 実装済み |
| `SignalBoolOutput` | 実装済み |
| `SignalVector2Output` | 実装済み |
| `SignalVector3Output` | 実装済み |
| `SignalVector4Output` | 実装済み |
| `SignalColorOutput` | 実装済み |
| `SignalTriggerOutput` | 実装済み |

### Unity Object

| 設計名 | 状態 |
| --- | --- |
| `GameObjectActiveBoolOutput` | 実装済み |
| `BehaviourEnabledBoolOutput` | 実装済み |
| `RendererEnabledBoolOutput` | 実装済み |
| `ObjectGroupIndexIntOutput` | 実装済み |
| `TransformPositionVector3Output` | 実装済み |
| `TransformRotationVector3Output` | 実装済み |
| `TransformScaleVector3Output` | 実装済み |
| `TransformPositionAxisFloatOutput` | 実装済み |
| `TransformRotationAxisFloatOutput` | 実装済み |
| `TransformScaleAxisFloatOutput` | 実装済み |

### Transport

| 設計名 | 状態 |
| --- | --- |
| `TransportTapTriggerOutput` | 実装済み |
| `TransportResetPhaseTriggerOutput` | 実装済み |
| `TransportBpmFloatOutput` | 実装済み |
| `TransportMultiplierFloatOutput` | 実装済み |
| `TransportNudgeForwardTriggerOutput` | 実装済み |
| `TransportNudgeBackwardTriggerOutput` | 実装済み |

### MIDI

| 設計名 | 状態 |
| --- | --- |
| `MidiLedBoolOutput` | 実装済み |
| `MidiLedIndexedIntOutput` | 実装済み |
| `MidiLedValueFloatOutput` | 実装済み |
| `MidiLedColorOutput` | 実装済み |
| `MidiLedTriggerFlashOutput` | 実装済み |

### Animator

| 設計名 | 状態 |
| --- | --- |
| `AnimatorFloatOutput` | 実装済み |
| `AnimatorIntOutput` | 実装済み |
| `AnimatorBoolOutput` | 実装済み |
| `AnimatorTriggerOutput` | 実装済み |
| `AnimatorLayerWeightFloatOutput` | 実装済み |
| `AnimatorStateTriggerOutput` | 実装済み |

### VFX Graph

| 設計名 | 状態 |
| --- | --- |
| `VFXFloatOutput` | 実装済み |
| `VFXIntOutput` | 実装済み |
| `VFXUIntOutput` | 実装済み |
| `VFXBoolOutput` | 実装済み |
| `VFXVector2Output` | 実装済み |
| `VFXVector3Output` | 実装済み |
| `VFXVector4Output` | 実装済み |
| `VFXColorOutput` | 実装済み |
| `VFXMatrixOutput` | 実装済み |
| `VFXTextureOutput` | 実装済み |
| `VFXTextureIndexOutput` | 実装済み |
| `VFXTriggerEventOutput` | 実装済み |

### Renderer / Material

| 設計名 | 状態 |
| --- | --- |
| `RendererFloatOutput` | 実装済み |
| `RendererIntOutput` | 実装済み |
| `RendererVectorOutput` | 実装済み |
| `RendererColorOutput` | 実装済み |
| `RendererTextureOutput` | 実装済み |
| `RendererTextureIndexOutput` | 実装済み |
| `MaterialFloatOutput` | 実装済み |
| `MaterialIntOutput` | 実装済み |
| `MaterialVectorOutput` | 実装済み |
| `MaterialColorOutput` | 実装済み |
| `MaterialTextureOutput` | 実装済み |

### Volume

| 設計名 | 状態 |
| --- | --- |
| `VolumeFloatOutput` | 実装済み |
| `VolumeIntOutput` | 実装済み |
| `VolumeBoolOutput` | 実装済み |
| `VolumeColorOutput` | 実装済み |
| `VolumeVectorOutput` | 実装済み |

### Cinemachine

| 設計名 | 状態 |
| --- | --- |
| `CinemachineCameraIndexOutput` | 実装済み |
| `CinemachineFovFloatOutput` | 実装済み |
| `CinemachineDutchFloatOutput` | 実装済み |
| `CinemachinePriorityIntOutput` | 実装済み |

### VideoPlayer

| 設計名 | 状態 |
| --- | --- |
| `VideoPlayerSpeedFloatOutput` | 実装済み |
| `VideoPlayerPlayBoolOutput` | 実装済み |
| `VideoPlayerTimeFloatOutput` | 実装済み |
| `VideoPlayerNormalizedTimeFloatOutput` | 実装済み |
| `VideoPlayerClipIndexIntOutput` | 実装済み |
| `VideoPlayerPlayTriggerOutput` | 実装済み |
| `VideoPlayerPauseTriggerOutput` | 実装済み |
| `VideoPlayerStopTriggerOutput` | 実装済み |
| `VideoPlayerRestartTriggerOutput` | 実装済み |

### AudioSource

| 設計名 | 状態 |
| --- | --- |
| `AudioSourceVolumeFloatOutput` | 実装済み |
| `AudioSourcePitchFloatOutput` | 実装済み |
| `AudioSourcePanFloatOutput` | 実装済み |
| `AudioSourcePlayBoolOutput` | 実装済み |
| `AudioSourceTimeFloatOutput` | 実装済み |
| `AudioSourceClipIndexIntOutput` | 実装済み |
| `AudioSourcePlayTriggerOutput` | 実装済み |
| `AudioSourcePauseTriggerOutput` | 実装済み |
| `AudioSourceStopTriggerOutput` | 実装済み |
| `AudioSourceRestartTriggerOutput` | 実装済み |

### OSC

| 設計名 | 状態 |
| --- | --- |
| `OscFloatOutput` | 実装済み |
| `OscIntOutput` | 実装済み |
| `OscBoolOutput` | 実装済み |
| `OscVector2Output` | 実装済み |
| `OscVector3Output` | 実装済み |
| `OscVector4Output` | 実装済み |
| `OscColorOutput` | 実装済み |
| `OscTriggerOutput` | 実装済み |

### Generic Component

| 設計名 | 状態 |
| --- | --- |
| `ComponentMemberFloatOutput` | 実装済み |
| `ComponentMemberIntOutput` | 実装済み |
| `ComponentMemberBoolOutput` | 実装済み |
| `ComponentMemberVector2Output` | 実装済み |
| `ComponentMemberVector3Output` | 実装済み |
| `ComponentMemberVector4Output` | 実装済み |
| `ComponentMemberColorOutput` | 実装済み |

## 集計の読み方

第62〜64章の設計一覧はSource 95項目、Modifier 48項目、Output 107項目。VectorやSignalのファミリーを具体型へ展開すると本表はSource 106クラス、Modifier 51クラス、Output 109クラスになる（計266クラス名）。Subsystem、Profile、Driver、Inspector、Monitor等は本表に含めず、[実装・検証チェックリスト](implementation-plan.md)で追跡する。本表の全クラス名がRuntime実装に存在することを照合した。
