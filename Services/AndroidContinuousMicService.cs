using Android.App;
using Android.Content;
using Android.OS;
using Android.Media;
using AndroidX.Core.App;
using CommunityToolkit.Mvvm.Messaging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Maui.ApplicationModel;
using System.Text;
using Resource = Microsoft.Maui.Resource;

namespace Visor.Services
{
    [Service(Name = "com.visor.ContinuousMicService", ForegroundServiceType = global::Android.Content.PM.ForegroundService.TypeMicrophone)]
    public class AndroidContinuousMicService : Service, AudioManager.IOnAudioFocusChangeListener
    {
        public const string ChannelId = "VisorMicChannel";
        public const int NotificationId = 1001;
        private const int SampleRate = 16000;
        private const int AudioBufferSize = 512;
        private const float VoiceActivityThreshold = 0.04f;
        private const int SilenceChunksToFinalize = 6;
        private const int MinSpeechSamples = SampleRate / 3;
        private const int MaxSpeechSamples = SampleRate * 3;

        private VisorAudioEngine _audioEngine;
        private StableVisorBrain _brain;
        private SpeechEnhancer _enhancer;
        private CancellationTokenSource _cts;
        private bool _isListening = false;
        private bool _isBusy = false;
        private AudioFocusRequestClass _focusRequest;
        private AudioManager? _audioManager;
        private Mode? _previousAudioMode;

        public override IBinder OnBind(Intent intent) => null;

        public void OnAudioFocusChange(AudioFocus focusChange) { }

        public override void OnCreate()
        {
            base.OnCreate();
            _audioEngine = new VisorAudioEngine();
            _brain = new StableVisorBrain();
            _enhancer = new SpeechEnhancer();
        }

        public override StartCommandResult OnStartCommand(Intent intent, StartCommandFlags flags, int startId)
        {
            if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
            {
                var channel = new NotificationChannel(ChannelId, "Whisper Mic Service", NotificationImportance.Low);
                ((NotificationManager)GetSystemService(NotificationService)).CreateNotificationChannel(channel);
            }

            var notification = new NotificationCompat.Builder(this, ChannelId)
                .SetContentTitle("Visor :: Whisper Mode")
                .SetContentText("Multi-Language Neural Engine Active")
                .SetSmallIcon(Resource.Drawable.dotnet_bot)
                .SetOngoing(true)
                .Build();

            if (Build.VERSION.SdkInt >= BuildVersionCodes.Q)
                StartForeground(NotificationId, notification, global::Android.Content.PM.ForegroundService.TypeMicrophone);
            else
                StartForeground(NotificationId, notification);

            if (intent?.Action == "STOP_LISTENING")
            {
                _isListening = false;
                StopAudioPump();
                AbandonFocus();
                return StartCommandResult.Sticky;
            }

            RequestFocus();
            _isListening = true;
            StartAudioPump();

            return StartCommandResult.Sticky;
        }

