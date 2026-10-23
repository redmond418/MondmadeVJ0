# パッケージ構成と導入

更新日：2026-10-23（JST）。**UPMパッケージは1つに統合し、機能の分離は内部フォルダとasmdefで行う。** Package、Setup Window、Core / Integration Editor、EditMode / PlayMode testsを配置した。Unity 6000.3.8f1の最新全依存Stage ProjectではEditMode 169/169が通過し、通常PlayModeは7 passed / 6 Explicit skipped。現作業ツリーの全依存ProjectでもEditMode 173/173、通常PlayMode 8/8（7 Explicit skipped）を確認した。native CoreMIDI packet buffer再利用変更後もmacOS universal Mono / IL2CPP Player buildが成功した（IL2CPP 0 errors / Cloud Diagnostics symbol upload tokenなしのwarning 1件）。Windows x64 Mono cross-buildも成功した。Windows buildはUnity Cloud Diagnosticsのsymbol upload warningが2件あり、Windows IL2CPPはこのmacOS hostで未検証。StandaloneOSX IL2CPP Test Runnerは「All test(s) succeeded」と表示したが、Player終了後にNUnit XMLが出ず実行件数は未確定。CoreMIDI timestamped Clockの61秒×3速度smokeは欠落 / drop 0だったがsubmission driftが3.219〜4.304msで、A14の1ms基準を超過した。外部受信機の期限実測も未実施のため未合格。APC mini mk2 Pad 1のNote 56、VelocityButtonの押下・Release、Fader 1とTrack Button 1の型付き実機入力、Pad 1の緑点灯を確認した。型付きPad probe #14は9.214秒、Fader / Track Button probe #13は15.210秒で通過した。A20では対話型EditorからBackground Music入力（2ch）を8秒取得し、Runtime FrameClock経由で2,984 normalized samples・peak 0.797を確認した（音声データは保存していない）。QuickStartとProfileの入力を引き続き確認し、APC mini mk2は接続状態を維持している。A17実機USB切断・再接続probeは1/1 Passed。実機Pad 1入力後、切断中のPad押下 / Fader=0.7 / Toggle=true / Hold=trueの保持、再接続時のgeneration更新、Panic中の80 LED全OFF Full Resync、Panic解除後のPad 1 Desired LED値21再送を確認し、Pad 1の緑点灯もライブprobe中に目視した。公開HTTPS UPM URLは現行commit未push・release tag未作成のため未検証。残る受入条件の詳細は[実装・検証計画](implementation-plan.md)を参照。

2026-10-23の後続A14 smokeではCoreMIDI計測点を`MIDISend`直前へ修正した。各61秒・120 / 174 / 240 BPMの累積submission driftは3.9339 / 2.9742 / 5.1804msで、依然1ms基準を超えた。旧計測点による値と外部receiver測定の区別は[実装・検証計画](implementation-plan.md)に記録する。

## 配置

```text
MondmadeVJ0/
├ Packages/
│ ├ manifest.json
│ ├ packages-lock.json
│ └ com.redmond.mondmade-vj0/        # 配布・導入するUPMパッケージはこれ1つ
│   ├ package.json
│   ├ Runtime/
│   │ ├ Core/                       # 独立asmdef、Route・Signal・Root等
│   │ ├ Transport/
│   │ ├ InputSystem/
│   │ ├ Midi/
│   │ ├ Osc/
│   │ ├ Audio/Lasp/
│   │ ├ VFXGraph/
│   │ ├ Rendering/
│   │ ├ Cinemachine/
│   │ ├ Video/
│   │ └ Animator/
│   ├ Editor/
│   │ ├ Core/                       # 基本Inspector・Setup・Monitor
│   │ └ Integrations/               # Integration別のEditor asmdef
│   ├ Tests/EditMode/                # Core / Editor / MIDI / OSC / Transport tests
│   ├ Tests/PlayMode/                # Runtime lifecycle / APC hardware tests
│   ├ Documentation~/               # package内で配布する追加資料（任意）
│   ├ README.md
│   ├ CHANGELOG.md
│   └ LICENSE (公開前に権利者が選択)
├ Assets/
│ ├ MondmadeVJ0.Dev/                 # 結合検証Scene・実機テスト・開発素材
│ ├ MondmadeVJ0.Generated/           # Generic Accessor生成C#（既定asmdefなし）
│ └ Editor/MondmadeVJ0Bootstrap/     # 開発プロジェクト用の外部依存導入
├ ProjectSettings/
└ docs/
```

