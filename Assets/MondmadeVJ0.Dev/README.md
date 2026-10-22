# Mondmade VJ0開発用Scene

## APC mini mk2 QuickStart

`Scenes/APC-mini-mk2-QuickStart.unity`は、APC mini mk2・Transport・OSC同期を一つのSceneで結合確認するための開発用Sceneです。`Midi/APC-mini-mk2-QuickStart.asset`はPad 64個、Track 8個、Scene 8個、Shift 1個、Fader 9個の90 Logical Controlを定義します。

1. Sceneを開き、`Mondmade VJ0 Runtime`の`MidiHub`にProfileとInput / Output port ordinalが割り当てられていることを確認します。この開発機ではRtMidiが両方ともport `0`として列挙しました。異なるPCではInspectorを更新してください。
2. Playし、Runtime Inspectorの`Open Monitor`からMidiHubが`Connected`であることを確認します。
3. Pad 1で立方体とPad LEDを切り替え、Fader 1で球の幅を操作します。TransportのBeat Phaseで球の高さが動きます。
4. OSC受信を使う場合は`OscHub`のlisten portを送信側に合わせ、TransportのClock SourceをExternalにします。`/mondmade/transport`へfloat 4個（BPM、whole beat、fractional beat、running）を送ります。

このSceneとProfileは結合検証用です。利用者のプロジェクトでは同じProfileを複製し、利用環境に合わせてport ordinalとControl定義を編集してください。
