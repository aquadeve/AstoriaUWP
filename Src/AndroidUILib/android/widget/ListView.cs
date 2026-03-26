using AndroidInteropLib.android.view;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace AndroidInteropLib.android.widget
{
    public class ListView : ViewGroup
    {
        // Use field initializer so it is ready before base constructor calls CreateWinUI
        private Windows.UI.Xaml.Controls.ListView listView
            = new Windows.UI.Xaml.Controls.ListView();

        public ListView() : base(null, null) { }

        public override void CreateWinUI(params object[] obj)
        {
            listView.HorizontalAlignment = HorizontalAlignment.Stretch;
            listView.VerticalAlignment = VerticalAlignment.Stretch;
            WinUI.Content = listView;
        }

        public void setAdapter(object adapter) { }

        public void addView(object child)
        {
            if (child is View view)
                listView.Items.Add(view.WinUI);
            else if (child is UIElement element)
                listView.Items.Add(element);
        }

        public override void addView(View view, LayoutParams param)
        {
            listView.Items.Add(view.WinUI);
        }

        public override void addView(View view)
        {
            listView.Items.Add(view.WinUI);
        }

        public override void removeView(View view)
        {
            listView.Items.Remove(view.WinUI);
        }
    }
}