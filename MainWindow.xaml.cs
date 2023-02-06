global using System;
global using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Threading.Tasks;

namespace BluetoothA2DPListener
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private Label serverConsole;
        private Label recieverConsole;

        private ReceiverTask _receiverTask;
        private ServerTask _serverTask;
        
        public MainWindow()
        {
            InitializeComponent();

            InitializeVariable();
        }

        private void InitializeVariable()
        {
            serverConsole = this.FindName("ServerConsole") as Label;
            recieverConsole = this.FindName("RecieverConsole") as Label;
        }


        private void OnClickServer(object sender, RoutedEventArgs e)
        {
            _serverTask = new ServerTask();
            _serverTask.InitializeRfcommServer(this);
        }

        private void OnClickReceiver(object sender, RoutedEventArgs e) {
            _receiverTask = new ReceiverTask();
            _receiverTask.ReceiveAdvertise(this);
        }

        internal async Task ServerOutput(string s)
        {
            await this.Dispatcher.InvokeAsync(() => {
                serverConsole.Content += s;
            });
        }

        internal async Task ReceiverOutput(string s)
        {
            await this.Dispatcher.InvokeAsync(() => {
                recieverConsole.Content += s;
            });
        }
    }
}
