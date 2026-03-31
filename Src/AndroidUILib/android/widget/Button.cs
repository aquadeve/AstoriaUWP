using AndroidInteropLib.android.content;
using AndroidInteropLib.android.util;
using Windows.UI.Xaml.Controls;

namespace AndroidInteropLib.android.widget
{
    public class Button : TextView
    {
        private Windows.UI.Xaml.Controls.Button button;

        public Button()
             : base(null, null)
        {
        }

        public Button(Context context, AttributeSet attrs)
        : base(context, attrs)
        {
        }

        public override void CreateWinUI(params object[] obj)
        {
            button = new Windows.UI.Xaml.Controls.Button();
            this.WinUI = button;
            base.CreateWinUI(obj);
        }

        public override void setText(string text)
        {
            if (button != null)
                button.Content = text;
            else
                base.setText(text);
        }

        // Additional Android Button APIs can be mapped here
    }
}