# プロジェクト調査と移植元

更新日：2026-10-23（JST）。下記は作業開始時の調査スナップショットと、現時点の実装状態を併記する。パッケージの最新版・将来の互換性を保証するものではない。

本書の「候補」「確認」は作業開始時点の記録。設計判断と現状の導入結果は[動作契約](design-notes.md)と[パッケージ構成](packaging.md)を参照する。OSC JackはType Tag取得APIを追加したredmond418 fork、InspectorはUnity標準UI Toolkit、MIDIはMinis＋RtMidi、ソースは単一UPM内の機能別asmdef配置を採用した。

## 作業開始時点の状態

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
| Unity接続 | Unity CLI 1.0.0-beta.14で本プロジェクトのEditorがreadyと確認 |

開始時点では独自Runtime / Editor実装がなく、R3や各Integrationもmanifestに未導入だった。生成済みのcsprojやLibrary内のキャッシュは開始時点の実装根拠に含めていない。

## 2026-10-23時点の実装

| 項目 | 状況 |
| --- | --- |
| UPM | `Packages/com.redmond.mondmade-vj0/`に単一パッケージを作成。Runtime / Editor / Testsを機能別asmdefへ分離 |
| Runtime | 8型Route、共通Lifecycle / FrameClock / Service、Source / Modifier、Transport、MIDI / APC、OSC、Lasp、Animator、VFX、Rendering、Cinemachine、Video、Generic Component Outputを実装 |
| Editor | Core Route Inspector、検索可能なNode型選択、Integration候補Drawer、MIDI Learn、Setup、Validation、Route中間値・MIDI / Transport / Performance Monitorを実装 |
| Unity | Unity 6000.3.8f1 macOS arm64 Stage / local-file consumer: EditMode 135 passed / 2 optional skip、通常PlayMode 7 passed / 6 Explicit skip。全依存Stage: EditMode 169/169、PlayMode 7/6、macOS universal Mono / IL2CPP Player build成功（IL2CPP Test Runnerは成功表示、XML件数未確定）、Windows x64 Mono cross-build成功。OSC UDP / strict Type Tag、Lasp Audio契約、QuickStart全64 Pad mappingを検証。A14規定Stage実機試験はOSXEditor / Apple M1 / macOS 26.1.0、120 / 174 / 240 BPM各600秒でfps 59.996 / 59.994 / 59.991、LED 10Hz / 約800 states/s。Clock欠落0 / 3 / 10、drop 0、p99各0.0010ms、最大遅延5.8020 / 12.5650 / 10.3140ms、累積ずれ5.7989 / 12.5641 / 10.3135msで不合格。外部受信機実測未実施。最新sourceの全依存Stage EditModeは169/169通過。CoreMIDI timestamped Clock backendの8ms先行予約を使う61秒×3速度smokeはClock欠落 / drop 0、p99 1.193〜1.234ms、最大4.114〜4.753msだが、累積ずれ3.219〜4.304msで1ms条件を超過した。外部受信機実測は未実施。最新版はmacOS universal IL2CPP Player build Succeeded・0 errors / 2 platform-service warnings、Windows x64 Mono cross-build Succeeded・0 errors / 2 Unity Cloud Diagnostics symbol-upload warnings。library C# warningは0。 |
| 構成 | R3 / Unsafe / NuGetForUnity / Unity標準moduleだけの任意UPM依存なし構成でlocal package import・compile、macOS universal IL2CPP、Windows x64 Mono Player buildを確認。Input System＋MIDI＋OSCを加えた部分構成もGit UPM URLでimport・compile、EditMode 123 passed / 2 URP / VFX tests skipped、macOS universal Mono Player buildを確認 |
| APC mini mk2 | 90-control ProfileとQuickStart Sceneを`Assets/MondmadeVJ0.Dev/`へ配置。macOS IORegistryでUSB接続を確認し、RtMidi input/output両portをOpen。実機の左上Pad 1がNote 56を送ることを確認し、QuickStartを含むProfile note番号を修正。Bulk SysExによるPad 1緑点灯をユーザーが目視確認。Fader / Track ButtonはStage Projectの絞り込みPlayMode probeを90秒間で7回実行したが、いずれもport接続後にraw MIDI 0件で、型付き入力未確認。5回目は90.115秒、6回目は90.596秒、7回目は90.121秒でRaw MIDI 0件。6・7回目は物理操作を依頼したが、実施有無は確認できていない。RtMidi input / outputはport 0 `APC mini mk2 Control`、port 1 `APC mini mk2 Notes`と列挙され、Fader / Track Button probeは正しいControl port 0を選択。公式[Communications Protocol](https://cdn.inmusicbrands.com/akai/attachments/APC%20mini%20mk2%20-%20Communication%20Protocol%20-%20v1.0.pdf)のFader 1 CC 0x30、Track Button 1 Note 0x64（いずれもchannel 0 / Port 0）とProfileは一致。 |
| 未完の受入 | CoreMIDI backendを含むPackage SHA `066590edf7353b158865311324cf6c194ca74779` の`git+file` UPM URLから部分依存Projectへ導入し、EditMode 145 passed / 2 optional skippedとmacOS universal Mono Player build（0 errors / 0 warnings）を確認。publicなGitHub repositoryへの現行commit pushとrelease tag作成は未実施のためHTTPS UPM導入は未検証。Windows IL2CPP / Windows上の実機MIDI、全Integration Playerの外部連携・実機動作、A14 10分×3速度の再合格（旧RtMidi試験は不合格、CoreMIDI短縮smokeも累積ずれ超過）と外部受信機実測、USB切断・再接続、Fader / Track Button実機入力照合 |

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

## 作業開始時の依存候補

下表の「未導入」は2026-10-22の作業開始時点を示す。現在の依存lockと採用版は[パッケージ構成](packaging.md)を参照。

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

初回調査時点では既存manifestのGit URLに固定tagがないものもあった。作業では採用版をlockし、`Packages/`と開発用Sceneを追加した。参照元リポジトリへ変更は加えていない。
