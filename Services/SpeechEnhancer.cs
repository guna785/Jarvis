using System;
using System.Buffers;
using System.Collections.Generic;

namespace Visor.Services
{
    internal sealed class SpeechEnhancer
    {
        private const float PreEmphasis = 0.97f;
        private const float TargetRms = 0.07f;
        private const float MinGain = 0.6f;
        private const float MaxGain = 3.0f;

        private float _previousInputSample;
        private float _noiseRms = 0.005f;
        private float _agcGain = 1.0f;

        public float NoiseRms => _noiseRms;

        public void ProcessPcm16(short[] pcm, int count, List<float> output, out float peak, out float rms)
        {
            peak = 0f;
            double sumSquares = 0;
            float prev = _previousInputSample;
            float[]? temp = null;

            try
            {
                temp = ArrayPool<float>.Shared.Rent(count);

                // Pre-emphasis and stats in one pass.
                for (int i = 0; i < count; i++)
                {
                    float x = pcm[i] / 32768f;
                    float y = x - (PreEmphasis * prev);
                    prev = x;

                    temp[i] = y;
                    float abs = Math.Abs(y);
                    if (abs > peak) peak = abs;
                    sumSquares += y * y;
                }

                _previousInputSample = prev;

                rms = count > 0 ? (float)Math.Sqrt(sumSquares / count) : 0f;

                // Update AGC gain estimate (slow).
                float desiredGain = rms > 1e-6f ? (TargetRms / rms) : MaxGain;
                desiredGain = Math.Clamp(desiredGain, MinGain, MaxGain);
                _agcGain = (_agcGain * 0.90f) + (desiredGain * 0.10f);

                // Light noise gate using the running noise floor (avoid pumping).
                float gate = (_noiseRms * 3.5f) + 0.002f;

                for (int i = 0; i < count; i++)
                {
                    float y = temp[i];
                    if (Math.Abs(y) < gate) y *= 0.15f;

                    y *= _agcGain;
                    y = Math.Clamp(y, -1f, 1f);
                    output.Add(y);
                }
            }
            finally
            {
                if (temp is not null)
                {
                    ArrayPool<float>.Shared.Return(temp, clearArray: false);
                }
            }
        }

        public void UpdateNoiseFloor(float chunkRms)
        {
            // Adapt quickly enough for changing environments, but avoid speech contaminating the floor.
            _noiseRms = (_noiseRms * 0.95f) + (chunkRms * 0.05f);
            _noiseRms = Math.Clamp(_noiseRms, 0.0005f, 0.05f);
        }
    }
}
