using System;
using System.Windows;
using System.Windows.Input;
using UnicornPet.Core;

namespace UnicornPet.App
{
    public partial class BubbleWindow : Window
    {
        public event Action Dismissed;

        public BubbleWindow()
        {
            InitializeComponent();
            MouseLeftButtonUp += (s, e) => Dismissed?.Invoke();
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            NoActivate.Apply(this);
        }

        public Size Measure(string text, double maxHeight = double.PositiveInfinity)
        {
            Bubble.SetText(text);
            if (!double.IsInfinity(maxHeight)) Bubble.SetMaxHeight(maxHeight);
            Bubble.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            return Bubble.DesiredSize;
        }

        public void ShowAt(string text, Rect pet, Rect workArea)
        {
            // the bubble (text, padding, border, tail) must fit the work area: leave room for the chrome around the text
            var size = Measure(text, workArea.Height - 60);
            Reposition(size, pet, workArea);
            if (!IsVisible) Show();
        }

        public void Reposition(Size size, Rect pet, Rect wa)
        {
            var p = BubbleLayout.Compute(size.Width, size.Height, pet.Left, pet.Top, pet.Width, pet.Height, wa.Left, wa.Top, wa.Width, wa.Height);
            Bubble.SetTail(p.TailX, !p.Beside);
            Left = p.Left;
            Top = p.Top;
        }

        public Size LastSize => Bubble.DesiredSize;
    }
}
