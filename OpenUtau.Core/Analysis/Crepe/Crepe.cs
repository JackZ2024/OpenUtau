using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using NWaves.Operations;
using NWaves.Signals;
using OpenUtau.Core.Util;

namespace OpenUtau.Core.Analysis.Crepe {
    public class Crepe : IDisposable {
        const int kModelSampleRate = 16000;
        const int kFrameSize = 1024;
        const int kActivationSize = 360;
        const float CENTS_PER_BIN = 20.0f;
        const double OFFSET = 1997.3794084376191;

        private static readonly Random rng = new Random();

        InferenceSession session;
        double[] centsMapping;
        private bool disposedValue;

        /// <summary>
        /// 量化方式
        /// </summary>
        public enum QuantizeMode {
            Floor,
            Round,
            Ceil
        }

        public Crepe() {
            if (Preferences.Default.CrepeModel == "full") {
                session = Onnx.getInferenceSession("./full.onnx");
            } else {
                session = Onnx.getInferenceSession(Resources.tiny);
            }

            centsMapping = Enumerable.Range(0, kActivationSize)
                .Select(i => i * 20 + 1997.3794084376191)
                .ToArray();
        }

        public double[] ComputeF0(DiscreteSignal signal, double stepMs, double threshold = 0.21) {
            if (signal.SamplingRate != kModelSampleRate) {
                var resampler = new Resampler();
                signal = resampler.Resample(signal, kModelSampleRate);
            }

            if (session.InputNames == null)
                return new double[0];

            int hopSize = (int)(kModelSampleRate * stepMs / 1000);
            int length = signal.Length / hopSize;
            var batchFrames = ToFrames(signal, hopSize, Preferences.Default.BatchSize);
            float[] activations = new float[length * kActivationSize];
            int currentStart = 0;
            foreach (var input in batchFrames) {
                int curLength = input.Dimensions[0];
                var inputs = new List<NamedOnnxValue>();
                inputs.Add(NamedOnnxValue.CreateFromTensor(session.InputNames[0], input));
                var outputs = session.Run(inputs);
                var activation = outputs.First().AsTensor<float>().ToArray();
                Array.Copy(activation, 0, activations, currentStart * kActivationSize, activation.Length);
                currentStart += curLength;
            }

            //int[] path = new int[length];
            //GetPath2(activations, path);
            int[] path = GetPath4(activations);
            //float[] confidences = new float[length];
            //double[] cents = new double[length];
            double[] f0 = new double[length];
            int[] uv = new int[length];
            int sum_uv = 0;
            for (int i = 0; i < length; ++i) {
                if(path[i] == -1) {
                    f0[i] = 0;
                    continue;
                }

                var frame = new ArraySegment<float>(activations, i * kActivationSize, kActivationSize);
                double cents = GetCents(frame, path[i]);
                float confidences = frame[path[i]];
                f0[i] = double.IsNormal(cents)
                    && double.IsNormal(confidences)
                    && confidences > threshold
                    ? 10f * Math.Pow(2.0, cents / 1200.0) : 0;
                //cents[i] = GetCents(frame, path[i]);
                //confidences[i] = frame[path[i]];
                //f0[i] = double.IsNormal(cents[i])
                //    && double.IsNormal(confidences[i])
                //    && confidences[i] > threshold
                //    ? 10f * Math.Pow(2.0, cents[i] / 1200.0) : 0;

                uv[i] = (f0[i] == 0 ? 1 : 0);
                sum_uv += uv[i];
            }

            if (sum_uv == length) {
                // 全都是没有声音的
            } else if (sum_uv > 0) {
                // 有没有声音的部分，需要实现插值
                Interp(f0, uv);
            }

            for (int i = 0; i < length; ++i) {
                f0[i] = FrequencyToMidiNote(f0[i]);
            }

            return f0;
        }

