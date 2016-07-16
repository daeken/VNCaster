using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Runtime.Serialization;
using System.Xml.Serialization;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.Storage;
using Windows.UI.Core;
using Windows.UI.Popups;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;

// The Blank Page item template is documented at http://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x409

namespace VNCaster
{
	[DataContract]
	public class Host {
		[DataMember]
		public string Alias;
		[DataMember]
		public string Hostname;
		[DataMember]
		public int Port;
		[DataMember]
		public string Password;

		public string _Alias {
			get {
				if(Alias == null)
					return string.Format("{0}:{1}", Hostname, Port);
				return Alias;
			}
		}

		public Host() {
		}

		public Host(string alias, string name, int p, string pass) {
			Alias = alias;
			Hostname = name;
			Port = p;
			Password = pass;
		}
	}
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainPage : Page
    {
		List<Host> hosts;
		Host editingHost;
        public MainPage()
        {
            this.InitializeComponent();

			LoadHosts();
			RefreshHosts();
			
			add_hostname.TextChanged += (sender, e) => {
				add_save.IsEnabled = add_hostname.Text != "" && add_port.Text != "";
			};
			add_port.TextChanged += (sender, e) => {
				add_save.IsEnabled = add_hostname.Text != "" && add_port.Text != "";
			};
		}

		private void LoadHosts() {
			var settings = ApplicationData.Current.LocalSettings;
			var ser = new XmlSerializer(typeof(List<Host>));
			if(settings.Values.ContainsKey("hosts"))
				using(var reader = new StringReader((string)settings.Values["hosts"]))
					hosts = (List<Host>)ser.Deserialize(reader);
			else
				hosts = new List<Host>();
		}
		private void SaveHosts() {
			var settings = ApplicationData.Current.LocalSettings;
			var ser = new XmlSerializer(hosts.GetType());
			using(var writer = new StringWriter()) {
				ser.Serialize(writer, hosts);
				settings.Values["hosts"] = writer.ToString();
			}
			RefreshHosts();
		}
		private void RefreshHosts() {
			HostGrid.ItemsSource = null;
			HostGrid.ItemsSource = hosts;
			if(hosts.Count == 0) {
				DefaultText.Visibility = Visibility.Visible;
				HostGrid.Visibility = Visibility.Collapsed;
			} else {
				HostGrid.Visibility = Visibility.Visible;
				DefaultText.Visibility = Visibility.Collapsed;
			}
		}

		private void addButton_Click(object sender, RoutedEventArgs e) {
			addHeader.Text = "Add Host";
			editingHost = null;
			add_alias.Text = "";
			add_hostname.Text = "";
			add_port.Text = "5900";
			add_password.Password = "";
			AddPane.IsPaneOpen = !AddPane.IsPaneOpen;
		}

		protected override void OnNavigatedTo(NavigationEventArgs e) {
			SystemNavigationManager.GetForCurrentView().AppViewBackButtonVisibility = AppViewBackButtonVisibility.Collapsed;
		}

		private void add_save_Click(object sender, RoutedEventArgs e) {
			var settings = ApplicationData.Current.LocalSettings;
			var alias = add_alias.Text != "" ? add_alias.Text : string.Format("{0}:{1}", add_hostname.Text, add_port.Text);
			var hostname = add_hostname.Text;
			var port = int.Parse(add_port.Text);
			var password = add_password.Password != "" ? add_password.Password : null;
			if(editingHost != null) {
				editingHost.Alias = alias;
				editingHost.Hostname = hostname;
				editingHost.Port = port;
				editingHost.Password = password;
				editingHost = null;
			} else
				hosts.Add(new Host(alias, hostname, port, password));
			SaveHosts();
			AddPane.IsPaneOpen = false;
		}

		private void StackPanel_RightTapped(object sender, RightTappedRoutedEventArgs e) {
			FlyoutBase.ShowAttachedFlyout(sender as FrameworkElement);
		}

		private void Edit_Click(object sender, RoutedEventArgs e) {
			addHeader.Text = "Edit Host";
			AddPane.IsPaneOpen = true;
			editingHost = (Host) ((sender as FrameworkElement).DataContext);
			add_alias.Text = editingHost.Alias == null ? "" : editingHost.Alias;
			add_hostname.Text = editingHost.Hostname;
			add_port.Text = editingHost.Port.ToString();
			add_password.Password = editingHost.Password == null ? "" : editingHost.Password;
		}

		private async void Delete_Click(object sender, RoutedEventArgs e) {
			var host = (Host)((sender as FrameworkElement).DataContext);
			var dialog = new MessageDialog("Are you sure you want to delete this host?", host._Alias);
			dialog.Commands.Add(new UICommand("Yes") { Id = 0 });
			dialog.Commands.Add(new UICommand("No") { Id = 1 });
			dialog.DefaultCommandIndex = 1;
			dialog.CancelCommandIndex = 1;
			if((int) (await dialog.ShowAsync()).Id == 0) {
				hosts.Remove(host);
				SaveHosts();
			}
			
		}

		private void Host_Click(object sender, RoutedEventArgs e) {
			var host = (Host)((sender as FrameworkElement).DataContext);
			Frame.Navigate(typeof(StreamPage), host);
		}
	}
}
