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

Unity 6000.3.8f1 macOS arm64 Stage Projectで全Runtime / Editor assemblyをcompileし、EditMode 147/147・通常PlayMode 7/7（実機依存Explicit 6件はskip）を確認しています。任意IntegrationのUPM依存を含めないR3＋NuGetForUnity＋Unity標準moduleの構成もcompileし、macOS universal IL2CPPとWindows x64 Mono Player buildを確認しました。Input System＋MIDI＋OSCを加えた部分構成のlocal Git UPM import・compileとmacOS universal Mono Player buildも確認しています。クリーンな消費側Projectでは、全依存構成のEditMode 147/147と通常PlayMode 7/7、Core必須依存だけの構成でEditMode 89 pass / 3 optional Integration skip、部分構成でEditMode 123 pass / 2 URP / VFX skipでした。R3のIL2CPPリンクに必要なUnsafe 6.0.0をSetupから導入する経路も検証しました。Lasp Audio契約、OSC strict Type Tag・UDP loopback / main-thread delivery、8192件Queueの契約試験も通過しました。実入力デバイスを使ったAudio captureは未確認です。APC mini mk2のRtMidi input / output portをOpenし、実機の左上Pad 1がNote 56を送ること、Pad 1のLED点灯とON/OFF payloadを検証しました。Runtime MonitorにはRaw / Modifier / Final / Output、MIDI、Tap / Clock履歴とPerformance計測を実装しています。4ms MIDI Clock lookahead変更後、APC mini mk2・1000 Route・全LED 10Hzの15秒短縮probeは各BPM 5秒でClock欠落 / output drop 0、p99 0.001〜0.004ms、最大遅延0.003〜0.006ms、累積ずれ0.0029〜0.0053msでした。10分×3相の受入ではありません。package commit `e62e5e72c4cf485535e7ecc11987c41c3a65d841`を完全SHA指定したローカルGit UPM URLで再導入し、部分構成でEditMode 123/125（URP / VFXの任意Integration 2件skip）、PlayMode 7 passed / 6 実機依存Explicit skip、macOS universal MonoとWindows x64 Mono Player buildを確認しました。全依存macOS universal Player buildはこの変更後に無進捗となり中断したため未確認です。従来差分では全依存macOS universal Mono / IL2CPPとWindows x64 Mono Player buildを確認済みです。Windows IL2CPP module、Windows上の実機、USB再接続、MIDI Clockの10分負荷と外部受信機実測、公開HTTPS URLからの導入は未確認です。未実施の受け入れ項目は[実装・検証計画](../../docs/implementation-plan.md)に記録します。