        Tensor<float> ToFrames(DiscreteSignal signal, double stepMs) {
            float[] paddedSamples = new float[signal.Length + kFrameSize];
            Array.Copy(signal.Samples, 0, paddedSamples, kFrameSize / 2, signal.Length);
            int hopSize = (int)(kModelSampleRate * stepMs / 1000);
            int length = signal.Length / hopSize;
            float[] frames = new float[length * kFrameSize];
            for (int i = 0; i < length; ++i) {
                Array.Copy(paddedSamples, i * hopSize,
                    frames, i * kFrameSize, kFrameSize);
                NormalizeFrame(new ArraySegment<float>(
                    frames, i * kFrameSize, kFrameSize));
            }
            return frames.ToTensor().Reshape(new int[] { length, kFrameSize });
        }
        IEnumerable<Tensor<float>> ToFrames(DiscreteSignal signal, int hopSize, int batchSize=int.MaxValue) {
            // Create padded samples array
            float[] paddedSamples = new float[signal.Length + kFrameSize];
            Array.Copy(signal.Samples, 0, paddedSamples, kFrameSize / 2, signal.Length);
            int length = signal.Length / hopSize;
            if (batchSize <= 0) batchSize = length;

            // Process frames in batches
            for (int batchStart = 0; batchStart < length; batchStart += batchSize) {
                int currentBatchSize = Math.Min(batchSize, length - batchStart);
                float[] batchFrames = new float[currentBatchSize * kFrameSize];

                // Extract frames for the current batch
                for (int i = 0; i < currentBatchSize; ++i) {
                    int frameIndex = batchStart + i;
                    Array.Copy(paddedSamples, frameIndex * hopSize, batchFrames, i * kFrameSize, kFrameSize);
                    NormalizeFrame(new ArraySegment<float>(batchFrames, i * kFrameSize, kFrameSize));
                }

                // Yield tensor for the current batch
                yield return batchFrames.ToTensor().Reshape(new int[] { currentBatchSize, kFrameSize });
            }
        }

        void GetPath(float[] activations, int[] path) {
            float[] prob = new float[kActivationSize];
            float[] nextProb = new float[kActivationSize];
            for (int i = 0; i < kActivationSize; ++i) {
                prob[i] = (float)Math.Log(1.0 / kActivationSize);
            }
            float[,] transitions = new float[kActivationSize, kActivationSize];
            int dist = 12;
            for (int i = 0; i < kActivationSize; ++i) {
                int low = Math.Max(0, i - dist);
                int high = Math.Min(kActivationSize, i + dist + 1);
                float sum = 0;
                for (int j = low; j < high; ++j) {
                    transitions[i, j] = dist - Math.Abs(i - j) + 1e-6f;
                    sum += transitions[i, j];
                }
                for (int j = low; j < high; ++j) {
                    transitions[i, j] = (float)Math.Log(transitions[i, j] / sum);
                }
            }
            for (int i = 0; i < path.Length; ++i) {
                var activ = new ArraySegment<float>(activations, i * kActivationSize, kActivationSize);
                activ = Softmax(activ.ToArray());
                //Array.Clear(nextProb, 0, nextProb.Length);
                for (int j = 0; j < kActivationSize; ++j) {
                    nextProb[j] = float.NegativeInfinity; // 初始化为负无穷
                }
                for (int j = 0; j < kActivationSize; ++j) {
                    int low = Math.Max(0, j - dist);
                    int high = Math.Min(kActivationSize, j + dist + 1);
                    float maxP = float.MinValue;
                    for (int k = low; k < high; ++k) {
                        //float p = (float)(prob[k] + transitions[k, j] + Math.Log(activ[j] + 1e-10f));
                        float p = (float)(prob[k] + transitions[k, j] + (activ[j] > 0 ? Math.Max(Math.Log(activ[j]), -1e6f) : float.NegativeInfinity));
                        if (p > maxP) {
                            maxP = p;
                        }
                    }
                    nextProb[j] = maxP;
                }
                path[i] = ArgMax(nextProb);
                prob = (float[])nextProb.Clone(); // 更新 prob 数组
            }
        }
        void GetPath2(float[] activations, int[] path) {
            // 假设 activations.Length == path.Length * kActivationSize
            float[] prob = new float[kActivationSize];
            float[] nextProb = new float[kActivationSize];
            int[,] backpointer = new int[path.Length, kActivationSize];

            // 初始化均匀分布 (对数形式)
            for (int i = 0; i < kActivationSize; ++i) {
                prob[i] = (float)Math.Log(1.0 / kActivationSize);
            }

            // 构建转移概率矩阵 (log 形式)
            float[,] transitions = new float[kActivationSize, kActivationSize];
            int dist = 12;
            for (int i = 0; i < kActivationSize; ++i) {
                int low = Math.Max(0, i - dist);
                int high = Math.Min(kActivationSize, i + dist);
                float sum = 0;
                for (int j = low; j < high; ++j) {
                    transitions[i, j] = dist - Math.Abs(i - j);
                    sum += transitions[i, j];
                }
                for (int j = low; j < high; ++j) {
                    transitions[i, j] = (float)Math.Log(transitions[i, j] / sum);
                }
            }

            // 动态规划
            for (int t = 0; t < path.Length; ++t) {
                var activ = new ArraySegment<float>(activations, t * kActivationSize, kActivationSize);
                for (int j = 0; j < kActivationSize; ++j) {
                    int low = Math.Max(0, j - dist);
                    int high = Math.Min(kActivationSize, j + dist);
                    float maxP = float.MinValue;
                    int bestK = -1;

                    for (int k = low; k < high; ++k) {
                        // 正确公式: prob[k] + log P(j|k) + log activ[j]
                        float p = prob[k] + transitions[k, j] + (float)Math.Log(activ[j] + 1e-10f);
                        if (p > maxP) {
                            maxP = p;
                            bestK = k;
                        }
                    }

                    nextProb[j] = maxP;
                    backpointer[t, j] = bestK;
                }

                // 更新 prob = nextProb
                Array.Copy(nextProb, prob, kActivationSize);
            }

            // 回溯路径 (找到最后一帧的最优状态)
            int lastState = ArgMax(prob);
            path[path.Length - 1] = lastState;
            for (int t = path.Length - 2; t >= 0; --t) {
                path[t] = backpointer[t + 1, path[t + 1]];
            }
        }
        
