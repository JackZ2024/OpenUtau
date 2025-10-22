using System;
using System.Collections.Generic;
using System.Linq;

// 定义一个接口，用于Viterbi解码器获取模型相关信息
public interface IViterbiModel
{
    int NumberOfStates { get; } // 模型的总状态数

    // 获取初始状态的对数概率
    // 通常所有状态初始概率相等，或者某个特定状态是起始状态
    float GetInitialLogProbability(int stateIndex);

    // 获取从一个状态转移到另一个状态的对数概率
    float GetTransitionLogProbability(int fromStateIndex, int toStateIndex);

    // 获取在给定时间步和状态下，观测到的对数概率
    // 对应于你的activations[timeStep * NumberOfStates + stateIndex]
    float GetEmissionLogProbability(int timeStep, int stateIndex);

    // 获取序列的总时间步数（或长度）
    int SequenceLength { get; }
}

public class ViterbiDecoder
{
    public static int[] Decode(IViterbiModel model)
    {
        int numStates = model.NumberOfStates;
        int sequenceLength = model.SequenceLength;

        if (sequenceLength == 0)
        {
            return new int[0];
        }

        // Viterbi 路径矩阵：viterbiPath[time][state] 存储到达该状态的最大对数概率
        float[,] viterbiPath = new float[sequenceLength, numStates];

        // 回溯矩阵：backPointer[time][state] 存储到达该状态的最佳前驱状态的索引
        int[,] backPointer = new int[sequenceLength, numStates];

        // 1. 初始化 (t=0)
        for (int s = 0; s < numStates; s++)
        {
            viterbiPath[0, s] = model.GetInitialLogProbability(s) + model.GetEmissionLogProbability(0, s);
            // 初始步没有前驱，可以设置为-1或0，但实际回溯时会跳过这一步
            backPointer[0, s] = -1;
        }

        // 2. 迭代 (t=1 到 sequenceLength - 1)
        for (int t = 1; t < sequenceLength; t++)
        {
            for (int s = 0; s < numStates; s++) // 当前状态 (s_t)
            {
                float maxLogProb = float.MinValue;
                int bestPrevState = -1;

                for (int prevS = 0; prevS < numStates; prevS++) // 前一个状态 (s_{t-1})
                {
                    // viterbiPath[t-1, prevS]          -> 到达前一个状态的最大对数概率
                    // model.GetTransitionLogProbability(prevS, s) -> 从前一个状态转移到当前状态的对数概率
                    // model.GetEmissionLogProbability(t, s)       -> 在当前状态下观测到的对数概率 (当前时间步 t 的观测)
                    float currentLogProb = viterbiPath[t - 1, prevS]
                                         + model.GetTransitionLogProbability(prevS, s)
                                         + model.GetEmissionLogProbability(t, s); // 注意：这里是当前时间步t的发射概率

                    if (currentLogProb > maxLogProb)
                    {
                        maxLogProb = currentLogProb;
                        bestPrevState = prevS;
                    }
                }
                viterbiPath[t, s] = maxLogProb;
                backPointer[t, s] = bestPrevState;
            }
        }

        // 3. 回溯：找到最终时间步的最佳状态，然后反向追踪路径
        int[] bestPath = new int[sequenceLength];

        // 找到最后一个时间步的最佳状态
        float finalMaxLogProb = float.MinValue;
        int lastState = -1;
        for (int s = 0; s < numStates; s++)
        {
            if (viterbiPath[sequenceLength - 1, s] > finalMaxLogProb)
            {
                finalMaxLogProb = viterbiPath[sequenceLength - 1, s];
                lastState = s;
            }
        }

        // 从最后一个最佳状态开始回溯
        bestPath[sequenceLength - 1] = lastState;
        for (int t = sequenceLength - 1; t > 1; t--)
        {
            bestPath[t - 1] = backPointer[t, bestPath[t]];
        }

        return bestPath;
    }
}
