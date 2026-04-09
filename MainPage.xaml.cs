
using CommunityToolkit.Maui.Media;
using CommunityToolkit.Mvvm.Messaging;
using Jarvis.Contract;
using Jarvis.Services;
using Jarvis.Models;
using System.Globalization;
using System.Text;
using System.Collections.ObjectModel;

namespace Jarvis;

public partial class MainPage : ContentPage
{
    private Animation _masterAnimation;
    private readonly IContinuousMicService _micService;
    private const string AnimationHandle = "ArcReactorAnim";
    private bool _isThinking = false;
    private bool _isSpeaking = false;
    private Random _rand = new Random();
    private bool _isSystemActive = false;
    // --- THE A.I. BRAIN ---
    private LocalAIEngine _aiCore;
    // private BiometricService _biometricService; // Removed biometrics
    private JarvisDatabase _db;
    private Jarvis.Models.UserProfile _cachedUser;
    private bool _isAwaitingNewUserName = false;
    private float[] _pendingVoicePrint;
    private CancellationTokenSource _currentAiCts;
    private ObservableCollection<ChatHistory> _chatItems = new();
    public MainPage(IContinuousMicService micService)
    {
        InitializeComponent();
        _micService = micService;

        HistoryView.ItemsSource = _chatItems;
        WeakReferenceMessenger.Default.Register<SpeechUpdateMessage>(this, OnSpeechMessageReceived);
        // 2. Instantiate the AI Engine and Services
        _aiCore = new LocalAIEngine();
        _db = new JarvisDatabase();

        // 3. Boot the AI in the background
        // IMPORTANT: Change this path to wherever you extracted the ONNX files on the device!
        string modelDirectory = FileSystem.AppDataDirectory;



        Task.Run(async () =>
        {
           
            // 1. Extract files to the physical phone storage FIRST
            await ExtractAIFilesToDeviceAsync();
            MainThread.BeginInvokeOnMainThread(() => TranscriptLabel.Text = "Waking up Neural Net...");
            await SpeakWithUI("Booting Neural Net...");
            await _aiCore.InitializeAsync(modelDirectory);
            // _biometricService.Initialize(Path.Combine(modelDirectory, "ecapa_tdnn.onnx")); // Removed biometrics
            MainThread.BeginInvokeOnMainThread(() => TranscriptLabel.Text = "System Standby.");
            await SpeakWithUI("System Standby.");

            // Initialize current user after everything is ready
            var allUsers = await _db.GetAllUsersAsync();
            if (allUsers.Count > 0) 
            {
                _cachedUser = allUsers[0];
                var history = await _db.GetFullHistoryAsync(_cachedUser.Id, 20);
                MainThread.BeginInvokeOnMainThread(() => {
                    foreach (var h in history) _chatItems.Add(h);
                    ScrollToBottom();
                });
            }
            
            // Turn on Mic only after everything is loaded
            _micService.StartListening();
            MainThread.BeginInvokeOnMainThread(() => {
                TranscriptLabel.Text = "Listening...";
                try { Vibration.Default.Vibrate(TimeSpan.FromMilliseconds(150)); } catch { }
            });
        });

    }

    // This runs automatically right after the UI finishes drawing on the screen
    protected override async void OnAppearing()
    {
        base.OnAppearing();

        // Request permissions safely on the UI thread
        await CheckAndRequestMicrophonePermission();

        // Optional: Start the idle animation as soon as the app opens
        SetNormalState();
    }