        void GetPath3(float[] activations, int[] path) {
            float[] prob = new float[kActivationSize];
            for (int i = 0; i < path.Length; ++i) {
                var activ = new ArraySegment<float>(activations, i * kActivationSize, kActivationSize);
                Array.Copy(activ.ToArray(), prob, kActivationSize);
                //double[] new_prob = Softmax(prob);
                //Console.WriteLine(prob.Sum());
                path[i] = ArgMax(prob);
            }
        }

        int[] GetPath4(float[] activations) {

            float[] logits = new float[activations.Length];
            int mSteps = activations.Length / kActivationSize;
            for (int i = 0; i < mSteps; ++i) {
                var activ = new ArraySegment<float>(activations, i * kActivationSize, kActivationSize);
                var softmaxData = Softmax(activ.ToArray());

                Array.Copy(softmaxData, 0, logits, i * kActivationSize, kActivationSize);
            }


            //    var activ = new ArraySegment<float>(activations, i * kActivationSize, kActivationSize);
            //activ = Softmax(activ.ToArray());

            // 创建CREPE模型适配器
            ViterbiModel crepeModel = new ViterbiModel(logits, kActivationSize, mSteps, 12);

            // 使用Viterbi解码器
            int[] decodedPath = ViterbiDecoder.Decode(crepeModel);
            return decodedPath;
        }

        double GetCents(ArraySegment<float> activations, int index) {
            int start = Math.Max(0, index - 4);
            int end = Math.Min(activations.Count, index + 5);
            double weightedSum = 0;
            double weightSum = 0;
            for (int i = start; i < end; ++i) {
                weightedSum += activations[i] * centsMapping[i];
                weightSum += activations[i];
            }
            return weightedSum / weightSum;
        }
        static int ArgMax(Span<float> values) {
            int index = -1;
            float value = float.MinValue;
            for (int i = 0; i < values.Length; ++i) {
                if (value < values[i]) {
                    index = i;
                    value = values[i];
                }
            }
            return index;
        }

        public double FrequencyToMidiNote(double freq) {
            double log2Value = Math.Log(freq / 440.0f, 2); // 以2为底的对数
            return 69 + 12 * log2Value;
        }

