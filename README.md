# MondmadeVJ0

UnityでVJシステムを組むための基盤ライブラリです。Inspectorで `Source → Modifiers → Route State → Outputs` をつなぎ、音楽時間、MIDI Surface、OSC、音声解析、映像・LED出力を扱います。

Runtime、Editor、Setup、Monitorと標準機能カタログを実装し、宣言した受入範囲のA01〜A24をローカルで検証しました。公開HTTPS UPMからの導入だけは、pushとrelease tagを行っていないため未検証です。現在の実装、環境別の確認結果、残作業は[実装と検証](docs/implementation-plan.md)を参照してください。

## ドキュメント

| 文書 | 内容 |
| --- | --- |
| [仕様](docs/spec.md) | 採用方針、責務、API契約、標準機能の範囲 |
| [機能カタログ](docs/feature-catalog.md) | Source・Modifier・Outputの全項目 |
| [設計判断と動作契約](docs/design-notes.md) | 確定したD01〜D20の動作 |
| [パッケージ構成と導入](docs/packaging.md) | 単一UPM、内部asmdef、Git URLと依存関係 |
| [プロジェクト調査・移植元](docs/project-audit.md) | 環境、既存システムの参照箇所、依存関係 |
| [実装と検証](docs/implementation-plan.md) | 受け入れ条件、検証結果、未完了項目 |
| [会話の設計本文](docs/reference/conversation-design.md) | 移植元の設計資料。保存用の原文 |

## パッケージ構成

ライブラリは `Packages/com.redmond.mondmade-vj0/` の単一UPMパッケージです。機能別Runtime / Editor assemblyで分離し、任意Integrationは対応する外部依存が導入された場合だけ有効になります。開発Sceneと実機結合検証は `Assets/MondmadeVJ0.Dev/` に置いています。

開発中はPackage Managerの「Install package from disk」で、このclone内の `Packages/com.redmond.mondmade-vj0/package.json` を選択できます。共有用Git URLは[パッケージ導入手順](docs/packaging.md)に記載しています。公開HTTPS URLとrelease tagはまだ検証していません。

## QuickStart

`Assets/MondmadeVJ0.Dev/Scenes/APC-mini-mk2-QuickStart.unity` はAPC mini mk2との結合検証Sceneです。手順は[パッケージ導入手順](docs/packaging.md#開発用quickstart)を参照してください。SceneでProfileと現在のMIDI Portを選び、Runtime Monitorの接続状態を確認してからPad 1やFader 1を操作します。

## 開発環境と検証

- Unity 6000.3.8f1、macOS arm64 Editor
- 最新全依存Stage ProjectのEditMode 255/255、通常PlayMode 12/12を通過（Explicit probe 7件は通常実行でskip）
- macOS universal Mono Player buildを確認。現行sourceのuniversal IL2CPP Playerもx86_64 / arm64両方で起動し、AOT accessor、stripping後の実行、RtMidi接続状態を確認
- Windows x64 Mono cross-buildも確認。Windows上での実行は今回の受入範囲外
- APC mini mk2でPad / Fader / Track Button入力、緑色LED、USB切断・再接続時の状態保持とLED同期を確認
- Background Music入力の8秒probeで2,984 samplesを確認（音声データは保存していません）
- A14 MIDI Clock送信側は、累積期限ずれ上限をユーザー合意の5msへ改訂し、最新の10分×3速度測定が合格

ローカルの受入で残っている項目はありません。公開HTTPS UPM導入の確認には、現行commitのpushとrelease tagが必要です。A14の最新10分×3速度は、累積ずれ上限を5msへ改訂した基準で合格しています（4.5406 / 3.7213 / 4.6805ms）。保存XMLは改訂前の1ms assertionで生成された履歴です。外部受信機の実到着時刻は参考観測として未測定で、A14送信側の合否条件には含めません。Windows上での実行は今回の受入範囲外です。30分負荷調査では持続的なUnity RSS増加やメモリ不足を再現しませんでした。検証済み範囲と制限は[実装と検証](docs/implementation-plan.md)を参照してください。

## 設計上の前提

- MIDIコントローラー切断中は最後の値を保持し、再接続で初期化しません。
- Gridの`1/4`は1拍の1/4です。
