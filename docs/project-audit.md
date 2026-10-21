# プロジェクト調査と移植元

調査日：2026-10-22（JST）。ローカルの実ファイルとUnity CLIで確認した事実を記す。パッケージの最新版・互換性の保証ではなく、調査時点の構成である。

本書の「候補」「確認」は初回調査の記録。以後の設計判断は[動作契約](design-notes.md)と[パッケージ構成](packaging.md)で確定している。例えばOSC JackはType Tag取得APIを追加するredmond418 fork、InspectorはUnity標準UI Toolkit、MIDIはMinis＋RtMidi、ソースは単一UPM内の機能別asmdef配置を採用する。forkの必要API追加・依存のインストール・互換性試験はまだ行っていない。

## MondmadeVJ0の現在地

調査開始時HEAD：`60ee129d3497c214bb6eb25718c5da11729fc49c`（プロジェクト作成）。開始時の作業ツリーはclean。

| 項目 | 確認結果 |
| --- | --- |
| Unity | [ProjectVersion.txt](../ProjectSettings/ProjectVersion.txt)：6000.3.8f1 |
| Render Pipeline | [manifest.json](../Packages/manifest.json)：URP 17.3.0 |
| Input System | 1.18.0。activeInputHandler=1、新Input Systemを使用 |
| その他 | Test Framework 1.6.0、Timeline 1.8.10、Pipeline 0.9.0-exp.1 |
| Assets | SampleScene、InputSystem_Actions、PC / Mobile用URP・Renderer、Volume Profile |
| Build Settings | [EditorBuildSettings.asset](../ProjectSettings/EditorBuildSettings.asset)：SampleSceneのみ有効 |
| Color Space | Linear |
| 独自コード | Assets以下にC#、asmdef、MondmadeVJ0 Runtimeの実装はない |
| Unity接続 | Unity CLI 1.0.0-beta.13で本プロジェクトのEditorがreadyと確認 |

設計上のコード例はすべて未実装。R3・Minis・Lasp・OSC Jack・Cinemachine・VFX Graph・Alchemy・UniTask・LitMotionは現在のmanifestにない。生成済みのcsprojやLibrary内のキャッシュをライブラリ実装の根拠にしない。

## 参照した既存システム

3リポジトリは本プロジェクトと同じGitHubフォルダの直下。以下のリンクはその配置を前提にする。各リポジトリには調査時にローカル変更があったため、HEADを記録しつつ、参照内容は実際の作業ツリーで確認した。今回、参照元へ変更は加えていない。

| プロジェクト | HEAD | 位置付け |
| --- | --- | --- |
| Vkasai2026VJ | `c461b463b32b620fed2dcd0a47a497af86e62930` | 直列処理・Output群・APC LED送信の参考 |
| NxPCLab20260327 | `83811895f5b79303f3ca6745789c38e231ab7bc2` | Composite / 型変換 / Input / Audio Providerの参考 |
| OmochworlD | `87ec11dffbc656f3f95b12cd5d7bd3494407acac` | 静的基盤と動的アプリ機能の境界確認 |

NxPCLabの[AGENTS.md](../../NxPCLab20260327/AGENTS.md)と[spec.md](../../NxPCLab20260327/spec.md)は参照元固有の方針。Provider Tree必須、全体namespace、Editor制約などをMondmadeVJ0へそのまま転用しない。会話で採用されたHybrid設計を基準とする。

## 実装の対応表

「参考にする」はそのままコピーする意味ではない。プロジェクト固有のnamespace・依存・状態所有権を切り離し、新しい契約で再実装する。

