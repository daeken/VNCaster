using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.UI.Core;
using Windows.UI.Input;
using Windows.UI.Popups;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;

// The Blank Page item template is documented at http://go.microsoft.com/fwlink/?LinkId=234238

namespace VNCaster {
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class StreamPage : Page {
		VncClient client;
		InputMapper mapper;

		float ratio;

		public StreamPage() {
            this.InitializeComponent();
		}

		protected override void OnNavigatedTo(NavigationEventArgs ne) {
			base.OnNavigatedTo(ne);
			SystemNavigationManager.GetForCurrentView().AppViewBackButtonVisibility = AppViewBackButtonVisibility.Visible;
			var host = (Host)ne.Parameter;

			client = new VncClient(host.Hostname, host.Port, host.Password);
			mapper = new InputMapper(client, image);

			image.PointerMoved += (sender, e) => {
				HandlePointer(e.GetCurrentPoint(image));
				e.Handled = true;
			};
			image.PointerPressed += (sender, e) => {
				HandlePointer(e.GetCurrentPoint(image));
				e.Handled = true;
			};
			image.PointerReleased += (sender, e) => {
				HandlePointer(e.GetCurrentPoint(image));
				e.Handled = true;
			};
			image.PointerWheelChanged += (sender, e) => {
				HandlePointer(e.GetCurrentPoint(image));
				e.Handled = true;
			};
			
			var dispatcher = CoreWindow.GetForCurrentThread().Dispatcher;
			WriteableBitmap wb = null;

			client.Updated += async(sender, e) => {
				await dispatcher.RunAsync(CoreDispatcherPriority.High, new DispatchedHandler(() => {
					if(wb == null || wb.PixelWidth != e.fbw || wb.PixelHeight != e.fbh)
						wb = new WriteableBitmap(e.fbw, e.fbh);
					using(var stream = wb.PixelBuffer.AsStream())
						stream.Write(e.fb, 0, e.fb.Length);
					wb.Invalidate();
					image.Source = wb;
				}));
			};

			client.Resized += async (sender, e) => {
				ratio = (float)e.Width / e.Height;

				await dispatcher.RunAsync(CoreDispatcherPriority.High, new DispatchedHandler(() => {
					ResizeWindow();
				}));
			};
			var aview = ApplicationView.GetForCurrentView();
			aview.VisibleBoundsChanged += Aview_VisibleBoundsChanged;

			client.Disconnected += async (sender, error) => {
				await dispatcher.RunAsync(CoreDispatcherPriority.High, new DispatchedHandler(async() => {
					aview.VisibleBoundsChanged -= Aview_VisibleBoundsChanged;
					mapper.Disconnect();
					mapper = null;
					client = null;
					if(error) {
						var rootFrame = Window.Current.Content as Frame;
						rootFrame.GoBack();
						var dialog = new MessageDialog("You have been disconnected from the host");
						dialog.Commands.Add(new UICommand("Close") { Id = 0 });
						dialog.DefaultCommandIndex = 0;
						dialog.CancelCommandIndex = 0;

						await dialog.ShowAsync();
					}
				}));
			};

			client.Bailed += async (sender, message) => {
				client.Disconnect();
				await dispatcher.RunAsync(CoreDispatcherPriority.High, new DispatchedHandler(async () => {
					var rootFrame = Window.Current.Content as Frame;
					rootFrame.GoBack();
					var dialog = new MessageDialog(message, "You have been disconnected from the host");
					dialog.Commands.Add(new UICommand("Close") { Id = 0 });
					dialog.DefaultCommandIndex = 0;
					dialog.CancelCommandIndex = 0;

					await dialog.ShowAsync();
				}));
			};

			client.Connect();
		}

		bool awaitingResize = false;

		private async void Aview_VisibleBoundsChanged(ApplicationView sender, object args) {
			if(awaitingResize)
				return;
			awaitingResize = true;
			var dispatcher = CoreWindow.GetForCurrentThread().Dispatcher;
			await Task.Delay(50).ContinueWith(async _ => {
				await dispatcher.RunAsync(CoreDispatcherPriority.Normal, new DispatchedHandler(() => {
					awaitingResize = false;
					ResizeWindow();
				}));
			});
		}

		private void ResizeWindow() {
			var aview = ApplicationView.GetForCurrentView();
			var bounds = aview.VisibleBounds;
			var width = bounds.Width;
			var height = width / ratio;
			Debug.WriteLine("Currently {0}x{1}", (int)bounds.Width, (int)bounds.Height);
			Debug.WriteLine("Resizing ... {0}x{1}", (int)width, (int)height);
			aview.TryResizeView(new Size(width, height));
		}

		protected override void OnNavigatedFrom(NavigationEventArgs e) {
			base.OnNavigatedFrom(e);

			if(client != null)
				client.Disconnect();
		}

		int lowestTickValue = 0;

		void HandlePointer(PointerPoint point) {
			if(client == null)
				return;
			var wdelta = point.Properties.MouseWheelDelta;
			if(lowestTickValue == 0 || lowestTickValue > Math.Abs(wdelta))
				lowestTickValue = Math.Abs(wdelta);
			var ticks = wdelta == 0 ? 1 : (int)Math.Abs(Math.Ceiling((float)wdelta / lowestTickValue));
			var buttonState =
				(point.Properties.IsLeftButtonPressed ? 1 : 0) |
				(point.Properties.IsMiddleButtonPressed ? 2 : 0) |
				(point.Properties.IsRightButtonPressed ? 4 : 0);
			var wheelState =  
				(point.Properties.MouseWheelDelta > 0 ? 8 : 0) |
				(point.Properties.MouseWheelDelta < 0 ? 16 : 0);
			for(var i = 0; i < ticks; ++i) {
				client.SendPointer(
					(byte)(buttonState | wheelState),
					(ushort)(point.Position.X / image.ActualWidth * client.fbw),
					(ushort)(point.Position.Y / image.ActualHeight * client.fbh)
				);
				if(wheelState != 0)
					client.SendPointer(
						(byte)buttonState,
						(ushort)(point.Position.X / image.ActualWidth * client.fbw),
						(ushort)(point.Position.Y / image.ActualHeight * client.fbh)
					);
			}
		}
	}
}
