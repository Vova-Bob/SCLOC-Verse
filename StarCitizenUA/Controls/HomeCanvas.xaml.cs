using StarCitizenUA.Interfaces;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace StarCitizenUA.Controls
{
    public partial class HomeCanvas : Canvas
    {
        private double WidthRatio = 0.66 * 1.35;
        private double HeightRatio = 0.35;

        public IToastService? ToastService { get; set; }
        public ILinkService? LinkService { get; set; }

        public HomeCanvas()
        {
            InitializeComponent();
            Loaded += HomeCanvas_Loaded;
            SizeChanged += HomeCanvas_SizeChanged;
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

            int visibleCards = 5;
            double totalMargin = 20 * 2 * visibleCards; // Margin.Left + Margin.Right кожної картки

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
                        await ToastService.ShowToastAsync("Не вдалося відкрити посилання: " + ex.Message);
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
            OpenUrl("https://example.com/card6");// Замінити на фактичне посилання
        }

        private void Card7_Click(object sender, MouseButtonEventArgs e)
        {
            OpenUrl("https://example.com/card7");// Замінити на фактичне посилання
        }

        private void Card8_Click(object sender, MouseButtonEventArgs e)
        {
            OpenUrl("https://example.com/card8");// Замінити на фактичне посилання
        }
    }
}