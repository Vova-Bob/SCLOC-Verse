using StarCitizenUA.Interfaces;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;


namespace StarCitizenUA.Controls
{
    public partial class HomeCanvas : Canvas
    {
        private double WidthRatio = 0.66 * 1.35;
        private double HeightRatio = 0.35;
        private DispatcherTimer? smoothScrollTimer;
        private double targetOffset;
        private double currentVelocity;

        public IToastService? ToastService { get; set; }
        public ILinkService? LinkService { get; set; }

        public Button UpdateCheckButtonControl => CheckUpdateButton;
        public TextBlock CurrentVersionTextControl => CurrentVersionTextBlock;
        public TextBlock AvailableVersionTextControl => AvailableVersionTextBlock;
        public TextBlock UpdateStatusTextControl => UpdateStatusTextBlock;
        public Storyboard HideUpdatePanelStoryboard => (Storyboard)UpdatePanel.Resources["HideUpdatePanelStoryboard"];

        public HomeCanvas()
        {
            InitializeComponent();
            Loaded += HomeCanvas_Loaded;
            SizeChanged += HomeCanvas_SizeChanged;
            this.PreviewMouseWheel += HomeCanvas_PreviewMouseWheel;
        }

        private void HomeCanvas_Loaded(object sender, RoutedEventArgs e)
        {
            UpdateCardHeights();
            CenterScrollContainer();
        }

        private void HomeCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateCardHeights();
            UpdateCardWidths();
            CenterScrollContainer();
        }

        private void HomeCanvas_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (!SliderScroll.IsMouseOver) return;
            e.Handled = true;

            double scrollStep = 100;
            double scrollDelta = (e.Delta > 0 ? -scrollStep : scrollStep);

            targetOffset = Math.Max(0, Math.Min(targetOffset + scrollDelta, SliderScroll.ScrollableWidth));

            if (smoothScrollTimer == null)
            {
                smoothScrollTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(16)
                };
                smoothScrollTimer.Tick += SmoothScrollTimer_Tick;
            }

            currentVelocity += (targetOffset - SliderScroll.HorizontalOffset) * 0.1;

            if (!smoothScrollTimer.IsEnabled)
                smoothScrollTimer.Start();
        }

        private void SmoothScrollTimer_Tick(object? sender, EventArgs e)
        {
            if (SliderScroll == null) return;

            double current = SliderScroll.HorizontalOffset;

            currentVelocity *= 0.85;

            double next = current + currentVelocity;

            if (Math.Abs(currentVelocity) < 0.3 && Math.Abs(next - targetOffset) < 0.3)
            {
                SliderScroll.ScrollToHorizontalOffset(targetOffset);
                currentVelocity = 0;
                smoothScrollTimer?.Stop();
                return;
            }

            next = Math.Max(0, Math.Min(next, SliderScroll.ScrollableWidth));
            SliderScroll.ScrollToHorizontalOffset(next);
        }

        private void UpdateCardHeights()
        {
            if (SliderPanel == null) return;

            double slideHeight = this.ActualHeight * HeightRatio;

            double minHeight = 150;
            double maxHeight = 300;
            slideHeight = Math.Max(minHeight, Math.Min(maxHeight, slideHeight));

            foreach (var child in SliderPanel.Children)
            {
                if (child is Border border)
                    border.Height = slideHeight;
            }
        }

        private void UpdateCardWidths()
        {
            if (SliderPanel == null || SliderPanel.Children.Count == 0) return;

            int visibleCards = 4;
            double totalMargin = 76 * 2 * visibleCards; // Margin.Left + Margin.Right ����� ������

            double cardWidth = (ScrollContainer.ActualWidth - totalMargin) / visibleCards;

            foreach (var child in SliderPanel.Children)
            {
                if (child is Border border)
                {
                    border.Width = cardWidth;
                }
            }
        }

        private void CenterScrollContainer()
        {
            if (ScrollContainer != null)
            {
                ScrollContainer.Width = this.ActualWidth * WidthRatio;
                Canvas.SetLeft(ScrollContainer, (this.ActualWidth - ScrollContainer.Width) / 2);
                ScrollContainer.Clip = new RectangleGeometry(new Rect(0, 0, ScrollContainer.Width, ScrollContainer.ActualHeight));
            }
        }

        public void SetScrollWidthRatio(double ratio)
        {
            WidthRatio = ratio;
            CenterScrollContainer();
        }

        private async void OpenUrl(string url)
        {
            if (LinkService != null)
            {
                await LinkService.OpenLinkAsync(url);
            }
            else
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = url,
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    if (ToastService != null)
                        await ToastService.ShowToastAsync("�� ������� ������� ���������: " + ex.Message);
                }
            }
        }

        private void YouTubeCard_Click(object sender, MouseButtonEventArgs e)
        {
            OpenUrl("https://www.youtube.com/watch?v=4AwL8TKXcTU");
        }

        private void Card2_Click(object sender, MouseButtonEventArgs e)
        {
            OpenUrl("https://robertsspaceindustries.com/en/orgs/UKR");
        }

        private void Card3_Click(object sender, MouseButtonEventArgs e)
        {
            OpenUrl("https://discord.gg/y2c7M9cgbk");
        }

        private void Card4_Click(object sender, MouseButtonEventArgs e)
        {
            OpenUrl("https://scloc.pp.ua/");
        }

        private void Card5_Click(object sender, MouseButtonEventArgs e)
        {
            OpenUrl("https://www.erkul.games/live/calculator");
        }

        private void Card6_Click(object sender, MouseButtonEventArgs e)
        {
            OpenUrl("https://example.com/card6");// ������� �� �������� ���������
        }

        private void Card7_Click(object sender, MouseButtonEventArgs e)
        {
            OpenUrl("https://example.com/card7");// ������� �� �������� ���������
        }

        private void Card8_Click(object sender, MouseButtonEventArgs e)
        {
            OpenUrl("https://example.com/card8");// ������� �� �������� ���������
        }
    }
}