using System;
using System.Globalization;
using UnityEngine;

namespace MondmadeVJ0.Transport.TextMeshPro
{
    /// <summary>Displays a MusicalTransport's effective BPM on a TextMeshPro text component.</summary>
    [DisallowMultipleComponent]
    public sealed class TransportBpmText : MonoBehaviour
    {
        private const string NumberFormat = "0.##";
        private const string BpmSuffix = " BPM";

        [SerializeField] private MusicalTransport transport;
        [SerializeField] private TMPro.TMP_Text targetText;
        [SerializeField] private string noTransportText = "-- BPM";

        private TMPro.TMP_Text observedText;
        private double lastObservedBpm = double.NaN;
        private string lastWrittenText;

        private void Reset() => CacheText();

        private void OnValidate() => CacheText();

        private void OnEnable()
        {
            CacheText();
            lastObservedBpm = double.NaN;
            lastWrittenText = null;
            Refresh();
        }

        private void Update() => Refresh();

        private void CacheText()
        {
            if (targetText == null)
                targetText = GetComponent<TMPro.TMP_Text>();

            if (targetText == observedText)
                return;

            observedText = targetText;
            lastObservedBpm = double.NaN;
            lastWrittenText = null;
        }

        private void Refresh()
        {
            CacheText();
            if (targetText == null)
                return;

            if (transport == null)
            {
                lastObservedBpm = double.NaN;
                WriteText(noTransportText);
                return;
            }

            var bpm = transport.EffectiveBpm;
            if (!double.IsNaN(lastObservedBpm) && bpm.Equals(lastObservedBpm))
            {
                if (!string.Equals(targetText.text, lastWrittenText, StringComparison.Ordinal))
                    targetText.SetText(lastWrittenText);
                return;
            }

            lastObservedBpm = bpm;
            WriteText(bpm.ToString(NumberFormat, CultureInfo.InvariantCulture) + BpmSuffix);
        }

        private void WriteText(string value)
        {
            if (string.Equals(lastWrittenText, value, StringComparison.Ordinal)
                && string.Equals(targetText.text, value, StringComparison.Ordinal))
                return;

            targetText.SetText(value);
            lastWrittenText = value;
        }
    }
}
