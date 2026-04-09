using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Media;
using Android.OS;
using Android.Speech;
using AndroidX.Core.App;
using CommunityToolkit.Mvvm.Messaging;
using Encoding = Android.Media.Encoding;
using Resource = Microsoft.Maui.Resource;

namespace Jarvis.Services
{
    [Service(Name = "com.jarvis.ContinuousMicService", ForegroundServiceType = global::Android.Content.PM.ForegroundService.TypeMicrophone)]
    public class AndroidContinuousMicService : Service, IRecognitionListener
    {
        public const string ChannelId = "JarvisMicChannel";
        public const int NotificationId = 1001;

        private SpeechRecognizer _speechRecognizer;
        private Intent _speechIntent;
        private bool _isListening;

        public override IBinder OnBind(Intent intent) => null;

        public override StartCommandResult OnStartCommand(Intent intent, StartCommandFlags flags, int startId)
        {
            if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
            {
                var channel = new NotificationChannel(ChannelId, "Native Mic Service", NotificationImportance.Low);
                ((NotificationManager)GetSystemService(NotificationService)).CreateNotificationChannel(channel);
            }

            var notification = new NotificationCompat.Builder(this, ChannelId)
                .SetContentTitle("F.R.I.D.A.Y. Online")
                .SetContentText("Native OS Audio active.")
                .SetSmallIcon(Resource.Drawable.dotnet_bot)
                .SetOngoing(true)
                .Build();

            StartForeground(NotificationId, notification, global::Android.Content.PM.ForegroundService.TypeMicrophone);
            _isListening = true;

            MainThread.BeginInvokeOnMainThread(() =>
            {
                InitializeNativeSpeech();
                StartListeningLoop();
            });

            return StartCommandResult.Sticky;
        }

        private CancellationTokenSource _silenceCts;

        private void InitializeNativeSpeech()
        {
            try
            {
                if (_speechRecognizer != null)
                {
                    _speechRecognizer.StopListening();
                    _speechRecognizer.Destroy();
                    _speechRecognizer = null;
                }
            }
            catch { }

            _speechRecognizer = SpeechRecognizer.CreateSpeechRecognizer(this);
            _speechRecognizer.SetRecognitionListener(this);

            _speechIntent = new Intent(RecognizerIntent.ActionRecognizeSpeech);
            _speechIntent.PutExtra(RecognizerIntent.ExtraLanguageModel, RecognizerIntent.LanguageModelFreeForm);
            _speechIntent.PutExtra(RecognizerIntent.ExtraPartialResults, true);
            _speechIntent.PutExtra(RecognizerIntent.ExtraMaxResults, 1);
            _speechIntent.PutExtra(RecognizerIntent.ExtraLanguage, Java.Util.Locale.Default.ToString());
            _speechIntent.PutExtra(RecognizerIntent.ExtraConfidenceScores, true);
        }

        private void StartListeningLoop()
        {
            if (_isListening && _speechRecognizer != null)
            {
                _speechRecognizer.StartListening(_speechIntent);
            }
        }

        private void ResetSilenceTimer()
        {
            _silenceCts?.Cancel();
            _silenceCts = new CancellationTokenSource();
            var token = _silenceCts.Token;

            // Manual endpointing: if no speech for 1.8s, force completion
            Task.Delay(1800, token).ContinueWith(t =>
            {
                if (!t.IsCanceled && _isListening)
                {
                    MainThread.BeginInvokeOnMainThread(() => _speechRecognizer?.StopListening());
                }
            });
        }

        public void OnPartialResults(Bundle partialResults)
        {
            var matches = partialResults.GetStringArrayList(SpeechRecognizer.ResultsRecognition);
            if (matches != null && matches.Count > 0)
            {
                ResetSilenceTimer();
                WeakReferenceMessenger.Default.Send(new SpeechUpdateMessage(matches[0], false));
            }
        }

        private List<byte> _audioBuffer = new List<byte>();
        // private AudioRecord _rawRecorder; // Removed biometrics
        // private bool _isRecordingRaw = false; // Removed biometrics

        public void OnReadyForSpeech(Bundle? @params) { }
        
        public void OnBeginningOfSpeech() 
        { 
            ResetSilenceTimer();
        }

        public void OnRmsChanged(float rmsdB) { }
        public void OnBufferReceived(byte[] buffer) { }
        public void OnEndOfSpeech() { }
        public void OnEvent(int eventType, Bundle? @params) { }

        public void OnResults(Bundle results)
        {
            _silenceCts?.Cancel();
            var matches = results.GetStringArrayList(SpeechRecognizer.ResultsRecognition);
            if (matches != null && matches.Count > 0)
            {
                WeakReferenceMessenger.Default.Send(new SpeechUpdateMessage(matches[0], true));
            }

            // Restart loop automatically after user finishes a sentence
            // IMPORTANT: Slight delay to ensure hardware is released
            Task.Delay(500).ContinueWith(_ => {
                if (_isListening) MainThread.BeginInvokeOnMainThread(() => StartListeningLoop());
            });
        }

        private int _errorCount = 0;

        public void OnError(SpeechRecognizerError error)
        {
            _silenceCts?.Cancel();
            _errorCount++;

            // If we hit a critical 'Busy/Audio' crash or have 3 consecutive failures:
            // Destroy the entire engine and rebuild.
            if (_errorCount > 3 || error == SpeechRecognizerError.RecognizerBusy || error == SpeechRecognizerError.Audio)
            {
                _errorCount = 0;
                Task.Delay(1000).ContinueWith(_ =>
                {
                    if (_isListening) MainThread.BeginInvokeOnMainThread(() =>
                    {
                        try
                        {
                            _speechRecognizer?.StopListening();
                            _speechRecognizer?.Destroy();
                            _speechRecognizer = null;
                        }
                        catch { }

                        InitializeNativeSpeech();
                        StartListeningLoop();
                    });
                });
                return;
            }

            // Silent backoff for standard errors
            Task.Delay(800).ContinueWith(_ =>
            {
                if (_isListening) MainThread.BeginInvokeOnMainThread(() => StartListeningLoop());
            });
        }

        public override void OnDestroy()
        {
            _isListening = false;
            if (_speechRecognizer != null)
            {
                _speechRecognizer.StopListening();
                _speechRecognizer.Destroy();
            }
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
            Text = text;
            IsFinalCommand = isFinal;
            RawAudio = rawAudio;
        }
    }
}
