
using CommunityToolkit.Maui.Media;
using CommunityToolkit.Mvvm.Messaging;
using Jarvis.Contract;
using Jarvis.Services;
using System.Globalization;

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
    public MainPage(IContinuousMicService micService)
    {
        InitializeComponent();
        _micService = micService;

        WeakReferenceMessenger.Default.Register<SpeechUpdateMessage>(this, OnSpeechMessageReceived);
        // 2. Instantiate the AI Engine
        _aiCore = new LocalAIEngine();

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
            MainThread.BeginInvokeOnMainThread(() => TranscriptLabel.Text = "System Standby.");
            await SpeakWithUI("System Standby.");
            await CheckAndRequestMicrophonePermission();

        });

    }

    private void OnSpeechMessageReceived(object recipient, SpeechUpdateMessage message)
    {
        // ALWAYS update UI on the Main Thread!
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            if (message.IsFinalCommand)
            {
                TranscriptLabel.Text = $"> {message.Text}";

                // The user finished speaking. Send to Llama AI!
                await ProcessCommand(message.Text);
            }
            else
            {
                // The user is still talking. Show the live typing effect.
                TranscriptLabel.Text = message.Text;                
            }
        });
    }
    private async Task ExtractAIFilesToDeviceAsync()
    {
        string targetDirectory = FileSystem.AppDataDirectory;

        // The exact 6 files you downloaded from HuggingFace
        string[] aiFiles = new string[]
        {
        "model.onnx",
        "model.onnx.data",
        "genai_config.json",
        "tokenizer.json",
        "tokenizer_config.json",
        "special_tokens_map.json"
        };

        foreach (var fileName in aiFiles)
        {
            string targetFilePath = Path.Combine(targetDirectory, fileName);

            // ONLY copy if it doesn't exist. We don't want to copy 1.3GB every time!
            if (!File.Exists(targetFilePath))
            {
                //MainThread.BeginInvokeOnMainThread(() => TranscriptLabel.Text = $"Extracting {fileName}...");

                try
                {
                    using var stream = await FileSystem.OpenAppPackageFileAsync(fileName);
                    using var memoryStream = File.Create(targetFilePath);
                    await stream.CopyToAsync(memoryStream);
                }
                catch (Exception ex)
                {
                    //MainThread.BeginInvokeOnMainThread(() => TranscriptLabel.Text = $"ERR extracting {fileName}: {ex.Message}");
                    return; // Stop the boot process if a file is missing
                }
            }
        }
    }







    // --- COMMAND PROCESSING ---

    private async Task ProcessCommand(string command)
    {
        command = command.ToLower();
        SetThinkingState();
        await Task.Delay(1000); // Simulate API call / AI processing
        // 2. Check for manual override commands
        if (command.ToLower().Contains("shutdown") || command.ToLower().Contains("stop system"))
        {
            await SpeakWithUI("Powering down local host. Goodbye.");
            //OnSystemToggleClicked(null, null); // Force toggle off
            return;
        }

        // 3. Send the command to the local Llama 3.2 Model
        string aiResponse;
        try
        {
            _micService.StopListening(); // Pause ears while thinking/speaking
            aiResponse = await _aiCore.ChatLocallyAsync(command);
            if (string.IsNullOrWhiteSpace(aiResponse))
            {
                return;
            }
            _micService.StartListening();
            await SpeakWithUI(aiResponse);
        }
        catch (Exception ex)
        {
            aiResponse = "I encountered a neural net error while processing that.";
            Console.WriteLine($"AI Error: {ex.Message}");
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
        _micService.StartListening();
        await SpeakWithUI("Systems online. I am ready, Boss.");
        return true;
    }

    private void ClearStates()
    {
        _isThinking = false;
        _isSpeaking = false;
        this.AbortAnimation(AnimationHandle);

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
        var scannerDown = new Animation(v => ScannerLine.TranslationY = v, 0, 360, Easing.SinInOut);
        var scannerUp = new Animation(v => ScannerLine.TranslationY = v, 360, 0, Easing.SinInOut);

        // 2. Slow, multi-layered data ring rotation
        var ring1Spin = new Animation(v => DataRing1.Rotation = v, 0, 360);
        var ring2Spin = new Animation(v => DataRing2.Rotation = v, 0, -360);
        var qRingSpin = new Animation(v => QuantumRing.Rotation = v, 0, 360);

        // 3. Very subtle nanotech breathing
        var coreBreatheUp = new Animation(v => MainCore.Scale = v, 0.98, 1.02, Easing.CubicInOut);
        var coreBreatheDown = new Animation(v => MainCore.Scale = v, 1.02, 0.98, Easing.CubicInOut);

        parentAnim.Add(0, 0.5, scannerDown); parentAnim.Add(0.5, 1, scannerUp);
        parentAnim.Add(0, 1, ring1Spin); parentAnim.Add(0, 1, ring2Spin); parentAnim.Add(0, 1, qRingSpin);
        parentAnim.Add(0, 0.5, coreBreatheUp); parentAnim.Add(0.5, 1, coreBreatheDown);

        parentAnim.Commit(this, AnimationHandle, length: 8000, repeat: () => true);
    }

    private void SetThinkingState()
    {
        ClearStates();
        _isThinking = true;

        // Background high-speed data spin
        new Animation(v => DataRing2.Rotation = v, 0, 720).Commit(this, AnimationHandle, length: 2000, repeat: () => true);
        new Animation(v => QuantumRing.Rotation = v, 0, -360).Commit(this, AnimationHandle + "2", length: 1000, repeat: () => true);

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
    private async Task SpeakWithUI(string textToSay)
    {
        // 1. Trigger the visual "Speaking" state (Holographic Sonar Ripples)
        SetSpeakingState();

        // 2. Configure the voice (Optional: Adjust pitch to sound more robotic/AI)
        var speechOptions = new SpeechOptions()
        {
            Pitch = 1.2f,  // 1.0 is normal. Higher is more feminine/synthetic, lower is deeper.
            Volume = 1.0f
        };

        // 3. Speak the text and await its completion
        // The UI will continue its random 200ms speaking loop while this runs
        await TextToSpeech.Default.SpeakAsync(textToSay, speechOptions);
        // 4. THE LOOP TRIGGER: Once speech finishes, listen again!

        // 4. Speech is finished. Return the visual UI to the normal idle state.
        SetNormalState();
    }

}
