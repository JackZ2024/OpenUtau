using System;

namespace OpenUtau.Core.SignalChain {
    public class WaveSource : ISignalSource {
        public readonly double offsetMs;
        public readonly double estimatedLengthMs;
        public readonly int skipData; // add by Jack
        public readonly int offset;
        public readonly int estimatedLength;
        public readonly int channels;
        public readonly bool isWavePart;  // add by Jack

        public double EndMs => offsetMs + estimatedLengthMs;
        public bool HasSamples => data != null;

        private readonly object lockObj = new object();
        private float[] data;

        // change by Jack
        public WaveSource(double offsetMs, double estimatedLengthMs, double skipOverMs, int channels, bool isWavePart=false) {
            this.offsetMs = offsetMs;
            // add by Jack
            this.skipData = (int)((skipOverMs) * 44100 / 1000) * channels;
            // end add
            this.estimatedLengthMs = estimatedLengthMs;
            this.channels = channels;
            // change by Jack
            offset = (int)((offsetMs) * 44100 / 1000) * channels;
            estimatedLength = (int)(estimatedLengthMs * 44100 / 1000) * channels;
            // add by Jack
            this.isWavePart = isWavePart;
            // end add
        }

        public void SetSamples(float[] samples) {
            lock (lockObj) {
                data = samples;
            }
        }

        public bool IsReady(int position, int count) {
            int copies = 2 / channels;
            return position + count <= offset * copies
                || offset * copies + estimatedLength * copies <= position
                || data != null;
        }

        public int Mix(int position, float[] buffer, int index, int count) {
            int copies = 2 / channels;
            if (data == null) {
                if (position + count <= offset * copies) {
                    return position + count;
                }
                return position;
            }
            int start = Math.Max(position, offset * copies);
            int end = Math.Min(position + count, offset * copies + data.Length * copies);
            // add by Jack
            if (isWavePart) {
                end = Math.Min(position + count, offset * copies + estimatedLength * copies);
            }
            // end add
            for (int i = start; i < end; ++i) {
                // change by Jack
                int dataIndex = i / copies - offset + this.skipData;
                if (dataIndex < 0 || dataIndex >= data.Length) {
                    buffer[index + i - position] += 0;
                } else {
                    buffer[index + i - position] += data[dataIndex];
                }
                // end change
            }
            return end;
        }
    }
}
