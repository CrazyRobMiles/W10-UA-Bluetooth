using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;

// The Blank Page item template is documented at http://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x409

using WindowsBluetooth;

namespace PrinterDemo
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        public BluetoothManager manager = null;

        public MainPage()
        {
            this.InitializeComponent();
        }

        private void setupBluetooth()
        {
            if (manager != null)
                return;

            manager = new BluetoothManager();

            manager.StatusChangedNotification += new BluetoothManager.StatusChangedDelegate(statusChanged);

            manager.Initialise("PRINTER");
        }

        private async Task<bool> sendMessage(string message)
        {
            if (manager.Status == BluetoothManager.ManagerStatus.GotConnection)
            {
                return await manager.SendStringAsync(message);
            }
            return false;
        }

        private void Print_Button_Click(object sender, RoutedEventArgs e)
        {
            sendMessage(PrintTextBox.Text + "\n");
        }

        private void statusChanged(BluetoothManager.ManagerStatus status)
        {
            StatusTextBlock.Text = status.ToString();
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            setupBluetooth();
        }
    }
}
