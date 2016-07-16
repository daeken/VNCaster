using System;
using System.Collections.Generic;
using System.Diagnostics;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.System;

namespace VNCaster {
	class InputMapper {
		VncClient client;
		Dictionary<VirtualKey, Tuple<uint, uint>> charMap;
		Dictionary<VirtualKey, uint> specialMap;

		public InputMapper(VncClient client, Image image) {
			this.client = client;
			Window.Current.CoreWindow.KeyDown += CoreWindow_KeyDown;
			Window.Current.CoreWindow.KeyUp += CoreWindow_KeyUp;
			Window.Current.CoreWindow.Dispatcher.AcceleratorKeyActivated += Dispatcher_AcceleratorKeyActivated;

			charMap = new Dictionary<VirtualKey, Tuple<uint, uint>>();
			for(var i = 0; i < 26; ++i)
				AddChar(VirtualKey.A + i, 'A' + i, 'a' + i);
			string symbols = ")!@#$%^&*(";
			for(var i = 0; i < 10; ++i)
				AddChar(VirtualKey.Number0 + i, symbols[i], '0' + i);
			AddChar(219, '{', '[');
			AddChar(221, '}', ']');
			AddChar(186, ':', ';');
			AddChar(187, '+', '=');
			AddChar(188, '<', ',');
			AddChar(189, '_', '-');
			AddChar(190, '>', '.');
			AddChar(191, '?', '/');
			AddChar(192, '~', '`');
			AddChar(220, '|', '\\');
			AddChar(222, '"', '\'');

			specialMap = new Dictionary<VirtualKey, uint>();
			AddSpecial(VirtualKey.Enter, 0xFF0D);
			AddSpecial(VirtualKey.Space, ' ');
			AddSpecial(VirtualKey.Back, 0xFF08);
			AddSpecial(VirtualKey.Tab, 0xFF09);
			AddSpecial(VirtualKey.Escape, 0xFF1B);

			AddSpecial(VirtualKey.Left, 0xFF51);
			AddSpecial(VirtualKey.Up, 0xFF52);
			AddSpecial(VirtualKey.Right, 0xFF53);
			AddSpecial(VirtualKey.Down, 0xFF54);

			AddSpecial(VirtualKey.PageUp, 0xFF55);
			AddSpecial(VirtualKey.PageDown, 0xFF56);
			AddSpecial(VirtualKey.End, 0xFF57);
			AddSpecial(VirtualKey.Home, 0xFF58);

			AddSpecial(VirtualKey.Shift, 0xFFE1);
			AddSpecial(VirtualKey.LeftShift, 0xFFE1);
			AddSpecial(VirtualKey.RightShift, 0xFFE2);
			AddSpecial(VirtualKey.Control, 0xFFE3);
			AddSpecial(VirtualKey.LeftControl, 0xFFE3);
			AddSpecial(VirtualKey.RightControl, 0xFFE4);
			AddSpecial(VirtualKey.Menu, 0xFFE9);
			AddSpecial(VirtualKey.LeftMenu, 0xFFE9);
			AddSpecial(VirtualKey.RightMenu, 0xFFEA);
			AddSpecial(VirtualKey.LeftWindows, 0xFFE7);
			AddSpecial(VirtualKey.RightWindows, 0xFFE8);
		}

		public void Disconnect() {
			Window.Current.CoreWindow.KeyDown -= CoreWindow_KeyDown;
			Window.Current.CoreWindow.KeyUp -= CoreWindow_KeyUp;
			Window.Current.CoreWindow.Dispatcher.AcceleratorKeyActivated -= Dispatcher_AcceleratorKeyActivated;
		}

		private void Dispatcher_AcceleratorKeyActivated(CoreDispatcher sender, AcceleratorKeyEventArgs e) {
			Handle(!e.KeyStatus.IsKeyReleased, e.VirtualKey, e.KeyStatus);
			e.Handled = true;
		}

		private void CoreWindow_KeyUp(CoreWindow sender, KeyEventArgs e) {
			Handle(false, e.VirtualKey, e.KeyStatus);
			e.Handled = true;
		}

		private void CoreWindow_KeyDown(CoreWindow sender, KeyEventArgs e) {
			Handle(true, e.VirtualKey, e.KeyStatus);
			e.Handled = true;
		}

		private void AddChar(VirtualKey key, int upper, int lower) {
			charMap.Add(key, new Tuple<uint, uint>((uint) upper, (uint) lower));
		}
		private void AddChar(int key, int upper, int lower) {
			AddChar((VirtualKey)key, upper, lower);
		}

		private void AddSpecial(VirtualKey key, int send) {
			specialMap.Add(key, (uint)send);
		}

		bool lshift = false, rshift = false, lcontrol = false, rcontrol = false,
			lalt = false, ralt = false, lwin = false, rwin = false;

		bool shift { get { return lshift || rshift; } }
		bool control { get { return lcontrol || rcontrol; } }
		bool alt { get { return lalt || ralt; } }
		bool win { get { return lwin || rwin; } }

		private void Handle(bool down, VirtualKey key, CorePhysicalKeyStatus status) {
			switch(key) {
				case VirtualKey.LeftShift: lshift = down; break;
				case VirtualKey.RightShift: rshift = down; break;
				case VirtualKey.Shift: lshift = rshift = down; break;

				case VirtualKey.LeftControl: lcontrol = down; break;
				case VirtualKey.RightControl: rcontrol = down; break;
				case VirtualKey.Control: lcontrol = rcontrol = down; break;

				case VirtualKey.LeftMenu: lalt = down; break;
				case VirtualKey.RightMenu: ralt = down; break;
				case VirtualKey.Menu: lalt = ralt = down; break;

				case VirtualKey.LeftWindows: lwin = down; break;
				case VirtualKey.RightWindows: rwin = down; break;
			}

			if(charMap.ContainsKey(key)) {
				var elem = charMap[key];
				InjectNS(shift ? elem.Item1 : elem.Item2, down);
			} else if(specialMap.ContainsKey(key)) {
				Inject(specialMap[key], down);
			} else {
				Debug.WriteLine("Unknown key: {0}", key);
			}
		}

		private void Inject(uint key, bool down) {
			Debug.WriteLine("{0:X}", key);
			client.SendKey(key, down);
		}

		private void InjectNS(uint key, bool down) {
			if(shift)
				Inject(0xFFE1, false);
			Inject(key, down);
			if(shift)
				Inject(0xFFE1, true);
		}
	}
}
