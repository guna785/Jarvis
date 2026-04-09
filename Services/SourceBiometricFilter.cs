using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace Jarvis.Services;

public class BiometricService
{
    private InferenceSession _session;

    public void Initialize(string modelPath) => _session = new InferenceSession(modelPath);

    public float[] CreateVoicePrint(float[] pcmAudio)
    {
        var input = new DenseTensor<float>(pcmAudio, new[] { 1, pcmAudio.Length });
        var inputs = new List<NamedOnnxValue> { NamedOnnxValue.CreateFromTensor("wavs", input) };
        using var results = _session.Run(inputs);
        return results.First().AsEnumerable<float>().ToArray();
    }

    public float CalculateSimilarity(float[] printA, float[] printB)
    {
        if (printA == null || printB == null) return 0;
        float dot = 0, magA = 0, magB = 0;
        for (int i = 0; i < printA.Length; i++)
        {
            dot += printA[i] * printB[i];
            magA += printA[i] * printA[i];
            magB += printB[i] * printB[i];
        }
        return dot / ((float)Math.Sqrt(magA) * (float)Math.Sqrt(magB));
    }
}