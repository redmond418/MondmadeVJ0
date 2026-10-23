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

Unity 6000.3.8f1の最新全依存Stage ProjectではEditMode 169/169が通過し、通常PlayModeは7 passed / 6 Explicit skippedです。macOS universal Mono / IL2CPP Player buildとWindows x64 Mono cross-buildは成功しました。Windows buildにはUnity Cloud Diagnosticsのsymbol-upload warningが2件あります。StandaloneOSX IL2CPP Test Runnerは全テスト成功と表示しましたが、NUnit XMLが出力されず実行件数は未確定です。

MIDI ClockのA14は未合格です。CoreMIDI timestamped backendの8ms先行予約では61秒×3速度smokeのClock欠落 / output dropは0でしたが、submission driftは3.219 / 4.011 / 4.304msで1ms基準を超えました。外部受信機での期限出力実測は未実施です。stage-onlyの10 / 12ms比較も一貫して1ms以下にならず、20msはClock欠落・frame cadence悪化を生じたため、実装は8ms設定のままです。以前の10分×3速度RtMidi実測も欠落・最大遅延・累積ずれの条件で不合格です。

APC mini mk2の左上Pad 1からNote 56を受信し、Pad 1の緑点灯を確認しました。Fader / Track Buttonは90秒の実機probeを7回行いましたが、全回Raw MIDI 0件で型付き入力は未確認です。APC mini mk2は接続状態を維持しており、USB切断・再接続は未試験です。

CoreMIDI backendを含むPackage SHA `066590edf7353b158865311324cf6c194ca74779` を指定したローカル`git+file` UPM URLからの導入では、部分依存ProjectでEditMode 145 passed / 2 optional skippedを確認しました。公開HTTPS UPM URLからの導入は、現行commitのpushとrelease tagが未実施のため未検証です。Windows IL2CPP / 実機MIDI、実Audio device capture、外部受信機測定も未確認です。受入項目の詳細と個別測定値は[実装・検証計画](../../docs/implementation-plan.md)を参照してください。
