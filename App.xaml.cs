using System.Configuration;
using System.Data;
using System.Windows;

namespace BluesBar
{
    public partial class App : Application
    {
        public App()
        {
            ShutdownMode = ShutdownMode.OnMainWindowClose; // BluesBar closes => app closes
        }
    }
}
