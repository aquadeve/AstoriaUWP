using AndroidInteropLib.android.content;
using AndroidInteropLib.android.util;
using AndroidInteropLib.org.xmlpull.v1;

namespace AndroidInteropLib.android.widget
{
    public class RadioButton : CompoundButton
    {
        private Windows.UI.Xaml.Controls.RadioButton radioButton = new Windows.UI.Xaml.Controls.RadioButton();

        public RadioButton(Context context, AttributeSet attrs) : base(context, attrs) { }

        public override void CreateWinUI(params object[] obj)
        {
            radioButton.Checked += (s, e) => { mChecked = true; };
            radioButton.Unchecked += (s, e) => { mChecked = false; };
            WinUI.Content = radioButton;

            if (obj != null && obj.Length > 1 && obj[1] is AttributeSet a)
            {
                string text = a.getAttributeValue(XmlPullParser.ANDROID_NAMESPACE, "text");
                if (text != null) radioButton.Content = text;

                string checked_ = a.getAttributeValue(XmlPullParser.ANDROID_NAMESPACE, "checked");
                if (checked_ != null) radioButton.IsChecked = checked_ == "true";
            }
        }

        public override void setText(string text)
        {
            radioButton.Content = text;
        }

        public override void setChecked(bool checked_)
        {
            base.setChecked(checked_);
            radioButton.IsChecked = checked_;
        }

        public override bool isChecked()
        {
            return radioButton.IsChecked == true;
        }
    }
}
