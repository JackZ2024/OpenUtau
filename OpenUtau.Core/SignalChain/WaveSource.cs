using System;

namespace OpenUtau.Core.SignalChain {
    public class WaveSource : ISignalSource {
        public readonly double offsetMs;
        public readonly double estimatedLengthMs;
        public readonly int skipData;
        public readonly int offset;
        public readonly int estimatedLength;
        public readonly int channels;
        public readonly bool isWavePart;

        public double EndMs => offsetMs + estimatedLengthMs;
        public bool HasSamples => data != null;

        private readonly object lockObj = new object();
        private float[] data;

        public WaveSource(double offsetMs, double estimatedLengthMs, double skipOverMs, int channels, bool isWavePart=false) {
            this.offsetMs = offsetMs;
            this.skipData = (int)((skipOverMs) * 44100 / 1000) * channels;
            this.estimatedLengthMs = estimatedLengthMs;
            this.channels = channels;
            offset = (int)((offsetMs) * 44100 / 1000) * channels;
            estimatedLength = (int)(estimatedLengthMs * 44100 / 1000) * channels;
            this.isWavePart = isWavePart;
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
            if (isWavePart) {
                end = Math.Min(position + count, offset * copies + estimatedLength * copies);
            }
            for (int i = start; i < end; ++i) {
                int dataIndex = i / copies - offset + this.skipData;
                if (dataIndex < 0 || dataIndex >= data.Length) {
                    buffer[index + i - position] += 0;
                } else {
                    buffer[index + i - position] += data[dataIndex];
                }
            }
            return end;
        }
    }
}
