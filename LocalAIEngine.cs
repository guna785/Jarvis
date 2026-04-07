using Microsoft.ML.OnnxRuntimeGenAI;
using System;
using System.Collections.Generic;
using System.Text;
using Model = Microsoft.ML.OnnxRuntimeGenAI.Model;

namespace Jarvis
{
    public class LocalAIEngine
    {
        private Model _model;
        private Tokenizer _tokenizer;
        private bool _isInitialized = false;

        private const string SystemPrompt =
            "You are F.R.I.D.A.Y., a highly advanced, witty personal AI companion. " +
            "You are talking to your creator. Keep answers conversational, concise, and natural. " +
            "Never use lists, bullet points, or say 'As an AI'. Speak like a living entity.";

        public async Task InitializeAsync(string modelPath)
        {
            if (_isInitialized) return;

            await Task.Run(() =>
            {
                _model = new Model(modelPath);
                _tokenizer = new Tokenizer(_model);
                _isInitialized = true;
            });
        }

        public async Task<string> ChatLocallyAsync(string userMessage)
        {
            if (!_isInitialized) return "Core is offline, Boss.";

            return await Task.Run(() =>
            {
                string formattedPrompt =
                    $"<|begin_of_text|><|start_header_id|>system<|end_header_id|>\n\n{SystemPrompt}<|eot_id|>" +
                    $"<|start_header_id|>user<|end_header_id|>\n\n{userMessage}<|eot_id|>" +
                    $"<|start_header_id|>assistant<|end_header_id|>\n\n";

                using var sequences = _tokenizer.Encode(formattedPrompt);
                using var generatorParams = new GeneratorParams(_model);

                generatorParams.SetSearchOption("max_length", 100);
                generatorParams.SetSearchOption("temperature", 0.6);
                generatorParams.SetSearchOption("top_p", 0.9);

                using var generator = new Generator(_model, generatorParams);
                generator.AppendTokenSequences(sequences);

                StringBuilder responseBuilder = new StringBuilder();

                while (!generator.IsDone())
                {
                    generator.GenerateNextToken();
                    var decodedToken = _tokenizer.Decode(new[] { generator.GetSequence(0)[^1] });
                    responseBuilder.Append(decodedToken);
                }

                return responseBuilder.ToString().Replace("<|eot_id|>", "").Replace("<|end_of_text|>", "").Trim();
            });
        }
    }
}
