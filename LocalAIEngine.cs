using Microsoft.ML.OnnxRuntimeGenAI;
using System;
using System.Collections.Generic;
using System.Text;
using Model = Microsoft.ML.OnnxRuntimeGenAI.Model;

namespace Visor
{
    public class LocalAIEngine
    {
        private Model _model;
        private Tokenizer _tokenizer;
        private TokenizerStream _tokenizerStream;
        private bool _isInitialized = false;       

        public async Task InitializeAsync(string modelPath)
        {
            if (_isInitialized) return;

            Console.WriteLine($"[LocalAIEngine] Initializing model from: {modelPath}");

            await Task.Run(() =>
            {
                try
                {
                    _model = new Model(modelPath);
                    _tokenizer = new Tokenizer(_model);
                    _tokenizerStream = _tokenizer.CreateStream();
                    _isInitialized = true;
                    Console.WriteLine("[LocalAIEngine] Initialization successful.");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[LocalAIEngine] CRITICAL INIT ERROR: {ex.Message}");
                    Console.WriteLine(ex.StackTrace);
                    throw; // Re-throw to be caught by MainPage
                }
            });
        }

        public async IAsyncEnumerable<string> StreamChatLocallyAsync(string userMessage, List<string> history = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            if (!_isInitialized) { yield return "Core is offline."; yield break; }

            string formattedPrompt = PreparePrompt(userMessage, history);
            var tokens = _tokenizer.Encode(formattedPrompt);
            
            using var generatorParams = new GeneratorParams(_model);
            generatorParams.SetSearchOption("max_length", 2048);
            generatorParams.SetSearchOption("temperature", 0.6);
            generatorParams.SetSearchOption("top_p", 0.9);
            // Performance optimizations
            generatorParams.SetSearchOption("do_sample", true);

            var channel = System.Threading.Channels.Channel.CreateUnbounded<string>();

            _ = Task.Run(() =>
            {
                try
                {
                    using var generator = new Generator(_model, generatorParams);
                    generator.AppendTokenSequences(tokens);

                    while (!generator.IsDone() && !ct.IsCancellationRequested)
                    {
                        generator.GenerateNextToken();
                        var seq = generator.GetSequence(0);
                        if (seq.Length > 0)
                        {
                            var token = _tokenizerStream.Decode(seq[^1]);
                            if (!string.IsNullOrEmpty(token))
                            {
                                channel.Writer.TryWrite(token);
                            }
                        }
                    }
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    Console.WriteLine("Generation Error: " + ex.Message);
                }
                finally
                {
                    channel.Writer.Complete();
                }
            }, ct);

            await foreach (var token in channel.Reader.ReadAllAsync(ct))
            {
                yield return token;
            }
        }

        private string PreparePrompt(string userMessage, List<string> history)
        {
            string currentTime = DateTime.Now.ToString("dddd, MMMM dd, yyyy HH:mm:ss");
            string systemPrompt = $"You are Visor, a sophisticated AI with a dry wit and human-like conversational patterns. Speak naturally, efficiently, and interact like a highly refined digital butler. Keep replies short. Current context: {currentTime}. You are aware of the time and can act accordingly. Protocols: If the user wants to call someone, respond briefly and include exactly one tag in the format '[[CALL:target]]' using the contact name or number to call.";
            
            var promptBuilder = new StringBuilder();
            promptBuilder.Append($"<|begin_of_text|><|start_header_id|>system<|end_header_id|>\n\n{systemPrompt}<|eot_id|>");

            if (history != null)
            {
                foreach (var h in history)
                {
                    int separatorIndex = h.IndexOf(':');
                    if (separatorIndex > 0)
                    {
                        string role = h.Substring(0, separatorIndex).Trim();
                        string message = h.Substring(separatorIndex + 1).Trim();

                        if (role.Equals("Visor", StringComparison.OrdinalIgnoreCase))
                        {
                            promptBuilder.Append($"<|start_header_id|>assistant<|end_header_id|>\n\n{message}<|eot_id|>");
                        }
                        else
                        {
                            promptBuilder.Append($"<|start_header_id|>user<|end_header_id|>\n\n{message}<|eot_id|>");
                        }
                    }
                }
            }

            promptBuilder.Append($"<|start_header_id|>user<|end_header_id|>\n\n{userMessage}<|eot_id|>");
            promptBuilder.Append($"<|start_header_id|>assistant<|end_header_id|>\n\n");
            return promptBuilder.ToString();
        }

        public async Task<string> ChatLocallyAsync(string userMessage, List<string> history = null)
        {
            StringBuilder sb = new StringBuilder();
            await foreach (var bit in StreamChatLocallyAsync(userMessage, history))
            {
                sb.Append(bit);
            }
            return sb.ToString().Replace("<|eot_id|>", "").Replace("<|end_of_text|>", "").Trim();
        }
    }
}
