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
    public class VisorBrain : IDisposable
    {
        private const string EnglishLanguage = "en";
        private const string TamilLanguage = "ta";

        private WhisperFactory _factory;
        private WhisperProcessor _englishProcessor;
        private WhisperProcessor _tamilProcessor;
        private bool _isInitialized = false;
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
                _englishProcessor = _factory.CreateBuilder()
                    .WithLanguage(EnglishLanguage)
                    .WithPrompt("Listen for English and Tamil (தமிழ்). User says: Hello Visor.")
                    .WithThreads(Math.Max(2, Environment.ProcessorCount - 1))
                    .Build();
                _tamilProcessor = _factory.CreateBuilder()
                    .WithLanguage(TamilLanguage)
                    .WithThreads(Math.Max(2, Environment.ProcessorCount - 1))
                    .Build();
                _isInitialized = true;
            });
        }

        public async IAsyncEnumerable<string> ProcessAudioAsync(float[] samples)
        {
            if (!_isInitialized) yield break;

            await _processorLock.WaitAsync();
            try
            {
                await foreach (var segment in _englishProcessor.ProcessAsync(samples))
                {
                    yield return segment.Text.Trim();
                }
            }
            finally
            {
                _processorLock.Release();
            }
        }

        public void Dispose()
        {
            _englishProcessor?.Dispose();
            _tamilProcessor?.Dispose();
            _factory?.Dispose();
            _processorLock.Dispose();
        }
    }
}
