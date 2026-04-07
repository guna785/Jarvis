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

        private void InitializeNativeSpeech()
        {
            _speechRecognizer = SpeechRecognizer.CreateSpeechRecognizer(this);
            _speechRecognizer.SetRecognitionListener(this);

            _speechIntent = new Intent(RecognizerIntent.ActionRecognizeSpeech);
            _speechIntent.PutExtra(RecognizerIntent.ExtraLanguageModel, RecognizerIntent.LanguageModelFreeForm);
            _speechIntent.PutExtra(RecognizerIntent.ExtraPartialResults, true);
            _speechIntent.PutExtra(RecognizerIntent.ExtraPreferOffline, true);
        }

        private void StartListeningLoop()
        {
            if (_isListening && _speechRecognizer != null)
            {
                _speechRecognizer.StartListening(_speechIntent);
            }
        }

        public void OnPartialResults(Bundle partialResults)
        {
            var matches = partialResults.GetStringArrayList(SpeechRecognizer.ResultsRecognition);
            if (matches != null && matches.Count > 0)
            {
                WeakReferenceMessenger.Default.Send(new SpeechUpdateMessage(matches[0], false));
            }
        }

        public void OnResults(Bundle results)
        {
            var matches = results.GetStringArrayList(SpeechRecognizer.ResultsRecognition);
            if (matches != null && matches.Count > 0)
            {
                WeakReferenceMessenger.Default.Send(new SpeechUpdateMessage(matches[0], true));
            }

            // Restart loop automatically after user finishes a sentence
            MainThread.BeginInvokeOnMainThread(() => StartListeningLoop());
        }

        public void OnError(SpeechRecognizerError error)
        {
            if (error == SpeechRecognizerError.NoMatch || error == SpeechRecognizerError.SpeechTimeout)
            {
                MainThread.BeginInvokeOnMainThread(() => StartListeningLoop());
            }
            else if (error == SpeechRecognizerError.Client)
            {
                // Client error usually means it was intentionally stopped via UI, do nothing.
            }
            else
            {
                WeakReferenceMessenger.Default.Send(new SpeechUpdateMessage($"MIC RESTARTING: {error}", false));
                MainThread.BeginInvokeOnMainThread(() => StartListeningLoop());
            }
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

        public void OnReadyForSpeech(Bundle? @params) { }
        public void OnBeginningOfSpeech() { }
        public void OnRmsChanged(float rmsdB) { }
        public void OnBufferReceived(byte[] buffer) { }
        public void OnEndOfSpeech() { }
        public void OnEvent(int eventType, Bundle? @params) { }

    }

    public class SpeechUpdateMessage
    {
        public string Text { get; set; }
        public bool IsFinalCommand { get; set; }

        public SpeechUpdateMessage(string text, bool isFinal)
        {
            Text = text;
            IsFinalCommand = isFinal;
        }
    }
}
