using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Whisper.net;
using Whisper.net.Ggml;

namespace Visor.Services
{
    public class StableVisorBrain : IDisposable
    {
        private const string EnglishLanguage = "en";
        private const string TamilLanguage = "ta";

        private WhisperFactory _factory;
        private WhisperProcessor _englishProcessor;
        private WhisperProcessor _tamilProcessor;
        private bool _isInitialized;
        private readonly SemaphoreSlim _processorLock = new(1, 1);
        private string _preferredLanguage = EnglishLanguage;

        public async Task InitializeAsync(string modelPath)
        {
            if (_isInitialized) return;

            if (!File.Exists(modelPath))
            {
                throw new FileNotFoundException("Whisper model not found at " + modelPath);
            }

            await Task.Run(() =>
            {
                _factory = WhisperFactory.FromPath(modelPath);
                _englishProcessor = CreateProcessor(EnglishLanguage);
                _tamilProcessor = CreateProcessor(TamilLanguage);
                _isInitialized = true;
            });
        }

        public async IAsyncEnumerable<string> ProcessAudioAsync(float[] samples)
        {
            string transcript = await TranscribeAsync(samples);
            if (!string.IsNullOrWhiteSpace(transcript))
            {
                yield return transcript;
            }
        }

        private WhisperProcessor CreateProcessor(string language)
        {
            return _factory.CreateBuilder()
                .WithLanguage(language)
                .WithThreads(Math.Max(2, Environment.ProcessorCount - 1))
                .Build();
        }

        private async Task<string> TranscribeAsync(float[] samples)
        {
            if (!_isInitialized)
            {
                return string.Empty;
            }

            await _processorLock.WaitAsync();
            try
            {
                string primaryLanguage = _preferredLanguage;
                string secondaryLanguage = primaryLanguage == EnglishLanguage ? TamilLanguage : EnglishLanguage;

                string primaryTranscript = await RunProcessorAsync(GetProcessor(primaryLanguage), samples);
                if (LooksGood(primaryTranscript, primaryLanguage))
                {
                    _preferredLanguage = primaryLanguage;
                    return primaryTranscript;
                }

                string secondaryTranscript = await RunProcessorAsync(GetProcessor(secondaryLanguage), samples);
                if (LooksBetter(secondaryTranscript, primaryTranscript, secondaryLanguage))
                {
                    _preferredLanguage = secondaryLanguage;
                    return secondaryTranscript;
                }

                return primaryTranscript.Length >= secondaryTranscript.Length ? primaryTranscript : secondaryTranscript;
            }
            finally
            {
                _processorLock.Release();
            }
        }

        private WhisperProcessor GetProcessor(string language) =>
            language == TamilLanguage ? _tamilProcessor : _englishProcessor;

        private static async Task<string> RunProcessorAsync(WhisperProcessor processor, float[] samples)
        {
            var builder = new StringBuilder();

            await foreach (var segment in processor.ProcessAsync(samples))
            {
                string text = segment.Text?.Trim() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(text))
                {
                    if (builder.Length > 0)
                    {
                        builder.Append(' ');
                    }

                    builder.Append(text);
                }
            }

            return builder.ToString().Trim();
        }

        private static bool LooksGood(string transcript, string language)
        {
            if (string.IsNullOrWhiteSpace(transcript))
            {
                return false;
            }

            return language switch
            {
                TamilLanguage => CountTamilCharacters(transcript) >= 2 || CountLetters(transcript) >= 3,
                _ => CountAsciiLetters(transcript) >= 3
            };
        }

        private static bool LooksBetter(string candidate, string current, string candidateLanguage)
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                return false;
            }

            string currentLanguage = candidateLanguage == TamilLanguage ? EnglishLanguage : TamilLanguage;
            if (!LooksGood(current, currentLanguage))
            {
                return LooksGood(candidate, candidateLanguage);
            }

            return ScoreTranscript(candidate, candidateLanguage) > ScoreTranscript(current, currentLanguage);
        }

        private static int ScoreTranscript(string transcript, string language)
        {
            if (string.IsNullOrWhiteSpace(transcript))
            {
                return 0;
            }

            int score = transcript.Length;
            score += language == TamilLanguage ? CountTamilCharacters(transcript) * 4 : CountAsciiLetters(transcript) * 2;
            score -= transcript.Count(ch => ch == '?' || ch == '[' || ch == ']') * 3;
            return score;
        }

        private static int CountAsciiLetters(string value) => value.Count(ch => ch is >= 'A' and <= 'Z' or >= 'a' and <= 'z');

        private static int CountTamilCharacters(string value) => value.Count(ch => ch >= '\u0B80' && ch <= '\u0BFF');

        private static int CountLetters(string value) => value.Count(char.IsLetter);

        public void Dispose()
        {
            _englishProcessor?.Dispose();
            _tamilProcessor?.Dispose();
            _factory?.Dispose();
            _processorLock.Dispose();
        }
    }
}
