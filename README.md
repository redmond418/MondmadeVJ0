# MondmadeVJ0

UnityでVJシステムを制作するための基盤ライブラリ。Inspectorで組む `Source → Modifiers → Route State → Outputs` を中心に、音楽時間、MIDI Surface、映像とLEDへの出力を共通化する。

現在はUnityプロジェクトの土台と設計ドキュメントを用意した状態で、MondmadeVJ0のRuntime・Editorコードは未実装。設計に挙げた機能一式を完成要件とし、機能を削った「初版」は定義しない。

## ドキュメント

| 文書 | 内容 |
| --- | --- |
| [仕様](docs/spec.md) | 採用方針、責務、API契約、標準機能の範囲 |
| [機能カタログ](docs/feature-catalog.md) | Source・Modifier・Outputの全項目と実装状況 |
| [設計判断と動作契約](docs/design-notes.md) | 原文の表記整理と確定したD01〜D20の動作 |
| [パッケージ構成と導入](docs/packaging.md) | 単一UPMのソース配置、内部asmdef、Git URLでの導入 |
| [プロジェクト調査・移植元](docs/project-audit.md) | 現在の環境、既存3システムの参照箇所、依存関係 |
| [実装と検証](docs/implementation-plan.md) | 依存順の作業項目と完成判定条件 |
| [会話の設計本文](docs/reference/conversation-design.md) | 「Unity基盤機能提案」最終設計・全65章の保存資料 |

実装時は仕様・設計補足・機能カタログを合わせて参照する。会話の原文は経緯確認用に残す。新しい設計判断を加える場合は、会話由来の要件と区別して記録する。

## ライブラリの配置と導入方針

ライブラリ全体を`Packages/com.redmond.mondmade-vj0/`の単一embedded UPMパッケージに置く。内部は機能別フォルダ・asmdefとRuntime / Editor / Testsで分け、デモと結合検証だけを`Assets/MondmadeVJ0.Dev/`に置く。名前空間は`MondmadeVJ0`以下。

他のVJプロジェクトにはGit URLを1つ登録して導入する。全機能のソースを同梱し、任意Integrationは外部依存が揃った場合だけ有効になる。必須R3の導入手順と、必要な外部ライブラリをまとめて追加するSetup Windowを提供する。ソースも編集する場合はローカルpackage.jsonを参照する。実際のpackageフォルダ・配布tag・Setup Windowはまだ作成していない。

MIDIコントローラー切断中は最後の値を保持し、再接続で初期化しない。Gridの`1/4`は1拍の1/4とする。

## 開発環境

- Unity **6000.3.8f1**
- URP **17.3.0** / Input System **1.18.0**
- Test Framework **1.6.0** / Pipeline **0.9.0-exp.1**
- 現在のScene：`Assets/Scenes/SampleScene.unity`

R3・Minis・Lasp・OSC Jack・VFX Graph・Cinemachineなど、設計上必要な追加依存は未導入。導入候補と既存プロジェクトの解決済みバージョンは[調査文書](docs/project-audit.md)を参照。