開発中はembedded packageとしてこのソースを編集し、.metaもGit管理する。利用側も同じ1パッケージをGit URLまたはローカルpackage.jsonで参照する。Library/PackageCacheやAssets側へ実装を二重配置しない。公開サンプルはSamples~、実機依存の結合試験はAssets/MondmadeVJ0.Devに置く。[UnityのPackage layout](https://docs.unity3d.com/6000.3/Documentation/Manual/cus-layout.html)

## 内部Assemblyと外部依存

package IDは `com.redmond.mondmade-vj0`。namespace / assemblyのprefixは `MondmadeVJ0`。1つのpackage.jsonが全機能を配布する。Coreと機能別Integrationは独立Runtime asmdefを持つ。Core Editor、Animator / MIDI / Rendering / VFX Editor、Setup Editorを分離した。Core契約、Inspector Validation、APC Profile、Transport、OSC AdapterのEditMode testsと、Runtime Lifecycle / APC hardwareのPlayMode testsを機能別asmdefに配置した。Coreは任意Integrationを参照しない。

| フォルダ | Runtime assembly | 内容・外部依存（採用基準版） |
| --- | --- | --- |
| Core | MondmadeVJ0.Core | Route、Source / Modifier、Signal、Clock契約、Root、Unity Object / Transform / AudioSource出力。R3 1.3.0本体、com.cysharp.r3 1.3.0、System.Runtime.CompilerServices.Unsafe 6.0.0、Unity Audio module |
| Transport | MondmadeVJ0.Transport | Core、Transport、Grid / Quantize、Tap。Coreのinterfaceを実装 |
| InputSystem | MondmadeVJ0.InputSystem | Core、com.unity.inputsystem 1.18.0 |
| Midi | MondmadeVJ0.Midi | Core、Minis 1.3.2、RtMidi 2.2.0、Input System。Raw Clock連携はCoreのinterface経由 |
| Osc | MondmadeVJ0.Osc | Core、OSC Jack 2.0.0ベースのredmond418 fork。D20のType Tag APIを追加したcommitへ固定 |
| Audio/Lasp | MondmadeVJ0.Audio.Lasp | Core、jp.keijiro.lasp 2.1.8 |
| VFXGraph | MondmadeVJ0.VFXGraph | Core、com.unity.visualeffectgraph 17.3.0 |
| Rendering | MondmadeVJ0.Rendering | Core、URP 17.3.0。Material / MPB / Volume |
| Cinemachine | MondmadeVJ0.Cinemachine | Core、com.unity.cinemachine 3.1.6 |
| Video | MondmadeVJ0.Video | Core、Unity Video module |
| Animator | MondmadeVJ0.Animator | Core、Unity Animation module |

UnityのProject manifestでAudio、Animation、Videoの標準moduleを有効にする。今回のR3最小構成でも、これらのmoduleを含む標準module群を導入している。

Editor assemblyはRuntime名に `.Editor`、Testsは `.Tests.Editor` / `.Tests.Runtime`を付ける。基本EditorはCoreだけへ依存し、Integration Editorは対応RuntimeとCore Editorへ依存する。Alchemy / UniTask / LitMotionは標準依存に含めない。LitMotion連携を追加する場合も同一パッケージ内の任意Integration assemblyとする。

## 任意Integrationの有効化

全Integrationのソースを同梱し、利用プロジェクトに外部依存が揃った機能だけをコンパイルする。Lasp / Cinemachine / VFX Graph等をすべてpackage.jsonの必須依存にして自動導入する構成にはしない。

各Integration asmdefに必要packageのVersion Definesと同じsymbolのDefine Constraintsを設定する。例：Cinemachineはcom.unity.cinemachineの `[3.1.6,4.0.0)` で `MONDMADE_HAS_CINEMACHINE` を定義し、そのsymbolをassemblyの制約にする。Editor / Testsも同条件、複数依存のMidiは全条件のANDとする。外部assemblyは名前で参照し、任意packageの不存在時に未解決GUIDを残さない。UnityはVersion DefinesのsymbolをDefine Constraintsに利用できる。[Unityの条件付きAssembly](https://docs.unity.com/en-us/engine/6000.7/manual/scripting/compilation-and-code-reload/script-compilation/assembly-definition-files/includes)

互換範囲は採用基準版以上・次major未満を基本とし、URP / VFX Graphは17.3系列の `[17.3.0,17.4.0)` とする。範囲外はSetup Windowで非対応と表示してassemblyを無効にする。既存版を無断でdowngradeしない。

Core Editorは利用可能なIntegrationだけを型登録から列挙する。未導入機能はSetupに「依存未導入」と表示し、Route追加メニューには出さない。外部型を参照するProfile・Prefab・Sceneは該当Samples~以下に置き、依存未導入時に自動importしない。既存Routeが使う依存の除去はD18の欠落型保護に従う。

OSC forkは固定commit `7ef8fa692030079d2ad6d1a314680b96fbd9a328` を参照する。Type Tag APIの直接呼び出しとIL2CPP preservation descriptorを実装した。fork APIのPlayer動作は別途検証記録が必要。

任意依存なし構成はR3 / Unsafe / NuGetForUnity / Unity標準moduleだけでlocal package import・compileを確認し、macOS universal IL2CPPとWindows x64 Mono Player buildも成功した。Input System＋MIDI＋OSC部分構成は過去の完全SHA `git+file` UPM URLでimport・compileとPlayer buildを確認済み。最新全依存Stage ProjectはEditMode 169/169、通常PlayMode 7 passed / 6 Explicit skipped、native CoreMIDI packet buffer再利用変更後もmacOS universal Mono / IL2CPP Player build成功（IL2CPP 0 errors / Cloud Diagnostics symbol-upload warning 1件）。Windows x64 Mono cross-buildも成功（Unity Cloud Diagnostics symbol-upload warning 2件）。過去の完全SHA `f238b0a20e0c9b3430f74f2f1c41ed6667ca4257` UPM URL検証は当時の内容を対象とする。CoreMIDI backendを含むPackage SHA `066590edf7353b158865311324cf6c194ca74779` の`git+file` UPM URLでは部分依存Projectへ導入し、EditMode 145 passed / 2 optional skipped、PlayMode 7 passed / 6 Explicit skippedとmacOS universal Mono Player build（0 errors / 0 warnings）を確認した。現行成果のHTTPS UPM導入はcommit未push・release tag未作成のため未検証。Windows IL2CPPは対象OS上でのbuildが必要で、このmacOS hostでは確認できない。

## 導入手順

1. Unity UPM部分を導入する。SetupはNuGetForUnityからR3 1.3.0とSystem.Runtime.CompilerServices.Unsafe 6.0.0を導入する。UnsafeはR3の.NET Standard 2.1 targetが参照し、IL2CPP Linkに必要。CoreにはUnity Audio、AnimatorにはAnimation、VideoにはVideoの標準moduleが必要。
2. Package Managerの「Install package from git URL」でMondmadeVJ0を**1回だけ**追加する。
3. パッケージ内のSetup Windowで必要な外部Integration依存をまとめて導入する。既存の互換版は再導入せず、依存が揃うと対応assemblyが有効になる。

```text
https://github.com/redmond418/MondmadeVJ0.git?path=/Packages/com.redmond.mondmade-vj0#RELEASE_TAG
```

RELEASE_TAGは実在する検証済みtagへ置き換える。完成時の公開版は1.0.0から始めるが、配布tagはまだ作成していない。ライブ用はtagまたはcommitへ固定する。URL形式は[UnityのGit dependencies](https://docs.unity3d.com/6000.3/Documentation/Manual/upm-git.html)に従う。

SetupはInput System、URP、MIDI、OSC、Lasp、Cinemachine、VFX Graphから利用するものを選び、固定版をUnity Package Manager Client APIで追加する。未選択の任意依存は追加しない。Setup WindowからR3本体とUnsafeも導入できる。MondmadeVJ0の機能別packageを追加するbootstrapや、複数のMondmadeVJ0 Git URL登録は不要。

Keijiroのscoped registryが不足していればProject Settingsの設定項目を案内する。OSC forkは固定commitのGit URLで導入し、同じIDのupstreamと二重登録しない。package.jsonのGit依存による自動解決は使わない。ユーザーのmanifestを文字列置換せず、既存registryと設定を保持する。

導入後はRuntime Root・必要Module・RouteHostをScene / Prefabへ配置する。自動的に全Sceneを変更したりSingletonを生成したりしない。R3、Unsafeと必要な外部ライブラリの導入は別途必要だが、MondmadeVJ0自身の登録・更新は1つで済む。

## 開発用QuickStart

`Assets/MondmadeVJ0.Dev/Scenes/APC-mini-mk2-QuickStart.unity`はAPC mini mk2を使う結合検証Scene。`Assets/MondmadeVJ0.Dev/Midi/APC-mini-mk2-QuickStart.asset`は90個のLogical Controlを持つProfile。Scene内のRuntimeにTransport、MIDI、OSC、OSC Transport Sync Moduleを登録してある。Play時にMidiHubのInput / Output ordinalが現在のPCのデバイス列挙と一致していることを確認する。

1. Sceneを開き、InspectorでSurface ProfileとPort ordinalを確認する。
2. Playし、Runtime MonitorでMidiHubのStatusが`Connected`になることを確認する。
3. 左上のPad 1を押すと立方体の表示とPad LEDが切り替わる。Fader 1で球の幅、Transport Phaseで球の高さが変化する。
4. OSC外部同期は`/mondmade/transport`の4個のfloat（BPM、whole beat、fractional beat、running）を、SceneのOSC Transport Sync Adapterへ送る。

Scene内のPort ordinalは開発時のAPC列挙結果を初期値にする。「Add APC mini mk2 Surface」は既存Surfaceで使用中のordinalを避け、`Control` portを優先する（`Notes` portは選ばない）。同名の実機を複数使う場合は、利用側のRtMidi一覧と接続順に合わせて割当を確認する。ordinal選択とSurface競合は合成テスト済み。実機2台同時の確認は二台目を所有しないため対象外。

## 開発・更新・リリース

同時開発は「Install package from disk」で、このcloneの `Packages/com.redmond.mondmade-vj0/package.json` を1つ指定する。共有・リリース時はローカルパスをGit tagへ戻す。利用側の拡張はAssets/<ProjectName>/または独立packageへ置く。

対象はUnity 6000.3系列以降、基準Editorは6000.3.8f1。macOS arm64 / Windows x64のEditorとStandalone（Mono / IL2CPP）を検証する。対象と試験済み結果は区別する。

releaseは1つのversion・tagで更新する。全機能の完成判定に加え、Core必須依存のみ・一部外部依存あり・全依存ありの各構成でGit導入・Editor import・Playerビルドを検証する。機能ごとに別のMondmadeVJ0 packageを登録する操作がないことも確認する。

LICENSE・外部依存通知・CHANGELOG・移行手順を同梱する。公開ライセンスは所有者の判断とし、この文書更新では新設しない。ライセンスは追加していない。NuGetForUnityが生成する`Assets/Packages/`と`Assets/packages.config`は復元物としてGit ignoreする。
