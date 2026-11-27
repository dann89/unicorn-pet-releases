using System;
using System.Windows;
using System.Windows.Controls;

namespace UnicornPet.App
{
    public partial class BubbleControl : UserControl
    {
        public BubbleControl() { InitializeComponent(); }

        public void SetText(string text) { Message.Text = text; }

        /// <summary>Never taller than the given height (the text is trimmed with an ellipsis rather than growing off the screen).</summary>
        public void SetMaxHeight(double height) { Message.MaxHeight = Math.Max(24, height); }

        public void SetTail(double x, bool visible)
        {
            Tail.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            Tail.Margin = new Thickness(x - 9, -2, 0, 0);
        }
    }
}
