namespace Visor.Controls;

public partial class VisorHudView : ContentView
{
    public VisorHudView()
    {
        InitializeComponent();
        StartAnimations();
    }

    private void StartAnimations()
    {
        // 1. Arc Reactor Rotation
        var reactorAnim = new Animation();
        reactorAnim.Add(0, 1, new Animation(v => OuterRing.Rotation = v, 0, 360));
        reactorAnim.Add(0, 1, new Animation(v => MiddleRing.Rotation = v, 360, 0));
        reactorAnim.Commit(this, "ReactorLoop", length: 8000, repeat: () => true);

        // 2. Core & Status Pulse
        var pulseAnim = new Animation(v => {
            Core.Opacity = v;
            StatusDot.Opacity = v;
        }, 0.4, 1.0);
        pulseAnim.Commit(this, "PulseLoop", length: 1500, easing: Easing.SinInOut, repeat: () => true);

        // 3. Scanline Drift
        this.Loaded += (s, e) => {
            var scanAnim = new Animation(v => Scanline.TranslationY = v, 0, this.Height > 0 ? this.Height : 600);
            scanAnim.Commit(this, "ScanLoop", length: 3000, repeat: () => true);
        };
    }
}