| 領域 | 実ファイル | 確認内容と移植判断 |
| --- | --- | --- |
| Pipeline | [ParametersProcessor.cs](../../Vkasai2026VJ/Assets/Scripts/Runtime/Control/ParametersProcessor/ParametersProcessor.cs) | Input / Processor / Outputの直列構成。新基盤は1 Route＝1 Sourceを基本とし、最終Streamの購読をRouteへ集約 |
| 初期値 | [TriggerProviders.cs](../../NxPCLab20260327/Assets/VJMond/Core/Routing/Providers/TriggerProviders.cs) | TriggerToLoopingIntProviderがGetStream内でSubject.OnNextしてから返すため、後続購読は初期値を受け取れない。現在値契約へ修正する対象 |
| Routing所有権 | [ParameterRoutingManager.cs](../../NxPCLab20260327/Assets/VJMond/Core/Routing/Runtime/ParameterRoutingManager.cs) | StartでBind、DestroyでDispose。GameOfLife固有Setupも含む。共通Runtimeへ固有参照を持ち込まない |
| 平滑化 | [SmoothProviders.cs](../../NxPCLab20260327/Assets/VJMond/Core/Routing/Providers/SmoothProviders.cs) | ProviderごとにEveryUpdateを購読。共通FrameClockと同型Modifierへ変更 |
| 時間 | [TempoController.cs](../../NxPCLab20260327/Assets/VJMond/Core/Tempo/TempoController.cs) | BeatでstartTimeをTime.unscaledTimeへ更新。epoch＋double ClockのTransportへ置換 |
| Subdivision | [BeatIntervalEmitter.cs](../../NxPCLab20260327/Assets/VJMond/Core/Tempo/BeatIntervalEmitter.cs) | 4拍の頭から別途経過時間を計算。拍子・Subdivision・Quantizeを共通Transportへ統合 |
| Audio | [LaspProviders.cs](../../NxPCLab20260327/Assets/VJMond/Core/Routing/Providers/LaspProviders.cs) | Band / Custom Hzと閾値・Hysteresis・Cooldownを参考にする。内蔵smoothingはModifierへ分離。周波数Index変換・正規化はLasp実APIで再検証 |
| Envelope | [MotionProviders.cs](../../NxPCLab20260327/Assets/VJMond/Core/Routing/Providers/MotionProviders.cs) | LitMotion＋UniTaskのTriggerToMotion。Core EnvelopeはFrameClockで作り、必要なら独立LitMotion Integrationへ分離 |
| VFX | [VFXParameterOutput.cs](../../Vkasai2026VJ/Assets/Scripts/Runtime/Control/ParametersProcessor/Output/VFXParameterOutput.cs) | Int / Float / BoolのSetterを参考にする。型検査・Property選択・名前のCacheを追加する設計 |
| Material | [MaterialPropertyParameterOutput.cs](../../Vkasai2026VJ/Assets/Scripts/Runtime/Control/ParametersProcessor/Output/MaterialPropertyParameterOutput.cs) | Materialへ直接書込む方式。RendererのMPB OutputとAsset直接変更Outputを分離 |
| Volume | [VolumeBindings.cs](../../NxPCLab20260327/Assets/VJMond/Core/Routing/Bindings/VolumeBindings.cs) | ColorCurves専用Binding。任意対応ParameterのOutputへ一般化し、Output自身に購読を持たせない |
| MIDI OUT | [MidiOutputService.cs](../../Vkasai2026VJ/Assets/Scripts/Runtime/Control/Midi/MidiOutputService.cs) | RtMidiでPort検索・Open・Send・Close。Static SingletonをMidiHubのServiceへ変更し、複数Surface・再接続を設計 |
| LED Driver | [ApcMiniMk2LedDriver.cs](../../Vkasai2026VJ/Assets/Scripts/Runtime/Control/Midi/ApcMiniMk2LedDriver.cs) | 0x96 / 0x90、赤緑Velocity、Mono ON/OFF。機種固有Protocolの参考として共通Styleから分離 |
| LED状態 | [ApcMiniMk2LedFeedbackController.cs](../../Vkasai2026VJ/Assets/Scripts/Runtime/Control/Midi/ApcMiniMk2LedFeedbackController.cs) | ControlMessageからtoggledSlotByIdを別管理しDefaultActiveもLED側に保持。Route Stateを映像とLEDへ同時配信する構造へ変更 |
| LED物理定義 | [ApcMiniMk2LedMap.cs](../../Vkasai2026VJ/Assets/Scripts/Runtime/Control/Midi/ApcMiniMk2LedMap.cs) / [出力検証](../../Vkasai2026VJ/Assets/Scripts/Runtime/Control/ApcMiniMk2LedOutputTest.cs) | Mappingと検証用Protocol値の確認先。新Profileに用途上のToggle・Camera選択を保存しない。APC実機仕様の確定にはメーカー資料と実機検証が必要 |
| 動的Binding | [ParameterBindingService.cs](../../OmochworlD/Assets/Scripts/Runtime/Input/ParameterBindingService.cs) | selected target、Bind / Unbind、pending復元を持つ。ランタイム編集アプリ用として本基盤へ移植しない |

NxPCLabの同じProvidersフォルダにはCompare、BooleanLogic、Collection、ValueConversion、InputAction系もある。各演算の参考にしつつ、Source / Modifier / 型変換の新しい分類に分け直す。LifeStepTriggerProviderやGameOfLife専用Binding、SDF描画・ノード保存・アプリUIは本基盤の移植対象にしない。

## 依存関係の現状と候補

| 依存 | 現在 | 既存プロジェクトでの確認内容・方針 |
| --- | --- | --- |
| R3 | 未導入 | 3作のmanifestにUnity側UPM、Assets/packages.configにも関連NuGetがある。Coreの必須候補。依存解決を確認してから導入 |
| Input System | 1.18.0 | Vkasai / Nxも1.18.0 |
| Minis | 未導入 | Vkasai / Nx 1.3.2、OmochworlD 1.3.1。物理入力Adapter候補 |
| RtMidi | 未導入 | Vkasaiのpackages-lockでjp.keijiro.rtmidi 2.2.0（間接依存）、MidiOutputServiceはRtMidi名前空間を使用。MinisだけでMIDI OUTまで実装できると仮定しない |
| OSC Jack | 未導入 | Vkasai / Nx 2.0.0、OmochworlDはredmond418 fork。fork固有機能の要否を確認 |
| Lasp | 未導入 | 3作とも2.1.8 |
| VFX Graph | 未導入 | Vkasai / Nx 17.3.0。現在のURPと同系列だが導入・Player確認は未実施 |
| Cinemachine | 未導入 | Vkasai / Nx 3.1.6、OmochworlD 3.1.5。v3系を参照元として確認 |
| Alchemy | 未導入 | 3作でGit URL依存。Inspector実装候補でありCore必須にはしない |
| UniTask / LitMotion | 未導入 | 3作で使用。Coreへ必須化せず、必要なIntegration側で検討 |
| Syphon / Hap / QuectoLines / uRaymarching等 | 未導入 | 既存アプリの映像制作側依存。会話の完成要件へ自動追加しない |

既存manifestのGit URLは固定tagがないものもある。実装時に採用バージョン、lock、native pluginの対象OSを固定・確認する。今回は設計資料だけを整備し、Packages、ProjectSettings、Scene、参照元コードは変更していない。
