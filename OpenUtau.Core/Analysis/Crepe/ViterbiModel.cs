using System;

public class ViterbiModel : IViterbiModel
{
    private readonly float[] _activations; // 你的原始activations数组
    private readonly int _kActivationSize;   // 状态数
    private readonly int _sequenceLength;    // 序列长度 (path.Length)
    private readonly float[,] _transitions;  // 预计算的对数转移概率
    private readonly int _dist;              // 你的距离参数

    public ViterbiModel(float[] activations, int kActivationSize, int sequenceLength, int dist)
    {
        _activations = activations ?? throw new ArgumentNullException(nameof(activations));
        if (kActivationSize <= 0) throw new ArgumentOutOfRangeException(nameof(kActivationSize));
        if (sequenceLength <= 0) throw new ArgumentOutOfRangeException(nameof(sequenceLength));
        if (activations.Length != kActivationSize * sequenceLength)
            throw new ArgumentException("Activations array length does not match kActivationSize * sequenceLength.");

        _kActivationSize = kActivationSize;
        _sequenceLength = sequenceLength;
        _dist = dist;

        // 预计算转移概率（与你原代码逻辑类似，但这里是直接Log形式）
        _transitions = new float[_kActivationSize, _kActivationSize];
        for (int i = 0; i < _kActivationSize; ++i)
        {
            int low = Math.Max(0, i - _dist);
            int high = Math.Min(_kActivationSize, i + _dist);
            float sum = 0;
            // 计算线性权重
            for (int j = low; j < high; ++j)
            {
                _transitions[i, j] = _dist - Math.Abs(i - j);
                sum += _transitions[i, j];
            }
            // 归一化并取对数
            for (int j = low; j < high; ++j)
            {
                if (sum > 0)
                {
                    _transitions[i, j] = (float)Math.Log(_transitions[i, j] / sum);
                }
                else
                {
                    // 如果sum为0（不应该发生，除非dist太小），则所有转移概率为负无穷
                    _transitions[i, j] = float.NegativeInfinity;
                }
            }
            // 对于不允许的转移，设置为负无穷
            for (int j = 0; j < _kActivationSize; ++j)
            {
                if (j < low || j >= high)
                {
                    _transitions[i, j] = float.NegativeInfinity;
                }
            }
        }
    }

    public int NumberOfStates => _kActivationSize;
    public int SequenceLength => _sequenceLength;

    public float GetInitialLogProbability(int stateIndex)
    {
        // 假设初始时所有状态的概率相等
        return (float)Math.Log(1.0 / NumberOfStates);
        // 或者如果你有特定的起始状态，可以设置其他状态为float.NegativeInfinity
    }

    public float GetTransitionLogProbability(int fromStateIndex, int toStateIndex)
    {
        // 直接返回预计算的对数转移概率
        return _transitions[fromStateIndex, toStateIndex];
    }

    public float GetEmissionLogProbability(int timeStep, int stateIndex)
    {
        // 获取当前时间步和状态的激活值
        // !!! 关键点：这里假设 _activations 存储的是原始概率，所以需要取 Log !!!
        // 如果你的CREPE输出已经是Log Softmax，则直接返回 _activations[timeStep * NumberOfStates + stateIndex];
        float activationValue = _activations[timeStep * NumberOfStates + stateIndex];
        // 避免Log(0)的情况，通常用一个很小的值代替0
        if (activationValue <= float.Epsilon) // 使用一个小的正数来检查是否接近0
        {
            return float.NegativeInfinity; // Log(0) 是负无穷
        }
        return (float)Math.Log(activationValue);
    }
}
