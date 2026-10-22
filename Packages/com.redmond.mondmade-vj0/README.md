# Mondmade VJ0

Inspectorで組み立てる型安全なVJ制御Route基盤。Core、Musical Transport、MIDI / APC mini mk2、OSC、Audio、Unity Outputを1つのUPMパッケージにまとめ、機能ごとにassemblyを分けています。

## Requirements

- Unity 6000.3以降
- R3 NuGet 1.3.0とSystem.Runtime.CompilerServices.Unsafe 6.0.0（同梱Setupから導入）
- Coreで使うUnity Audio module。Animator / Video出力には対応するUnity標準module
- Input System、URP、MIDI、OSC、Lasp、Cinemachine、VFX Graphの各Integrationは任意

## Setup

1. Package ManagerからこのUPMパッケージを導入します。
2. `Tools > Mondmade VJ0 > Setup`を開き、R3と必須の推移依存を導入し、必要なIntegrationだけを選んで追加します。
3. Sceneへ`MondmadeVJ0Runtime`と必要なModule、`RouteHost`を配置します。
4. Route InspectorでSource、Modifier、Outputを追加します。Play前ValidationとRuntime Monitorで設定と状態を確認します。

APC mini mk2の結合例はリポジトリの[`Assets/MondmadeVJ0.Dev`](../../Assets/MondmadeVJ0.Dev/README.md)、機能一覧は[機能カタログ](../../docs/feature-catalog.md)、詳細契約は[仕様](../../docs/spec.md)と[設計判断](../../docs/design-notes.md)、依存導入は[パッケージ構成](../../docs/packaging.md)を参照してください。

## Verification status

Unity 6000.3.8f1 macOS arm64 Stage Projectとlocal file package消費側でRuntime / Editor assemblyをcompileし、最新差分のEditMode 135 passed / 2 optional skipped、通常PlayMode 7 passed / 6 Explicit skippedを確認しています。任意IntegrationのUPM依存を含めないR3＋NuGetForUnity＋Unity標準moduleの構成ではmacOS universal IL2CPPとWindows x64 Mono Player buildを確認しました。Input System＋MIDI＋OSCを加えた部分構成は完全SHA `493a8d3b1a82c7c3bf1fd7debf78db1b81ef502b` のGit UPM URLでも導入・検証済みです。最新差分の部分依存消費側はmacOS StandaloneOSX universal MonoとWindows x64 Mono Player buildが成功しています。全依存のStage ProjectでもEditMode 159/159、通常PlayMode 7 passed / 6 Explicit skipped、macOS StandaloneOSX universal Mono Player buildが成功しています。QuickStart assetの全64 Pad入力／出力Note番号もUnity AssetDatabase testで照合しています。R3のIL2CPPリンクに必要なUnsafe 6.0.0をSetupから導入する経路も検証しました。Lasp Audio契約、OSC strict Type Tag・UDP loopback / main-thread delivery、8192件Queueの契約試験も通過しました。実入力デバイスを使ったAudio captureは未確認です。APC mini mk2は実機の左上Pad 1からNote 56を受信し、Bulk SysExでPad 1を緑点灯することをユーザーが目視確認しました。Runtime MonitorにはRaw / Modifier / Final / Output、MIDI、Tap / Clock履歴とPerformance計測を実装しています。最新差分での実機61秒・全LED 10Hz probeは約800 LED control states/sを送信しましたが、BPM 120 / 174 / 240の累積ずれは0.777 / 7.488 / 2.655msで、Clock欠落 / output dropは全相0でした。174 / 240 BPMで1ms目標を超えA14未合格です。各10分の受入と外部受信機実測、最新差分のGit UPM URL / 公開HTTPS URL、Windows IL2CPP / Windows実機、USB再接続、Fader / Track Buttonの実機入力照合は未確認です。未実施の受け入れ項目は[実装・検証計画](../../docs/implementation-plan.md)に記録します。
