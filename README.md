# MondmadeVJ0

UnityでVJシステムを組むための基盤ライブラリです。Inspectorで `Source → Modifiers → Route State → Outputs` をつなぎ、音楽時間、MIDI Surface、OSC、音声解析、映像・LED出力を扱います。

Runtime、Editor、Setup、Monitorと標準機能カタログの実装を用意しています。受け入れ試験には未完了項目が残っているため、プロジェクト全体はまだ完成扱いにしていません。現在の実装、環境別の確認結果、残作業は[実装と検証](docs/implementation-plan.md)を参照してください。

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
- 全依存Stage ProjectのEditMode 249/249、PlayMode 10 passed / 7 Explicit skippedを通過
- 現行実装のmacOS universal Mono / IL2CPP Player buildを確認
- APC mini mk2でPad / Fader / Track Buttonの入力、緑色LED、USB再接続後の状態保持を確認
- Background Music入力の短時間probeを確認

MIDI ClockのA14規定負荷、外部受信機の到着時刻、Windows IL2CPP / Windows上の実行、現行成果の公開HTTPS UPM導入は未完了です。package SHA `e436983ae974c6ce27a5e1f8808cbcd56845338b` のWindows x64 Mono Player buildは成功しました。Windows上でのPlayer実行・MIDI機器とWindows IL2CPPは未確認です。検証済み範囲と制限は[実装と検証](docs/implementation-plan.md)を正とします。

## 設計上の前提

- MIDIコントローラー切断中は最後の値を保持し、再接続で初期化しません。
- Gridの`1/4`は1拍の1/4です。
