using AndroidInteropLib.android.view;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace AndroidInteropLib.android.widget
{
    /// <summary>
    /// A view that shows items in a vertically scrolling two-level list with expandable groups.
    /// </summary>
    public class ExpandableListView : ListView
    {
        public ExpandableListView() : base() { }

        public void expandGroup(int groupPos) { }

        public void collapseGroup(int groupPos) { }

        public bool isGroupExpanded(int groupPos) { return false; }
    }
}
