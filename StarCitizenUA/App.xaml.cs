using StarCitizenUA.Composition;
using System.Windows;

namespace StarCitizenUA
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            var compositionRoot = new AppCompositionRoot();
            var window = compositionRoot.CreateMainWindow();
            window.Show();
        }
    }

}
