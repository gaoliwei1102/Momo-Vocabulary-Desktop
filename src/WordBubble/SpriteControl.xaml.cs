using System.Windows.Media.Animation;

namespace WordBubble;

public partial class SpriteControl : UserControl
{
    public SpriteControl() => InitializeComponent();

    public void Blink()
    {
        if (!SystemParameters.ClientAreaAnimation) return;
        var blink = new DoubleAnimation(1, 0.12, TimeSpan.FromMilliseconds(95)) { AutoReverse = true };
        EyeScale.BeginAnimation(ScaleTransform.ScaleYProperty, blink);
        var leaf = new DoubleAnimationUsingKeyFrames();
        leaf.KeyFrames.Add(new EasingDoubleKeyFrame(-9, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(100))));
        leaf.KeyFrames.Add(new EasingDoubleKeyFrame(5, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(220))));
        leaf.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(380))));
        LeafTilt.BeginAnimation(RotateTransform.AngleProperty, leaf);
    }

    public void React(string mood = "open")
    {
        Smile.Data = System.Windows.Media.Geometry.Parse(mood == "familiar" ? "M 33,52 Q 38,59 43,52" : "M 34,52 Q 38,56 42,52");
        if (!SystemParameters.ClientAreaAnimation) return;
        var bounce = new DoubleAnimationUsingKeyFrames();
        bounce.KeyFrames.Add(new EasingDoubleKeyFrame(0.86, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(80))));
        bounce.KeyFrames.Add(new EasingDoubleKeyFrame(1.06, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(190))));
        bounce.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(330))));
        Squash.BeginAnimation(ScaleTransform.ScaleYProperty, bounce);
        Blink();
    }
}
