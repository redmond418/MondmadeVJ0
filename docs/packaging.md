# パッケージ構成と導入

更新日：2026-10-22（JST）。ユーザーの指摘を受け、**UPMパッケージは1つに統合する。機能の分離は内部フォルダとasmdefで行う。** 本書は以前の機能別UPM案を置き換える。現時点では設計のみで、packageフォルダ・Setup Window・配布tagはまだ作成していない。

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
│   ├ Tests/                        # 機能別のEditor / Runtime Tests asmdef
│   ├ Samples~/                     # BasicRoutesとIntegration別サンプル
│   ├ Documentation~/
│   ├ README.md
│   ├ CHANGELOG.md
│   └ LICENSE.md
├ Assets/
│ ├ MondmadeVJ0.Dev/                 # 結合検証Scene・実機テスト・開発素材
│ ├ MondmadeVJ0.Generated/           # Generic Accessor生成C#（既定asmdefなし）
│ └ Editor/MondmadeVJ0Bootstrap/     # 開発プロジェクト用の外部依存導入
├ ProjectSettings/
└ docs/
```

開発中はembedded packageとしてこのソースを編集し、.metaもGit管理する。利用側も同じ1パッケージをGit URLまたはローカルpackage.jsonで参照する。Library/PackageCacheやAssets側へ実装を二重配置しない。公開サンプルはSamples~、実機依存の結合試験はAssets/MondmadeVJ0.Devに置く。[UnityのPackage layout](https://docs.unity3d.com/6000.3/Documentation/Manual/cus-layout.html)

## 内部Assemblyと外部依存

package IDは `com.redmond.mondmade-vj0`。namespace / assemblyのprefixは `MondmadeVJ0`。1つのpackage.jsonが全機能を配布する。各機能は独立したRuntime / Editor / Tests assemblyを持ち、Coreは任意Integrationを参照しない。

| フォルダ | Runtime assembly | 内容・外部依存（採用基準版） |
| --- | --- | --- |
| Core | MondmadeVJ0.Core | Route、Source / Modifier、Signal、Clock契約、Root、Unity Object / Transform / AudioSource出力。R3 1.3.0本体とcom.cysharp.r3 1.3.0 |
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

Editor assemblyはRuntime名に `.Editor`、Testsは `.Tests.Editor` / `.Tests.Runtime`を付ける。基本EditorはCoreだけへ依存し、Integration Editorは対応RuntimeとCore Editorへ依存する。Alchemy / UniTask / LitMotionは標準依存に含めない。LitMotion連携を追加する場合も同一パッケージ内の任意Integration assemblyとする。

## 任意Integrationの有効化

全Integrationのソースを同梱し、利用プロジェクトに外部依存が揃った機能だけをコンパイルする。Lasp / Cinemachine / VFX Graph等をすべてpackage.jsonの必須依存にして自動導入する構成にはしない。

各Integration asmdefに必要packageのVersion Definesと同じsymbolのDefine Constraintsを設定する。例：Cinemachineはcom.unity.cinemachineの `[3.1.6,4.0.0)` で `MONDMADE_HAS_CINEMACHINE` を定義し、そのsymbolをassemblyの制約にする。Editor / Testsも同条件、複数依存のMidiは全条件のANDとする。外部assemblyは名前で参照し、任意packageの不存在時に未解決GUIDを残さない。UnityはVersion DefinesのsymbolをDefine Constraintsに利用できる。[Unityの条件付きAssembly](https://docs.unity.com/en-us/engine/6000.7/manual/scripting/compilation-and-code-reload/script-compilation/assembly-definition-files/includes)

互換範囲は採用基準版以上・次major未満を基本とし、URP / VFX Graphは17.3系列の `[17.3.0,17.4.0)` とする。範囲外はSetup Windowで非対応と表示してassemblyを無効にする。既存版を無断でdowngradeしない。

Core Editorは利用可能なIntegrationだけを型登録から列挙する。未導入機能はSetupに「依存未導入」と表示し、Route追加メニューには出さない。外部型を参照するProfile・Prefab・Sceneは該当Samples~以下に置き、依存未導入時に自動importしない。既存Routeが使う依存の除去はD18の欠落型保護に従う。

OSC forkはupstreamとID・versionが同じなので、Setupで必要APIを確認した場合だけ `MONDMADE_OSC_EXTENSIONS` を設定する。OSC asmdefはpackage条件とこのsymbolの両方を要求し、依存の除去・変更でも検査する。

任意依存なし・一部のみ・全依存ありの実プロジェクトで、欠落参照のimportエラーも含めて検証する。条件設定だけで動作確認済みとは扱わない。

## 導入手順

1. 必須依存のR3本体とUnity UPM部分を導入する。NuGetForUnityの手順・採用版・推移依存を固定し、重複DLLを検査する。外部DLLは再梱包しない。
2. Package Managerの「Install package from git URL」でMondmadeVJ0を**1回だけ**追加する。
3. パッケージ内のSetup Windowで必要な外部Integration依存をまとめて導入する。既存の互換版は再導入せず、依存が揃うと対応assemblyが有効になる。

```text
https://github.com/redmond418/MondmadeVJ0.git?path=/Packages/com.redmond.mondmade-vj0#RELEASE_TAG
```

RELEASE_TAGは実在する検証済みtagへ置き換える。完成時の公開版は1.0.0から始めるが、現時点で配布tagが存在するとは扱わない。ライブ用はtagまたはcommitへ固定する。URL形式は[UnityのGit dependencies](https://docs.unity3d.com/6000.3/Documentation/Manual/upm-git.html)に従う。

Setupは必要な外部依存と版を表示してClient.AddAndRemoveで一括要求する。R3本体は事前手順とし、Coreがコンパイル不能でもSetupが利用可能とは説明しない。MondmadeVJ0の機能別packageを追加するbootstrapや、複数のMondmadeVJ0 Git URL登録は不要。

Keijiroのscoped registryが不足していればProject Settingsの設定項目を案内する。OSC forkは固定commitのGit URLで導入し、同じIDのupstreamと二重登録しない。package.jsonのGit依存による自動解決は使わない。ユーザーのmanifestを文字列置換せず、既存registryと設定を保持する。

導入後はRuntime Root・必要Module・RouteHostをScene / Prefabへ配置する。自動的に全Sceneを変更したりSingletonを生成したりしない。必須R3と必要な外部ライブラリの導入は別途必要だが、MondmadeVJ0自身の登録・更新は1つで済む。

## 開発・更新・リリース

同時開発は「Install package from disk」で、このcloneの `Packages/com.redmond.mondmade-vj0/package.json` を1つ指定する。共有・リリース時はローカルパスをGit tagへ戻す。利用側の拡張はAssets/<ProjectName>/または独立packageへ置く。

対象はUnity 6000.3系列以降、基準Editorは6000.3.8f1。macOS arm64 / Windows x64のEditorとStandalone（Mono / IL2CPP）を検証する。対象と試験済み結果は区別する。

releaseは1つのversion・tagで更新する。全機能の完成判定に加え、R3のみ・一部外部依存あり・全依存ありの各構成でGit導入・Editor import・Playerビルドを検証する。機能ごとに別のMondmadeVJ0 packageを登録する操作がないことも確認する。

LICENSE・外部依存通知・CHANGELOG・移行手順を同梱する。公開ライセンスは所有者の判断とし、この文書更新では新設しない。今回は導入契約の修正のみで、実際のpackage作成・外部依存導入はまだ実施しない。
