# パッケージ構成と導入

更新日：2026-10-24（JST）。**UPMパッケージは1つに統合し、機能の分離は内部フォルダとasmdefで行う。** 宣言したローカル受入範囲A01〜A24を検証済み。実装検証対象commit `b170336c3dd842077aca4d337a97c9c129c37b8e` のInput System / MIDI / OSC構成の隔離Git UPM consumerでEditMode 191 passed / 2 skipped、PlayMode 12 passed / 6 Explicit skipped、universal macOS Mono Player buildを確認した。別のStageではsource commit `215b48c` に対するmacOS universal IL2CPP Player起動、AOT accessor、stripping、RtMidi接続状態も確認済み。A14送信側の10分×3速度は、累積ずれ上限をユーザー合意の5msへ改訂した基準で合格。APC mini mk2のA17実機Pad・USB切断 / 再接続と、A20 Background Music入力（2ch、8秒、2,984 normalized samples・peak 0.797）も確認済み。メモリ調査では持続的リークやメモリ不足を再現しておらず、追加調査は不要とした。Windows上でのPlayer実行は今回の受入対象外。公開HTTPS UPM導入確認は公開push・release tagを伴うリリース確認として扱い、まだ実施していない。詳細と証跡は[実装・検証計画](implementation-plan.md)を参照。