        static float[] Softmax(float[] logits) {
            if (logits == null || logits.Length == 0)
                return Array.Empty<float>();

            int n = logits.Length;
            float[] safeLogits = new float[n];

            // 1️⃣ 替换 NaN 为 0
            for (int i = 0; i < n; i++) {
                safeLogits[i] = float.IsNaN(logits[i]) ? 0f : logits[i];
            }

            // 2️⃣ 数值稳定：减去最大值
            float maxLogit = safeLogits.Max();
            if (float.IsInfinity(maxLogit)) {
                // 如果最大值为 +∞，输出 one-hot
                float[] resultInf = new float[n];
                for (int i = 0; i < n; i++)
                    resultInf[i] = float.IsPositiveInfinity(logits[i]) ? 1f : 0f;
                return resultInf;
            }

            // 3️⃣ 计算 exp 并求和
            float[] expVals = new float[n];
            double sum = 0.0; // 用 double 累加避免误差
            for (int i = 0; i < n; i++) {
                float val = safeLogits[i] - maxLogit;

                if (float.IsNaN(val) || float.IsInfinity(val)) {
                    expVals[i] = 0f;
                } else {
                    float e = (float)Math.Exp(val);
                    expVals[i] = e;
                    if (!float.IsNaN(e) && !float.IsInfinity(e))
                        sum += e;
                }
            }

            // 4️⃣ 若 sum 为 0，则输出均匀分布
            float[] result = new float[n];
            if (sum <= 0.0) {
                float uniform = 1f / n;
                for (int i = 0; i < n; i++)
                    result[i] = uniform;
            } else {
                float invSum = (float)(1.0 / sum);
                for (int i = 0; i < n; i++)
                    result[i] = expVals[i] * invSum;
            }

            return result;
        }

        /// <summary>
        /// 对 f0 数组中的缺失值（由 uv 标记）进行插值修复。
        /// uv[i] == 1 表示 f0[i] 缺失，需要插值
        /// uv[i] == 0 表示 f0[i] 有效
        /// </summary>
        public void Interp(double[] f0, int[] uv) {
            if (f0 == null) throw new ArgumentNullException(nameof(f0));
            if (uv == null) throw new ArgumentNullException(nameof(uv));
            if (f0.Length != uv.Length) throw new ArgumentException("f0 和 uv 必须长度相同");

            int length = f0.Length;
            int i = 0;

            while (i < length) {
                if (uv[i] == 1) {
                    int left = i - 1;
                    int right = -1;

                    // 找右边第一个有效点
                    int k = i;
                    while (k < length && uv[k] == 1) k++;
                    if (k < length) right = k;

                    if (left == -1 && right == -1) {
                        // 全部缺失，不做处理
                        break;
                    } else if (left == -1) {
                        // 开头缺失 → 用第一个有效值填充
                        for (int j = 0; j < right; j++)
                            f0[j] = f0[right];
                    } else if (right == -1) {
                        // 结尾缺失 → 用最后一个有效值填充
                        for (int j = left + 1; j < length; j++)
                            f0[j] = f0[left];
                    } else {
                        // 中间缺失 → 线性插值
                        for (int j = left + 1; j < right; j++) {
                            double t = (j - left) / (double)(right - left);
                            f0[j] = f0[left] + t * (f0[right] - f0[left]);
                        }
                    }

                    // 跳到处理完的位置
                    i = (right == -1 ? length : right);
                } else {
                    i++;
                }
            }
        }

        /// <summary>
        /// 给音高预测结果加上三角分布噪声，用于去除量化误差
        /// </summary>
        private static float Dither(float value) {
            // 生成两个 [0,1) 的均匀随机数
            double u = rng.NextDouble();
            double v = rng.NextDouble();

            // 三角分布噪声: (u - v) * CENTS_PER_BIN
            double noise = (u - v) * CENTS_PER_BIN;

            return (float)(value + noise);
        }

        /// <summary>
        /// 将 pitch bins 转换为 cents，并加上 dither
        /// </summary>
        /// <param name="bins">预测的 pitch bin 数组</param>
        /// <returns>转换并加噪声后的 cents 数组</returns>
        public static float[] BinsToCents(int[] bins) {
            float[] output = new float[bins.Length];

            for (int i = 0; i < bins.Length; i++) {
                // 先从 bin 转换到 cents
                double cents = CENTS_PER_BIN * bins[i] + OFFSET;

                // 再加上 dither
                output[i] = Dither((float)cents);
            }

            return output;
        }

