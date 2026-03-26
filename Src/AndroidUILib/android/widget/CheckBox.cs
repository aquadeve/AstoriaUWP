using AndroidInteropLib.android.content;
using AndroidInteropLib.android.util;
using AndroidInteropLib.org.xmlpull.v1;

namespace AndroidInteropLib.android.widget
{
    public class CheckBox : CompoundButton
    {
        private Windows.UI.Xaml.Controls.CheckBox checkBox = new Windows.UI.Xaml.Controls.CheckBox();

        public CheckBox(Context context, AttributeSet attrs) : base(context, attrs) { }

        public override void CreateWinUI(params object[] obj)
        {
            // Windows.UI.Xaml.Controls.CheckBox inherits ContentControl, assign directly.
            checkBox.Checked += (s, e) => { mChecked = true; };
            checkBox.Unchecked += (s, e) => { mChecked = false; };
            WinUI = checkBox;

            if (obj != null && obj.Length > 1 && obj[1] is AttributeSet a)
            {
                string text = a.getAttributeValue(XmlPullParser.ANDROID_NAMESPACE, "text");
                if (text != null) checkBox.Content = text;

                string checked_ = a.getAttributeValue(XmlPullParser.ANDROID_NAMESPACE, "checked");
                if (checked_ != null) checkBox.IsChecked = checked_ == "true";
            }
        }

        public override void setText(string text)
        {
            checkBox.Content = text;
        }

        public override void setChecked(bool checked_)
        {
            base.setChecked(checked_);
            checkBox.IsChecked = checked_;
        }

        public override bool isChecked()
        {
            return checkBox.IsChecked == true;
        }
    }
}