2026-10-24、検証対象commit `a99e571094e6b58fc9282208ae5e3366ecebf398` を指定した`git+file` UPM consumerで全依存・部分依存・Core必須依存のみを検証した。各依存構成の詳細とXML保存先は[実装・検証計画](implementation-plan.md#2026-10-24-検証対象commitのgit-upmplayer検証)を参照。全依存consumerからmacOS universal Mono PlayerとWindows x64 Mono Playerをbuildした。この当時のStageではIL2CPP dSYM生成がディスク不足で失敗したが、後続の現行source commitで再buildとPlayer起動に成功した（下記記録）。

2026-10-24、Input System / MIDI / OSC構成で実装検証対象commit `b170336c3dd842077aca4d337a97c9c129c37b8e`を完全SHA固定した隔離Git UPM consumerをUnity 6000.3.8f1で確認した。manifestとlock fileは同SHAを解決し、EditMode 191 passed / 2 skipped、PlayMode 12 passed / 6 Explicit skipped、各0 failed。consumerからのuniversal macOS Mono Player buildも成功し、実行ファイルがx86_64 + arm64であることを`lipo`で確認した。consumer Player自体は起動していない。公開HTTPS URL確認とは分けて記録する。XMLとbuild artifactは[実装・検証計画](implementation-plan.md)に記載した。

過去のcommit `e436983` ではStandaloneOSX universal Mono / IL2CPP buildが成功しているが、これは現行コードのIL2CPP buildを証明しない。以前のWindows Build Profile / Pipeline経由のunsupported target errorsは、現行commitの標準Unity CLI Mono build成功で置き換えた。過去commitに対するStandaloneOSX IL2CPP Test Runnerは終了後NUnit XMLがなく、実行件数は未確定。

2026-10-23の後続A14 smokeではCoreMIDI計測点を`MIDISend`直前へ修正した。各61秒・120 / 174 / 240 BPMの累積submission driftは3.9339 / 2.9742 / 5.1804msで、当時の1ms基準を超えた。旧計測点による値と外部receiver測定の区別は[実装・検証計画](implementation-plan.md)に記録する。

2026-10-24、3msの送信spin窓を使った20秒×3速度smokeはClock missed / drop 0、p99 0.013 / 0.012 / 0.111ms、最大4.059 / 1.210 / 7.964ms、累積ずれ3.5227 / 1.2057 / 7.9616msだった。短縮smokeの結果はPassだが累積ずれは当時の1ms条件を超え、10分負荷・外部receiver実測でもない短縮比較として記録した。詳細は[実装・検証計画](implementation-plan.md)に記録する。

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
│   ├ Samples~/APC mini mk2/         # APC mini mk2 Surface Profile
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

OSC forkは固定commit `7ef8fa692030079d2ad6d1a314680b96fbd9a328` を参照する。Type Tag APIの直接呼び出しとIL2CPP preservation descriptorを実装した。macOS universal Mono / IL2CPP Playerから別プロセスのOSC peerへの双方向送受信を確認済み。別PCを使う実ネットワーク確認は未実施。

任意依存なし構成はR3 / Unsafe / NuGetForUnity / Unity標準moduleだけでlocal package import・compileを確認し、以前のpackage sourceでmacOS universal IL2CPPとWindows x64 Mono Player buildも成功した。2026-10-24に検証対象commit `a99e571094e6b58fc9282208ae5e3366ecebf398` の全依存・部分依存・Core必須依存のみconsumerからimport / compile / Player buildを検証した結果は冒頭と[実装・検証計画](implementation-plan.md#2026-10-24-検証対象commitのgit-upmplayer検証)を参照。古いSHA `f238b0a20e0c9b3430f74f2f1c41ed6667ca4257` や`066590edf7353b158865311324cf6c194ca74779`の結果は当時のpackage内容を対象とする。現行source commit `215b48c` ではmacOS universal IL2CPP Player build・起動が成功した。現行成果のHTTPS UPM導入だけはpush・release tag未実施のため未検証。Windows上の実行・IL2CPPは今回の受入対象外。

## 導入手順

1. Unity UPM部分を導入する。SetupはNuGetForUnityからR3 1.3.0とSystem.Runtime.CompilerServices.Unsafe 6.0.0を導入する。UnsafeはR3の.NET Standard 2.1 targetが参照し、IL2CPP Linkに必要。CoreにはUnity Audio、AnimatorにはAnimation、VideoにはVideoの標準moduleが必要。
2. Package Managerの「Install package from git URL」でMondmadeVJ0を**1回だけ**追加する。
3. パッケージ内のSetup Windowで必要な外部Integration依存をまとめて導入する。既存の互換版は再導入せず、依存が揃うと対応assemblyが有効になる。

```text
https://github.com/redmond418/MondmadeVJ0.git?path=/Packages/com.redmond.mondmade-vj0#main
```

`#main`は開発ブランチを参照する。チームや本番環境では検証済みtagまたはcommit SHAへ置き換える。完成時の公開版は1.0.0から始めるが、配布tagはまだ作成していない。URL形式は[UnityのGit dependencies](https://docs.unity3d.com/6000.3/Documentation/Manual/upm-git.html)に従う。

SetupはInput System、URP、MIDI、OSC、Lasp、Cinemachine、VFX Graphから利用するものを選び、固定版をUnity Package Manager Client APIで追加する。未選択の任意依存は追加しない。Setup WindowからR3本体とUnsafeも導入できる。MondmadeVJ0の機能別packageを追加するbootstrapや、複数のMondmadeVJ0 Git URL登録は不要。

Keijiroのscoped registryが不足していればProject Settingsの設定項目を案内する。OSC forkは固定commitのGit URLで導入し、同じIDのupstreamと二重登録しない。package.jsonのGit依存による自動解決は使わない。ユーザーのmanifestを文字列置換せず、既存registryと設定を保持する。

導入後はHierarchyの`GameObject > Mondmade VJ0 > Runtime`からRuntimeテンプレートを作成する。`MondmadeVJ0Runtime`の子にTransport、導入済みのMIDI / OSC Module、OSC Transport Sync Adapter、RouteHostを作り、Runtimeの配列へ登録する。RouteHostには利用側のRouteを追加する。自動的に全Sceneを変更したりSingletonを生成したりしない。R3、Unsafeと必要な外部ライブラリの導入は別途必要だが、MondmadeVJ0自身の登録・更新は1つで済む。

## 開発用QuickStart

APC mini mk2用の90 Logical Control ProfileはUPM packageの`Samples~/APC mini mk2`に同梱し、Package Managerから**APC mini mk2 Surface Profile**としてimportできる。開発用QuickStart Sceneにも同じProfileを置く。

`Assets/MondmadeVJ0.Dev/Scenes/APC-mini-mk2-QuickStart.unity`はAPC mini mk2を使う結合検証Scene。`Assets/MondmadeVJ0.Dev/Midi/APC-mini-mk2-QuickStart.asset`はScene用のProfile複製。Scene内のRuntimeにTransport、MIDI、OSC、OSC Transport Sync Moduleを登録してある。Play時にMidiHubのInput / Output ordinalが現在のPCのデバイス列挙と一致していることを確認する。

1. Sceneを開き、InspectorでSurface ProfileとPort ordinalを確認する。
2. Playし、Runtime MonitorでMidiHubのStatusが`Connected`になることを確認する。
3. 左上のPad 1を押すと立方体の表示とPad LEDが切り替わる。Fader 1で球の幅、Transport Phaseで球の高さが変化する。
4. OSC外部同期は`/mondmade/transport`の4個のfloat（BPM、whole beat、fractional beat、running）を、SceneのOSC Transport Sync Adapterへ送る。

Scene内のPort ordinalは開発時のAPC列挙結果を初期値にする。「Add APC mini mk2 Surface」は既存Surfaceで使用中のordinalを避け、`Control` portを優先する（`Notes` portは選ばない）。同名の実機を複数使う場合は、利用側のRtMidi一覧と接続順に合わせて割当を確認する。ordinal選択とSurface競合は合成テスト済み。実機2台同時の確認は二台目を所有しないため対象外。

## 開発・更新・リリース

同時開発は「Install package from disk」で、このcloneの `Packages/com.redmond.mondmade-vj0/package.json` を1つ指定する。共有・リリース時はローカルパスをGit tagへ戻す。利用側の拡張はAssets/<ProjectName>/または独立packageへ置く。

対象Unityは6000.3系列以降、基準Editorは6000.3.8f1。今回の実行受入はmacOS arm64を中心に行う。Windows x64 Mono cross-buildの記録はあるが、Windows上でのPlayer実行・MIDI・IL2CPPはユーザー判断により受入対象外とする。

releaseは1つのversion・tagで更新する。全機能の完成判定に加え、Core必須依存のみ・一部外部依存あり・全依存ありの各構成でGit導入・Editor import・Playerビルドを検証する。機能ごとに別のMondmadeVJ0 packageを登録する操作がないことも確認する。Windows上の実行・MIDI・IL2CPPは現行受入範囲から除外する。

LICENSE・外部依存通知・CHANGELOG・移行手順を同梱する。公開ライセンスは所有者の判断とし、この文書更新では新設しない。ライセンスは追加していない。NuGetForUnityが生成する`Assets/Packages/`と`Assets/packages.config`は復元物としてGit ignoreする。