        /// <summary>
        /// 将 bins 转换为频率 Hz
        /// </summary>
        public static double[] BinsToFrequency(int[] bins) {
            float[] cents = BinsToCents(bins);
            double[] freqs = new double[cents.Length];
            for (int i = 0; i < cents.Length; i++) {
                freqs[i] = CentsToFrequency(cents[i]);
            }
            return freqs;
        }

        /// <summary>
        /// 将音分 (cents) 转换为频率 (Hz)
        /// </summary>
        /// <param name="cents">音分值</param>
        /// <returns>对应的频率 (Hz)</returns>
        public static double CentsToFrequency(double cents) {
            return 10.0 * Math.Pow(2.0, cents / 1200.0);
        }

        /// <summary>
        /// 批量转换：cents 数组 → Hz 数组
        /// </summary>
        /// <param name="centsArray">音分数组</param>
        /// <returns>对应的频率数组</returns>
        public static double[] CentsToFrequency(double[] centsArray) {
            double[] frequencies = new double[centsArray.Length];
            for (int i = 0; i < centsArray.Length; i++) {
                frequencies[i] = CentsToFrequency(centsArray[i]);
            }
            return frequencies;
        }

        /// <summary>
        /// 将 cents 转换为 bins
        /// </summary>
        /// <param name="cents">音分值</param>
        /// <param name="mode">量化方式 (默认 Floor)</param>
        /// <returns>bin 索引 (整数)</returns>
        public static int CentsToBins(double cents, QuantizeMode mode = QuantizeMode.Floor) {
            double rawBin = (cents - OFFSET) / CENTS_PER_BIN;

            return mode switch {
                QuantizeMode.Floor => (int)Math.Floor(rawBin),
                QuantizeMode.Round => (int)Math.Round(rawBin),
                QuantizeMode.Ceil => (int)Math.Ceiling(rawBin),
                _ => (int)Math.Floor(rawBin)
            };
        }

        /// <summary>
        /// 批量转换：cents 数组 → bins 数组
        /// </summary>
        public static int[] CentsToBins(double[] centsArray, QuantizeMode mode = QuantizeMode.Floor) {
            int[] bins = new int[centsArray.Length];
            for (int i = 0; i < centsArray.Length; i++) {
                bins[i] = CentsToBins(centsArray[i], mode);
            }
            return bins;
        }

        /// <summary>
        /// 批量转换：frequency 数组 → bins 数组
        /// </summary>
        public static int[] FrequencyToBins(double[] frequencies, QuantizeMode mode = QuantizeMode.Floor) {
            return CentsToBins(FrequencyToCents(frequencies), mode);
        }

        /// <summary>
        /// 将 frequency 转换为 cents
        /// </summary>
        /// <param name="frequency">频率值</param>
        /// <returns>cents音分值</returns>
        public static double FrequencyToCents(double frequency) {
            return 1200 * Math.Log(frequency / 10.0, 2);
        }

        /// <summary>
        /// 批量转换：Hz 数组 → cents 数组
        /// </summary>
        /// <param name="frequencies">频率数组</param>
        /// <returns>对应的音分数组</returns>
        public static double[] FrequencyToCents(double[] frequencies) {
            double[] centsArray = new double[frequencies.Length];
            for (int i = 0; i < frequencies.Length; i++) {
                centsArray[i] = FrequencyToCents(frequencies[i]);
            }
            return frequencies;
        }

        void NormalizeFrame(ArraySegment<float> data) {
            double avg = data.Average();
            double std = Math.Sqrt(data.Average(d => Math.Pow(d - avg, 2)));
            for (int i = 0; i < data.Count; ++i) {
                data[i] = (float)((data[i] - avg) / std);
            }
        }

        protected virtual void Dispose(bool disposing) {
            if (!disposedValue) {
                if (disposing) {
                    session.Dispose();
                }
                disposedValue = true;
            }
        }

        public void Dispose() {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}
