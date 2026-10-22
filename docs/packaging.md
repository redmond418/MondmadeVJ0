# パッケージ構成と導入

更新日：2026-10-23（JST）。**UPMパッケージは1つに統合し、機能の分離は内部フォルダとasmdefで行う。** package、Setup Window、Core / Integration Editor、EditMode / PlayMode testsを配置した。最新差分を含むUnity 6000.3.8f1 macOS arm64のlocal file package消費側でEditMode 135 passed / 2 optional skipped、PlayMode 7 passed / 6 Explicit skipped、macOS StandaloneOSX universal MonoとWindows x64 Mono Player buildを確認した。全Integrationを含むStage ProjectもEditMode 159/159、通常PlayMode 7 passed / 6 Explicit skipped、macOS universal Mono / IL2CPP Player buildが成功した。IL2CPP buildはx86_64 + arm64 universal。StandaloneOSX IL2CPPのPlayMode Test Runnerで「All test(s) succeeded」と表示し、AOT / stripping後のテストPlayer実行まで確認した。Player終了後にEditorが待機状態となりNUnit XML未出力のため、実行件数は未確定である。QuickStart APC Profileの全64 Pad note mappingもUnity AssetDatabase testで確認した。AKAI Bulk LED SysExによるPad 1緑点灯はユーザーが目視確認し、同一出力workerのClock / LED結合probeも成功した。EditModeにはMaterial / Volume資源lease、Transform / Generic / Shader書込競合診断、Video prepare待ちとFault通知、Lasp AudioのdBFS / Spectrum / Trigger、OSC UDP / queue契約の試験を含む。完全SHA `493a8d3b1a82c7c3bf1fd7debf78db1b81ef502b` の`git+file` URLから最新差分を新規導入し、EditMode 135 passed / 2 optional skipped、PlayMode 7 passed / 6 Explicit skipped、macOS StandaloneOSX universal MonoとWindows x64 Mono Player buildを確認した。GitHub repositoryはpublicでHTTPS clone URLも確認したが、ローカルmainに未pushのcommitがあり、公開release tagもないため、現行成果のHTTPS UPM導入は未検証である。最新の実機A14 61秒probeは全BPMでLED約800 control states/s、p99 0.001ms、Clock欠落 / output drop 0だった。BPM 120 / 174 / 240の累積期限ずれは0.777 / 7.488 / 2.655msで174 / 240が1ms目標を超えた。174 BPMの2ms spin比較では累積期限ずれ7.013msで1ms目標に届かなかった。1ms spinは120 BPMで0.686msに収まったが、174 / 240 BPMは6.129 / 2.762ms、240 BPMにClock欠落2回が出た。0.5ms spinも174 BPMで累積ずれ2.495msとなった。Clock生成workerのpriority比較はNormal / Utilityの3ms spinで2.050ms、1ms spin併用で2.077ms、いずれも1ms目標を超え、採用案は得られていない。A14の規定10分×3速度と外部受信機測定は未完了。A15 Fader / Track Button入力probeは今回の90秒再実施でもraw MIDIを受信せず、型付き入力は未確認。USB再接続・Windows IL2CPP / Windows実機・Player上の外部連携・実機動作は未確認。全依存Stage ProjectのmacOS universal Mono Player buildは最新Packageで成功した。

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

任意依存なし・一部のみ・全依存ありの実プロジェクトで、欠落参照のimportエラーも含めて検証する。NuGetForUnity、R3、Unsafe、Unity標準moduleだけの構成はlocal UPM packageのimport・compileを確認し、macOS universal IL2CPPとWindows x64 Mono Player buildも成功した。Input System＋MIDI＋OSC部分構成の完全SHA Git URL導入も以前の差分で確認済み。最新の32-pad Bulk SysExとClock差分はlocal file package参照でimport・compile・EditMode / PlayMode tests、macOS StandaloneOSX universal MonoとWindows x64 Mono Player buildを確認した。完全SHA `493a8d3b1a82c7c3bf1fd7debf78db1b81ef502b` の`git+file` URLからも新規導入し、同じEditMode / PlayMode testsと両Player buildを確認した。公開HTTPS URL / release tagからの導入はcommitをpushしておらずrelease tagも未作成のため未検証。全依存構成も現在のPackageでEditMode 159/159、通常PlayMode 7 passed / 6 Explicit skipped、macOS universal Mono / IL2CPP Player buildを確認した。IL2CPPはx86_64 + arm64 universal build成功。PlayMode Test RunnerのPlayer表示は全テスト成功。Editor待機でNUnit XML未出力のため実行件数は未確定。変更前は全依存Windows x64 Mono buildが成功した。Windows IL2CPPはUnity 6000.3.8f1 Hubのmodule一覧に存在せず、未検証である。

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

Scene内のPort ordinalは開発時のAPC列挙結果を初期値にする。別の機器順序または別PCでは、利用側のRtMidi一覧に合わせて更新する。

## 開発・更新・リリース

同時開発は「Install package from disk」で、このcloneの `Packages/com.redmond.mondmade-vj0/package.json` を1つ指定する。共有・リリース時はローカルパスをGit tagへ戻す。利用側の拡張はAssets/<ProjectName>/または独立packageへ置く。

対象はUnity 6000.3系列以降、基準Editorは6000.3.8f1。macOS arm64 / Windows x64のEditorとStandalone（Mono / IL2CPP）を検証する。対象と試験済み結果は区別する。

releaseは1つのversion・tagで更新する。全機能の完成判定に加え、Core必須依存のみ・一部外部依存あり・全依存ありの各構成でGit導入・Editor import・Playerビルドを検証する。機能ごとに別のMondmadeVJ0 packageを登録する操作がないことも確認する。

LICENSE・外部依存通知・CHANGELOG・移行手順を同梱する。公開ライセンスは所有者の判断とし、この文書更新では新設しない。ライセンスは追加していない。NuGetForUnityが生成する`Assets/Packages/`と`Assets/packages.config`は復元物としてGit ignoreする。
