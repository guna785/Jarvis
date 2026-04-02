namespace Jarvis;

public partial class MainPage : ContentPage
{
	int count = 0;
    private Animation _masterAnimation;
    public MainPage()
	{
		InitializeComponent();
        StartDetailedAnimation();

    }
    private void StartDetailedAnimation()
    {
        // Abort previous to prevent stack
        this.AbortAnimation("DetailedReactorLoop");

        _masterAnimation = new Animation();

        // 1. ENERGY GATE: Slow Clockwise
        var gateRotation = new Animation(v => EnergyGate.Rotation = v, 0, 360);

        // 2. TEXT ORBIT: Fast Counter-Clockwise (Dissonance effect)
        var textRotation = new Animation(v => TextOrbit.Rotation = v, 360, 0);

        // 3. MAIN CORE: Breathing/Scale Pulse
        var corePulse = new Animation();
        corePulse.Add(0, 0.5, new Animation(v => MainCore.Scale = v, 1.0, 1.05, Easing.SinIn));
        corePulse.Add(0.5, 1, new Animation(v => MainCore.Scale = v, 1.05, 1.0, Easing.SinOut));

        // 4. BRIGHT CENTER: Intermittent "Flicker" (More human-like)
        var centerFlicker = new Animation();
        centerFlicker.Add(0, 0.2, new Animation(v => CoreCenter.Opacity = v, 0.9, 1.0));
        centerFlicker.Add(0.2, 0.25, new Animation(v => CoreCenter.Opacity = v, 1.0, 0.7)); // Flicker out
        centerFlicker.Add(0.25, 0.3, new Animation(v => CoreCenter.Opacity = v, 0.7, 1.0)); // Flicker in
        centerFlicker.Add(0.3, 1, new Animation(v => CoreCenter.Opacity = v, 1.0, 0.9));

        // Add them to the master loop
        _masterAnimation.Add(0, 1, gateRotation);   // 0% - 100% of duration
        _masterAnimation.Add(0, 1, textRotation);   // 0% - 100% of duration
        _masterAnimation.Add(0, 0.5, corePulse);    // 0% - 50%
        _masterAnimation.Add(0.5, 1, corePulse);    // 50% - 100%
        _masterAnimation.Add(0, 1, centerFlicker);  // 0% - 100%

        // Run infinitely
        _masterAnimation.Commit(this, "DetailedReactorLoop", length: 12000, repeat: () => true);
    }
    private void OnCounterClicked(object? sender, EventArgs e)
	{
		count++;

		if (count == 1)
			CounterBtn.Text = $"Clicked {count} time";
		else
			CounterBtn.Text = $"Clicked {count} times";

		SemanticScreenReader.Announce(CounterBtn.Text);
	}
}
