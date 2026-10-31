using System;
using System.Globalization;
using UnityEngine;

namespace MondmadeVJ0.Transport.TextMeshPro
{
    /// <summary>Displays a MusicalTransport's effective BPM on a TextMeshPro text component.</summary>
    [DisallowMultipleComponent]
    public sealed class TransportBpmText : MonoBehaviour
    {
        private const string DefaultDisplayFormat = "{0:0.##} BPM";

        [SerializeField] private MusicalTransport transport;
        [SerializeField] private TMPro.TMP_Text targetText;
        [SerializeField, Tooltip("Composite format string; use {0} for BPM, e.g. {0:0.##} BPM.")]
        private string displayFormat = DefaultDisplayFormat;
        [SerializeField] private string noTransportText = "-- BPM";

        private TMPro.TMP_Text observedText;
        private double lastObservedBpm = double.NaN;
        private string lastWrittenText;
        private bool warnedInvalidFormat;

        private void Reset() => CacheText();

        private void OnValidate()
        {
            CacheText();
            lastObservedBpm = double.NaN;
            lastWrittenText = null;
            warnedInvalidFormat = false;
        }

        private void OnEnable()
        {
            CacheText();
            lastObservedBpm = double.NaN;
            lastWrittenText = null;
            warnedInvalidFormat = false;
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
            WriteText(FormatBpm(bpm));
        }

        private string FormatBpm(double bpm)
        {
            var format = displayFormat ?? DefaultDisplayFormat;
            try
            {
                return string.Format(CultureInfo.InvariantCulture, format, bpm);
            }
            catch (FormatException exception)
            {
                if (!warnedInvalidFormat)
                {
                    Debug.LogWarning($"{nameof(TransportBpmText)} on '{name}' has an invalid display format. Using '{DefaultDisplayFormat}'. {exception.Message}", this);
                    warnedInvalidFormat = true;
                }

                return string.Format(CultureInfo.InvariantCulture, DefaultDisplayFormat, bpm);
            }
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
