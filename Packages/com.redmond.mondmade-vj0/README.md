# Mondmade VJ0

Inspectorで組み立てる型安全なVJ制御Route基盤。Core、Musical Transport、MIDI / APC mini mk2、OSC、Audio、Unity Outputを1つのUPMパッケージにまとめ、機能ごとにassemblyを分けています。

## Requirements

- Unity 6000.3以降
- R3 NuGet 1.3.0（同梱Setupから導入）
- Coreで使うUnity Audio module。Animator / Video出力には対応するUnity標準module
- Input System、URP、MIDI、OSC、Lasp、Cinemachine、VFX Graphの各Integrationは任意

## Setup

1. Package ManagerからこのUPMパッケージを導入します。
2. `Tools > Mondmade VJ0 > Setup`を開き、R3を導入し、必要なIntegrationだけを選んで追加します。
3. Sceneへ`MondmadeVJ0Runtime`と必要なModule、`RouteHost`を配置します。
4. Route InspectorでSource、Modifier、Outputを追加します。Play前ValidationとRuntime Monitorで設定と状態を確認します。

APC mini mk2の結合例はリポジトリの[`Assets/MondmadeVJ0.Dev`](../../Assets/MondmadeVJ0.Dev/README.md)、機能一覧は[機能カタログ](../../docs/feature-catalog.md)、詳細契約は[仕様](../../docs/spec.md)と[設計判断](../../docs/design-notes.md)、依存導入は[パッケージ構成](../../docs/packaging.md)を参照してください。

## Verification status

Unity 6000.3.8f1 macOS arm64 Stage Projectで全Runtime / Editor assemblyをcompileし、EditMode 45/45・通常PlayMode 6/6を確認しています。R3のみとInput System＋MIDI＋OSCの部分構成でもlocal UPM packageのcompileを確認しました。APC mini mk2のRtMidi input / output portをOpenし、実機の左上Pad 1がNote 56を送ること、LED ON/OFF payloadを検証しました。macOS universal x86_64 + arm64でMono / IL2CPP、Windows x64でMono Player buildが成功しています。macOS IL2CPP linkerはbaselibの最低macOS版に関する警告を出し、Cloud Diagnostics symbol uploadは認証情報がなく失敗しました。Git URLでの新規導入、Windows IL2CPPとWindows上の実機、物理LEDの目視、USB再接続、および長時間MIDI Clock性能は別途検証が必要です。未実施の受け入れ項目は[実装・検証計画](../../docs/implementation-plan.md)に記録します。
