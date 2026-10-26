# Mondmade VJ0

Inspectorで組み立てる型安全なVJ制御Route基盤。Core、Musical Transport、MIDI / APC mini mk2、OSC、Audio、Unity Outputを1つのUPMパッケージにまとめ、機能ごとにassemblyを分けています。

## Requirements

- Unity 6000.3以降
- R3 NuGet 1.3.0とSystem.Runtime.CompilerServices.Unsafe 6.0.0（同梱Setupから導入）
- Coreで使うUnity Audio module。Animator / Video出力には対応するUnity標準module
- Input System、URP、MIDI、OSC、Lasp、Cinemachine、VFX Graphの各Integrationは任意

## Setup

Package Managerの「Add package from git URL」に次のURLを入力します。

```text
https://github.com/redmond418/MondmadeVJ0.git?path=/Packages/com.redmond.mondmade-vj0#main
```

または、Projectの`Packages/manifest.json`の既存`dependencies` object内に次の1行を追加します。ほかの依存は残し、項目間のカンマを保ってください。

```json
"com.redmond.mondmade-vj0": "https://github.com/redmond418/MondmadeVJ0.git?path=/Packages/com.redmond.mondmade-vj0#main"
```

`#main`は開発ブランチです。チームや本番環境では検証したrelease tagまたはcommit SHAへ置き換えてください。インストール後、`Tools > Mondmade VJ0 > Setup`を開き、R3と必須の推移依存を導入して必要なIntegrationを選びます。その後、Hierarchyの`GameObject > Mondmade VJ0 > Runtime`から`MondmadeVJ0Runtime`付きGameObjectを作り、必要なModule、`RouteHost`を配置します。Route InspectorでSource、Modifier、Outputを追加し、Play前ValidationとRuntime Monitorで設定と状態を確認します。

APC mini mk2の結合例はリポジトリの[`Assets/MondmadeVJ0.Dev`](../../Assets/MondmadeVJ0.Dev/README.md)、機能一覧は[機能カタログ](../../docs/feature-catalog.md)、詳細契約は[仕様](../../docs/spec.md)と[設計判断](../../docs/design-notes.md)、依存導入は[パッケージ構成](../../docs/packaging.md)を参照してください。

## APC mini mk2 Profileの利用

UPM packageには、APC mini mk2用のSurface Profileを含めています。先にSetupからMIDI Integrationの依存を導入し、Package ManagerでMondmade VJ0を選んで**Samples**から**APC mini mk2 Surface Profile**をimportしてください。Profileはvelocity pad 64個、Track Button 8個、Scene Button 8個、Shift、Fader 9個の計90 Controlを定義します。Padはchannelを限定せずNote 0–63、Track / Scene / Shiftはchannel 0のNote 100–107 / 112–119 / 122、Faderはchannel 0のCC 48–56を使います。`MidiHub`で接続デバイスのInput / Output port ordinalを指定してください。

## Verification status

Unity 6000.3.8f1のStage ProjectではEditMode 255/255、通常PlayMode 12/12が通過しました（Explicit probe 7件は通常実行でskip）。実装検証対象commit `b170336c3dd842077aca4d337a97c9c129c37b8e` のInput System / MIDI / OSC構成の隔離Git UPM consumerでもEditMode 191 passed / 2 skipped、PlayMode 12 passed / 6 Explicit skipped、universal macOS Mono Player buildを確認しました。source commit `215b48c` のuniversal IL2CPP Playerもx86_64 / arm64両方で起動し、AOT accessorの実値、stripping後の動作、RtMidi接続状態を確認しています。Windows x64 Mono cross-buildの記録はありますが、Windows上での実行は今回の受入範囲外です。

MIDI ClockのA14送信側は、ユーザー合意により累積期限ずれ上限を5msへ改訂し、最新10分×3速度測定で合格しました。120 / 174 / 240 BPMの累積ずれは4.5406 / 3.7213 / 4.6805ms、Clock missed / output dropは全相0です。保存XMLは旧1ms assertionに対してFailedですが、failureはこの累積ずれ3件だけです。外部受信機の実到着時刻は別観測として未測定で、送信側合格を妨げません。

APC mini mk2ではPad 1、Fader 1、Track Button 1の型付き入力、緑色LED、USB切断中の値保持と再接続後のLED同期を実機確認しました。Background Music入力は8秒probeで2,984 normalized samples・peak 0.797を確認し、音声データは保存していません。約30分の高負荷調査ではUnity RSSは上下後に戻り、持続的な増加やメモリ不足を再現しませんでした。ChatGPTとUnityの同時使用時に発生したメモリ圧迫は一時的な負荷とみて、追加調査は行わない方針です。

IL2CPPのStage専用probeでは`ComponentAccessorGenerator.Add`が生成した`Gain` accessorをPlayerで実行し、stripping有効時も期待値`0.625`を確認しました。RtMidiはPlayer logで`Connected`を報告しました。probe harnessは配布Sceneには含めていません。Buildはerrors 0、Cloud Diagnosticsのsymbol upload設定に関するwarning 1件で、署名と両architectureを確認しました。

公開HTTPS UPM導入は現行commitのpushとrelease tagが未実施のため未検証です。これはリリース時の確認項目で、ローカルのA01〜A24受入とは分けています。A14・A20・APC mini mk2の実機probe、Playerの詳細な証跡と受入範囲は[実装・検証計画](../../docs/implementation-plan.md)を参照してください。
