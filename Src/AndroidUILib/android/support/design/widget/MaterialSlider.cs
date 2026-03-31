using AndroidInteropLib.android.content;
using AndroidInteropLib.android.util;
using AndroidInteropLib.android.view;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace AndroidInteropLib.android.support.design.widget
{
    public class MaterialSlider : View
    {
        private Slider slider = new Slider();

        public MaterialSlider(Context context, AttributeSet attrs) : base(context, attrs) { }

        public override void CreateWinUI(params object[] obj)
        {
            slider.HorizontalAlignment = HorizontalAlignment.Stretch;
            WinUI.Content = slider;
        }

        public void setValueFrom(float from)
        {
            slider.Minimum = from;
        }

        public void setValueTo(float to)
        {
            slider.Maximum = to;
        }

        public void setValue(float value)
        {
            slider.Value = value;
        }

        public float getValue() { return (float)slider.Value; }

        public void setStepSize(float stepSize)
        {
            if (stepSize > 0)
                slider.StepFrequency = stepSize;
        }
    }
}