        private void StartAudioPump()
        {
            if (_cts != null) return;
            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            Task.Run(async () =>
            {
                try
                {
                    string modelPath = Path.Combine(FileSystem.CacheDirectory, "whisper-tiny.bin");
                    if (!File.Exists(modelPath))
                    {
                        // Fallback or automatic download could happen here
                        // For now, let's assume it was extracted by MainPage
                        modelPath = Path.Combine(FileSystem.AppDataDirectory, "whisper-tiny.bin");
                    }

                    await _brain.InitializeAsync(modelPath);

                    if (!_audioEngine.Start()) {
                        WeakReferenceMessenger.Default.Send(new SpeechUpdateMessage("ERR: MIC REJECTED", false, null));
                        return;
                    }

                    // Set High Priority for Audio Thread
                    Android.OS.Process.SetThreadPriority(Android.OS.ThreadPriority.UrgentAudio);

                    short[] pcmBuffer = new short[AudioBufferSize];
                    List<float> accumulatedSamples = new List<float>(SampleRate * 2);
                    int silenceCounter = 0;
                    bool isSpeechDetected = false;
                    var semaphore = new SemaphoreSlim(1, 1);

                    while (!token.IsCancellationRequested && _isListening)
                    {
                        // NO LONGER BLOCKED BY _isBusy
                        int shortsRead = _audioEngine.Read(pcmBuffer, 0, pcmBuffer.Length);
                        if (shortsRead > 0)
                        {
                            _enhancer.ProcessPcm16(pcmBuffer, shortsRead, accumulatedSamples, out float peak, out float rms);

                            float dynamicThreshold = Math.Max(VoiceActivityThreshold, _enhancer.NoiseRms * 4f);
                            bool chunkIsSpeech = peak > dynamicThreshold || rms > (dynamicThreshold * 0.4f);

                            if (chunkIsSpeech)
                            {
                                silenceCounter = 0;
                                isSpeechDetected = true;
                            }
                            else
                            {
                                silenceCounter++;

                                // Learn the current environment while we're not in a speech segment.
                                if (!isSpeechDetected)
                                {
                                    _enhancer.UpdateNoiseFloor(rms);

                                    // Avoid transcribing pure silence (and avoid unbounded accumulation).
                                    if (silenceCounter >= 30)
                                    {
                                        accumulatedSamples.Clear();
                                        silenceCounter = 0;
                                    }
                                }
                            }

                            bool shouldFinalize =
                                (isSpeechDetected && accumulatedSamples.Count >= MinSpeechSamples && silenceCounter >= SilenceChunksToFinalize) ||
                                accumulatedSamples.Count >= MaxSpeechSamples;

                            if (shouldFinalize)
                            {
                                var samplesToProcess = accumulatedSamples.ToArray();
                                accumulatedSamples.Clear();
                                silenceCounter = 0;
                                isSpeechDetected = false;

                                // NON-BLOCKING PROCESSING
                                _ = Task.Run(async () => {
                                    if (await semaphore.WaitAsync(0)) {
                                        try {
                                            var fullText = new StringBuilder();
                                            await foreach (var segmentText in _brain.ProcessAudioAsync(samplesToProcess))
                                            {
                                                if (string.IsNullOrWhiteSpace(segmentText)) continue;
                                                
                                                fullText.Append(segmentText + " ");
                                                
                                                // Send Partial Update to UI
                                                WeakReferenceMessenger.Default.Send(new SpeechUpdateMessage(fullText.ToString().Trim(), false, null));
                                            }

                                            string finalResult = fullText.ToString().Trim();
                                            if (finalResult.Length > 2 && !finalResult.Contains("[") && !finalResult.Contains("thank you")) {
                                                WeakReferenceMessenger.Default.Send(new SpeechUpdateMessage(finalResult, true, null));
                                            }
                                        } catch (Exception ex) {
                                            Console.WriteLine("Streaming error: " + ex.Message);
                                        } finally {
                                            semaphore.Release();
                                        }
                                    }
                                }, token);
                            }
                        }
                        else {
                            await Task.Delay(10);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Whisper Pump Error: " + ex.Message);
                }
                finally
                {
                    _audioEngine.Stop();
                }
            }, token);
        }

        private void StopAudioPump()
        {
            _cts?.Cancel();
            _cts = null;
        }

        private void RequestFocus()
        {
            _audioManager = (AudioManager)GetSystemService(AudioService);
            _previousAudioMode ??= _audioManager.Mode;

            try
            {
                // Helps Android route/enable voice processing (AEC/NS/AGC) more consistently.
                _audioManager.Mode = Mode.InCommunication;
            }
            catch { }

            if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
            {
                if (_focusRequest == null) {
                    _focusRequest = new AudioFocusRequestClass.Builder(AudioFocus.Gain).SetOnAudioFocusChangeListener(this).Build();
                }
                _audioManager.RequestAudioFocus(_focusRequest);
            }
            else
            {
                _audioManager.RequestAudioFocus(this, Android.Media.Stream.Music, AudioFocus.Gain);
            }
        }

        private void AbandonFocus()
        {
            var audioManager = _audioManager ?? (AudioManager)GetSystemService(AudioService);
            if (Build.VERSION.SdkInt >= BuildVersionCodes.O && _focusRequest != null)
                audioManager.AbandonAudioFocusRequest(_focusRequest);
            else
                audioManager.AbandonAudioFocus(this);

            if (_previousAudioMode is not null)
            {
                try { audioManager.Mode = _previousAudioMode.Value; } catch { }
                _previousAudioMode = null;
            }
        }

        public override void OnDestroy()
        {
            _isListening = false;
            StopAudioPump();
            _brain?.Dispose();
            base.OnDestroy();
        }
    }

    public class SpeechUpdateMessage
    {
        public string Text { get; set; }
        public bool IsFinalCommand { get; set; }
        public byte[] RawAudio { get; set; }
        public SpeechUpdateMessage(string text, bool isFinal, byte[] rawAudio = null)
        {
            Text = text; IsFinalCommand = isFinal; RawAudio = rawAudio;
        }
    }
}