    private bool _isProcessing = false;
    private void OnSpeechMessageReceived(object recipient, SpeechUpdateMessage message)
    {
        // Handle UI updates for partial results immediately on MainThread
        if (!message.IsFinalCommand)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                // Real-time interruption check
                if ((_isSpeaking || _isThinking) && !string.IsNullOrWhiteSpace(message.Text) && message.Text.Length > 5)
                {
                    _currentAiCts?.Cancel();
                }
                TranscriptLabel.Text = message.Text;
            });
            return;
        }

        // Final command processing should start in background
        Task.Run(async () =>
        {
            if (_isProcessing) return; // Prevent overlapping commands
            _isProcessing = true;

            try
            {
                // 1. Kill any active AI/TTS tasks
                _currentAiCts?.Cancel();
                _currentAiCts = new CancellationTokenSource();
                var ct = _currentAiCts.Token;

                MainThread.BeginInvokeOnMainThread(() => TranscriptLabel.Text = $"> {message.Text}");
                
                await ProcessCommand(message.Text, message.RawAudio, ct);
            }
            finally
            {
                _isProcessing = false;
            }
        });
    }
    private async Task ExtractAIFilesToDeviceAsync()
    {
        string targetDirectory = FileSystem.AppDataDirectory;

        // The exact files you downloaded from HuggingFace
        string[] aiFiles = new string[]
        {
        // "ecapa_tdnn.onnx", // Removed biometrics
        "model.onnx",
        "model.onnx.data",
        "genai_config.json",
        "tokenizer.json",
        "tokenizer_config.json",
        "special_tokens_map.json"
        };

        var extractionTasks = aiFiles.Select(async fileName =>
        {
            string targetFilePath = Path.Combine(targetDirectory, fileName);
            if (!File.Exists(targetFilePath))
            {
                using var stream = await FileSystem.OpenAppPackageFileAsync(fileName);
                using var memoryStream = File.Create(targetFilePath, 4096, FileOptions.Asynchronous);
                await stream.CopyToAsync(memoryStream);
            }
        });

        try
        {
            await Task.WhenAll(extractionTasks);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Extraction error: {ex.Message}");
            return;
        }
    }







    // --- COMMAND PROCESSING ---

    private async Task ProcessCommand(string command, byte[] rawAudio, CancellationToken ct)
    {
        try
        {
            command = command.ToLower();
            MainThread.BeginInvokeOnMainThread(() => SetThinkingState());
            await Task.Delay(100, ct); 

            // IDENTIFICATION: Check if we have a cached user or need to find/prompt
            if (_cachedUser == null)
            {
                if (_isAwaitingNewUserName)
                {
                    string parsedName = ParseNameFromText(command);
                    _cachedUser = await _db.GetOrCreateUserAsync(parsedName);
                    _isAwaitingNewUserName = false;
                    await SpeakWithUI($"Welcome to the system, {parsedName}.", ct);
                    return;
                }
                else
                {
                    // Check if any user exists in DB
                    var allUsers = await _db.GetAllUsersAsync();
                    if (allUsers.Count > 0)
                    {
                        // Default to the first user for now, or we could ask "Who is speaking?" 
                        // But since we removed biometrics, we'll just use the last used or first one.
                        _cachedUser = allUsers[0];
                    }
                    else
                    {
                        _isAwaitingNewUserName = true;
                        await SpeakWithUI("Identify yourself. What is your name?", ct);
                        return;
                    }
                }
            }

            var currentUser = _cachedUser;

            var history = await _db.GetRecentHistoryAsync(currentUser.Id, limit: 6);
            
            // Log User Message to UI
            var userMsg = new ChatHistory { Role = currentUser.Name, Message = command, Timestamp = DateTime.Now };
            MainThread.BeginInvokeOnMainThread(() => {
                _chatItems.Add(userMsg);
                ScrollToBottom();
            });
            await _db.SaveMessageAsync(currentUser.Id, currentUser.Name, command);

            StringBuilder fullResponse = new StringBuilder();
            StringBuilder sentenceBuffer = new StringBuilder();
            
            // Stop mic to prevent Jarvis from hearing himself
            _micService.StopListening(); 
            SetSpeakingState();

            try {
                await foreach (var token in _aiCore.StreamChatLocallyAsync(command, history, ct))
                {
                    if (ct.IsCancellationRequested) break;

                    string cleanToken = token.Replace("<|eot_id|>", "").Replace("<|end_of_text|>", "");
                    fullResponse.Append(cleanToken);
                    sentenceBuffer.Append(cleanToken);

                    // Throttle UI updates to every 3 tokens or on sentence completion
                    if (fullResponse.Length % 3 == 0 || cleanToken.Contains("."))
                    {
                        MainThread.BeginInvokeOnMainThread(() => TranscriptLabel.Text = fullResponse.ToString());
                    }

                    if (cleanToken.Contains(".") || cleanToken.Contains("!") || cleanToken.Contains("?"))
                    {
                        string toSpeak = sentenceBuffer.ToString().Trim();
                        if (!string.IsNullOrWhiteSpace(toSpeak))
                        {
                            await TextToSpeech.Default.SpeakAsync(toSpeak, new SpeechOptions { Pitch = 1.0f, Volume = 1.0f }, ct);
                            sentenceBuffer.Clear();
                        }
                    }
                }

                if (!ct.IsCancellationRequested)
                {
                    string finalRemainder = sentenceBuffer.ToString().Trim();
                    if (!string.IsNullOrWhiteSpace(finalRemainder))
                    {
                        await TextToSpeech.Default.SpeakAsync(finalRemainder, new SpeechOptions { Pitch = 1.0f, Volume = 1.0f }, ct);
                    }

                    if (fullResponse.Length > 0)
                    {
                        var responseText = fullResponse.ToString().Trim();
                        var jarvisMsg = new ChatHistory { Role = "Jarvis", Message = responseText, Timestamp = DateTime.Now };
                        MainThread.BeginInvokeOnMainThread(() => {
                            _chatItems.Add(jarvisMsg);
                            ScrollToBottom();
                        });
                        await _db.SaveMessageAsync(currentUser.Id, "Jarvis", responseText);
                    }
                }
            }
            catch (OperationCanceledException) { 
                 // User interrupted, do nothing special, finally block will clean up
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"System Error: {ex.Message}");
            await SpeakWithUI("System error in neural net. Attempting to recover.");
        }
        finally
        {
            _isSpeaking = false;
            MainThread.BeginInvokeOnMainThread(() => {
                ClearStates();
                SetNormalState();
            });
            
            // Turn Mic Back on and Notify User
            _micService.StartListening();
            
            MainThread.BeginInvokeOnMainThread(() => {
                TranscriptLabel.Text = "Listening...";
                // Haptic feedback to indicate mic is hot
                try { Vibration.Default.Vibrate(TimeSpan.FromMilliseconds(100)); } catch { }
            });
        }
    }

    // Helper method to handle Android/iOS permissions
    private async Task<bool> CheckAndRequestMicrophonePermission()
    {
        var status = await Permissions.CheckStatusAsync<Permissions.Microphone>();
        if (status != PermissionStatus.Granted) status = await Permissions.RequestAsync<Permissions.Microphone>();

        if (status != PermissionStatus.Granted)
        {
            TranscriptLabel.Text = "ERR: Microphone permission denied.";
            _isSystemActive = false;
            return false;
        }
       
       // await SpeakWithUI("Systems online. I am ready.");
        return true;
    }

    private void ClearStates()
    {
        _isThinking = false;
        _isSpeaking = false;
        this.AbortAnimation(AnimationHandle);
        this.AbortAnimation(AnimationHandle + "2");
        this.AbortAnimation(AnimationHandle + "3");

        // Reset Visuals
        MainCore.Scale = 1; MainCore.Opacity = 1;
        GlitchCyan.Opacity = 0; GlitchRed.Opacity = 0;
        SonarRipple1.Opacity = 0; SonarRipple2.Opacity = 0;
        SonarRipple1.Scale = 1; SonarRipple2.Scale = 1;
        ScannerLine.TranslationY = 0;
    }

    private void SetNormalState()
    {
        ClearStates();
        var parentAnim = new Animation();

        // 1. Holographic Scanner moving up and down the grid
        var scannerDown = new Animation(v => ScannerLine.TranslationY = v, 0, 800, Easing.SinInOut);
        var scannerUp = new Animation(v => ScannerLine.TranslationY = v, 800, 0, Easing.SinInOut);

        // 2. High-Tech orbital rotation
        var ring1Spin = new Animation(v => DataRing1.Rotation = v, 0, 360);
        var ring2Spin = new Animation(v => DataRing2.Rotation = v, 0, -360);
        var qRingSpin = new Animation(v => QuantumRing.Rotation = v, 0, 360);
        var inner1Spin = new Animation(v => InnerRing1.Rotation = v, 0, -720);
        var inner2Spin = new Animation(v => InnerRing2.Rotation = v, 0, 360);

        // 3. Subtle nanotech breathing
        var coreBreatheUp = new Animation(v => MainCore.Scale = v, 0.95, 1.05, Easing.CubicInOut);
        var coreBreatheDown = new Animation(v => MainCore.Scale = v, 1.05, 0.95, Easing.CubicInOut);

        parentAnim.Add(0, 0.5, scannerDown); parentAnim.Add(0.5, 1, scannerUp);
        parentAnim.Add(0, 1, ring1Spin); parentAnim.Add(0, 1, ring2Spin); parentAnim.Add(0, 1, qRingSpin);
        parentAnim.Add(0, 1, inner1Spin); parentAnim.Add(0, 1, inner2Spin);
        parentAnim.Add(0, 0.5, coreBreatheUp); parentAnim.Add(0.5, 1, coreBreatheDown);

        parentAnim.Commit(this, AnimationHandle, length: 10000, repeat: () => true);
    }

    private void SetThinkingState()
    {
        ClearStates();
        _isThinking = true;

        // Background high-speed technical spin
        new Animation(v => InnerRing1.Rotation = v, 0, 1080).Commit(this, AnimationHandle, length: 1500, repeat: () => true);
        new Animation(v => InnerRing2.Rotation = v, 0, -720).Commit(this, AnimationHandle + "2", length: 1000, repeat: () => true);
        new Animation(v => QuantumRing.Rotation = v, 0, 360).Commit(this, AnimationHandle + "3", length: 500, repeat: () => true);

        // REAL-TIME CHROMATIC GLITCH ENGINE
        // This makes the core look like it's calculating so fast it's distorting reality
        Application.Current.Dispatcher.StartTimer(TimeSpan.FromMilliseconds(50), () =>
        {
            if (!_isThinking) return false;

            // 30% chance to glitch on any given 50ms frame
            if (_rand.NextDouble() > 0.7)
            {
                // Jitter the glitch layers
                GlitchCyan.TranslationX = _rand.Next(-8, 8);
                GlitchCyan.TranslationY = _rand.Next(-8, 8);
                GlitchRed.TranslationX = _rand.Next(-8, 8);
                GlitchRed.TranslationY = _rand.Next(-8, 8);

                // Flash them visible
                GlitchCyan.Opacity = 0.8;
                GlitchRed.Opacity = 0.8;
                MainCore.Opacity = 0.5; // Dim main core to show glitch
            }
            else
            {
                // Snap back to normal
                GlitchCyan.Opacity = 0;
                GlitchRed.Opacity = 0;
                MainCore.Opacity = 1;
            }

            return true;
        });
    }

    private void SetSpeakingState()
    {
        ClearStates();
        _isSpeaking = true;

        // Idle slow spin in background
        new Animation(v => DataRing1.Rotation = v, 0, 360).Commit(this, AnimationHandle, length: 10000, repeat: () => true);

        // VOLUMETRIC SONAR ENGINE
        // Creates expanding rings that fade out, syncing with a voice core
        Application.Current.Dispatcher.StartTimer(TimeSpan.FromMilliseconds(200), () =>
        {
            if (!_isSpeaking) return false;

            double amplitude = 1.0 + (_rand.NextDouble() * 0.8); // Scale between 1.0 and 1.8

            // Sharp core snap
            MainCore.ScaleTo(amplitude * 0.8, 100, Easing.SpringOut);

            // Only fire a ripple if the amplitude is high (simulating a loud syllable)
            if (amplitude > 1.3)
            {
                // Reset ripple 1
                SonarRipple1.Scale = 0.5;
                SonarRipple1.Opacity = 1;

                // Animate Ripple 1 (Expands and fades)
                SonarRipple1.ScaleTo(amplitude * 1.5, 400, Easing.CubicOut);
                SonarRipple1.FadeTo(0, 400, Easing.CubicIn);

                // Delay Ripple 2 slightly for a double-echo effect
                Device.StartTimer(TimeSpan.FromMilliseconds(100), () =>
                {
                    if (!_isSpeaking) return false;
                    SonarRipple2.Scale = 0.5;
                    SonarRipple2.Opacity = 0.8;
                    SonarRipple2.ScaleTo(amplitude * 1.2, 400, Easing.CubicOut);
                    SonarRipple2.FadeTo(0, 400, Easing.CubicIn);
                    return false;
                });
            }
            return true;
        });
    }

    /// <summary>
    /// Synchronizes the TTS voice with the Holographic Speaking UI
    /// </summary>
    private async Task SpeakWithUI(string textToSay, CancellationToken ct = default)
    {
        // 1. Trigger the visual "Speaking" state (Holographic Sonar Ripples)
        MainThread.BeginInvokeOnMainThread(() => SetSpeakingState());

        // 2. Configure the voice (Optional: Adjust pitch to sound more robotic/AI)
        var speechOptions = new SpeechOptions()
        {
            Pitch = 1.0f,  // 1.0 is natural human pitch.
            Volume = 1.0f
        };

        // 3. Speak the text and await its completion
        // The UI will continue its random 200ms speaking loop while this runs
        try 
        {
            await TextToSpeech.Default.SpeakAsync(textToSay, speechOptions, ct);
        }
        catch (OperationCanceledException) { } // Silence cancel logs

        // 4. Speech is finished. Return the visual UI to the normal idle state.
        MainThread.BeginInvokeOnMainThread(() => SetNormalState());
    }

    /// <summary>
    /// Extract a name from natural language input (e.g., "My name is John" -> "John")
    /// </summary>
    private string ParseNameFromText(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "User";

        string cleanText = text.Trim().TrimEnd('.', '!', '?');
        string[] prefixes = { "my name is ", "i am ", "i'm ", "call me ", "it's ", "it is ", "this is " };

        foreach (var prefix in prefixes)
        {
            if (cleanText.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                string result = cleanText.Substring(prefix.Length).Trim();
                // Capitalize first letter
                if (result.Length > 0)
                    return char.ToUpper(result[0]) + result.Substring(1);
                return result;
            }
        }

        // Fallback: return the whole thing capitalized
        if (cleanText.Length > 0)
            return char.ToUpper(cleanText[0]) + cleanText.Substring(1);

        return cleanText;
    }

    private void ScrollToBottom()
    {
        if (_chatItems.Count > 0)
        {
            HistoryView.ScrollTo(_chatItems.Count - 1, position: ScrollToPosition.End, animate: true);
        }
    }
}
