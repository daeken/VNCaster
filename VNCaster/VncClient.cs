using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using System.IO.Compression;
using Windows.Networking.Sockets;
using Windows.Security.Cryptography;
using Windows.Security.Cryptography.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media.Imaging;
using System.Text;

namespace VNCaster {
	class DisconnectException : Exception {
	}

	class BufReader {
		public BinaryReader br;

		public BufReader Init(Stream stream) {
			br = new BinaryReader(stream);
			return this;
		}

		public byte U8() {
			try {
				return br.ReadByte();
			} catch(Exception) {
				throw new DisconnectException();
			}
		}

		public ushort U16() {
			try {
				var x = br.ReadBytes(2);
				Array.Reverse(x);
				return BitConverter.ToUInt16(x, 0);
			} catch(Exception) {
				throw new DisconnectException();
			}
		}

		public uint U24() {
			return (uint)((U8() << 16) | (U8() << 8) | U8());
		}

		public uint U32() {
			try {
				var x = br.ReadBytes(4);
				Array.Reverse(x);
				return BitConverter.ToUInt32(x, 0);
			} catch(Exception) {
				throw new DisconnectException();
			}
		}

		public int I32() {
			try {
				var x = br.ReadBytes(4);
				Array.Reverse(x);
				return BitConverter.ToInt32(x, 0);
			} catch(Exception) {
				throw new DisconnectException();
			}
		}
	}
	class BitReader {
		byte[] data;
		int bits, mask;
		int byteoff, bitoff;
		public BitReader(byte[] data, int bits) {
			this.data = data;
			this.bits = bits;
			mask = (1 << bits) - 1;
		}
		public void PadByte() {
			if(bitoff != 0) {
				byteoff++;
				bitoff = 0;
			}
		}
		public int Next() {
			var val = (data[byteoff] >> (8 - bitoff - bits)) & mask;
			bitoff += bits;
			if(bitoff == 8) {
				bitoff = 0;
				byteoff++;
			}
			return val;
		}
	}

	struct ResizeEventArgs {
		public int Width, Height;

		public ResizeEventArgs(int Width, int Height) {
			this.Width = Width;
			this.Height = Height;
		}
	}

	struct FBUpdateArgs {
		public byte[] fb;
		public int fbw, fbh;

		public FBUpdateArgs(byte[] fb, int fbw, int fbh) {
			this.fb = fb;
			this.fbw = fbw;
			this.fbh = fbh;
		}
	}

	class VncClient : BufReader {
		string hostname;
		int port;
		string password;
		StreamSocket socket;
		byte[] fb;
		public int fbw, fbh;
		bool dirty = false;

		bool alive = true;

		Stream st;
		BinaryWriter bw;

		LazyMemoryStream lms;
		DeflateStream inflater;
		BufReader zr = null;

		public event EventHandler<bool> Disconnected;
		public event EventHandler<ResizeEventArgs> Resized;
		public event EventHandler<string> Bailed;
		public event EventHandler<FBUpdateArgs> Updated;

		void Bail(string message, params object[] list) {
			if(list.Length > 0) {
				Bail(String.Format(message, list));
				return;
			}

			Bailed(this, message);
		}

		public VncClient(string hostname, int port, string password) {
			this.hostname = hostname;
			this.port = port;
			this.password = password;
		}

		~VncClient() {
			Disconnect();
		}

		public void Disconnect(bool error=false) {
			if(!alive)
				return;
			alive = false;
			Disconnected(this, error);
		}

		public async void Connect() {
			socket = new StreamSocket();
			var host = new Windows.Networking.HostName(hostname);
			await socket.ConnectAsync(host, port.ToString());

			st = socket.InputStream.AsStreamForRead();
			var reader = new StreamReader(st);
			var resp = await reader.ReadLineAsync();

			var ost = socket.OutputStream.AsStreamForWrite();
			var writer = new StreamWriter(ost);
			writer.AutoFlush = true;

			br = new BinaryReader(st);
			bw = new BinaryWriter(ost);

			Debug.Assert(resp.StartsWith("RFB "));
			var vers = resp.Substring(4, 7);
			int major = Int32.Parse(vers.Substring(0, 3)), minor = Int32.Parse(vers.Substring(4, 3));

			Debug.Assert(major == 3 || major == 5);
			Debug.Assert(minor == 8 || minor == 889 || minor == 0);

			await writer.WriteAsync("RFB 003.008\n");

			var num = U8();
			if(num == 0) {
				var len = I32();
				Debug.WriteLine(String.Format("Length {0}", len));
				var bytes = br.ReadBytes(len);
				Debug.WriteLine(System.Text.Encoding.UTF8.GetString(bytes));
				return;
			}
			var sectypes = new int[num];
			for(var i = 0; i < num; ++i) {
				sectypes[i] = U8();
				Debug.WriteLine(sectypes[i]);
			}

			if(sectypes.Contains(1)) {
				Debug.WriteLine("No auth required");
				U8(1);
			} else if(sectypes.Contains(2)) {
				Debug.WriteLine("VNC auth required");
				if(password == null || password == "") {
					Bail("VNC authorization required");
					return;
				}
				U8(2);
				var chal = br.ReadBytes(16);
				var alg = SymmetricKeyAlgorithmProvider.OpenAlgorithm(SymmetricAlgorithmNames.DesEcb);
				var pwd = GenerateVncKey();
				var key = alg.CreateSymmetricKey(CryptographicBuffer.CreateFromByteArray(pwd));
				var enc = CryptographicEngine.Encrypt(key, CryptographicBuffer.CreateFromByteArray(chal), null);
				bw.Write(enc.ToArray());
				bw.Flush();
			} else {
				Debug.Write("Unknown auth methods: ");
				for(var i = 0; i < num; ++i)
					Debug.Write(String.Format("{0} ", sectypes[i]));
				Debug.WriteLine("");
				Bail("Unknown VNC authorization required");
				return;
			}

			var secresult = U32();
			if(secresult != 0) {
				Bail("Password incorrect");
				return;
			}

			Init();

			//RequestContinuousUpdates();

			var timer = new DispatcherTimer();
			timer.Interval = new TimeSpan(0, 0, 0, 0, (int) (1000.0 / 60));
			timer.Tick += (sender, e) => {
				try {
					if(!alive) {
						timer.Stop();
						RequestUpdate(0, 0, 1, 1, false); // Force a new update to end the connection
						return;
					}
					RequestUpdate(0, 0, fbw, fbh, true);
					lock(fb) {
						if(dirty) {
							Updated(this, new FBUpdateArgs(fb, fbw, fbh));
							dirty = false;
						}
					}
				} catch(DisconnectException) {
					if(alive)
						Disconnect(true);
				}
			};
			timer.Start();

			await Task.Factory.StartNew(() => {
				while(alive) {
					try {
						Read();
					} catch(DisconnectException) {
						if(alive)
							Disconnect(true);
					}
				}
			});
		}

		byte[] GenerateVncKey() {
			var pwd = (password + "\0\0\0\0\0\0\0\0").Substring(0, 8);
			var bytes = System.Text.Encoding.ASCII.GetBytes(pwd);
			var key = new byte[8];
			for(var i = 0; i < 8; ++i) {
				var inp = bytes[i];
				key[i] = 0;
				for(var j = 0; j < 8; ++j)
					key[i] |= (byte)(((inp >> j) & 1) << (7 - j));
			}
			return key;
		}

		void InitFB(int width, int height) {
			fb = new byte[width * height * 4];
			fbw = width;
			fbh = height;
			Resized(this, new ResizeEventArgs(fbw, fbh));
		}

		void Init() {
			U8(1); // Share desktop

			InitFB(U16(), U16());
			
			br.ReadBytes(16); // Pixel format. Don't care.

			var namelen = I32();
			var name = System.Text.Encoding.UTF8.GetString(br.ReadBytes(namelen));

			U32(0); // SetPixelFormat
			U8(32); // Bits per pixel
			U8(24); // Depth
			U8(0); // Big endian
			U8(1); // True color
			// RGB max
			U16(255);
			U16(255);
			U16(255);
			// RGB shift
			U8(16);
			U8(8);
			U8(0);
			U8(0); U8(0); U8(0); // Padding

			// TightVNC supports 16, 7, 5
			// RealVNC supports 16
			// Apple supports 16
			SetEncodings(new [] {1, 16, 5, -223});

			RequestUpdate(0, 0, fbw, fbh, false);
		}

		void SetEncodings(int[] encodings) {
			U8(2);
			U8(0);
			U16((ushort) encodings.Length);

			for(var i = 0; i < encodings.Length; ++i)
				I32(encodings[i]);
		}

		void RequestUpdate(int x, int y, int width, int height, bool incremental) {
			lock(fb) {
				U8(3);
				U8((byte)(incremental ? 1 : 0));
				U16((ushort)x);
				U16((ushort)y);
				U16((ushort)width);
				U16((ushort)height);
			}
		}

		void RequestContinuousUpdates() {
			U8(150);
			U8(1);
			U16(0);
			U16(0);
			U16((ushort) fbw);
			U16((ushort) fbh);
		}

		public void SendKey(uint key, bool down) {
			U8(4);
			U8((byte) (down ? 1 : 0));
			U16(0);
			U32(key);
		}

		public void SendPointer(byte buttons, ushort x, ushort y) {
			U8(5);
			U8(buttons);
			U16(x);
			U16(y);
		}

		void Read() {
			var msgtype = U8();
			switch(msgtype) {
				case 0:
					FramebufferUpdate();
					break;
				case 2:
					// Bell
					break;
				case 3:
					ServerCutText();
					break;
				default:
					Debug.WriteLine(string.Format("Unknown message type from server: {0}", msgtype));
					Bail("Unknown message from server");
					break;
			}
		}

		void ServerCutText() {
			U8(); U8(); U8(); // Padding
			var text = br.ReadBytes(I32());
			Debug.WriteLine(string.Format("Got cut buffer: {0}", Encoding.UTF8.GetString(text)));
		}

		void FramebufferUpdate() {
			//Debug.WriteLine("FramebufferUpdate");
			U8(); // Padding
			lock(fb) {
				var numrects = U16();
				for(var i = 0; i < numrects; ++i) {
					ushort x = U16(), y = U16(), width = U16(), height = U16();
					var enctype = I32();
					switch(enctype) {
						case 0:
							DecodeRaw(x, y, width, height);
							break;
						case 1:
							DecodeCopyRect(x, y, width, height);
							break;
						case 5:
							DecodeHextile(x, y, width, height);
							break;
						case 16:
							DecodeZRLE(x, y, width, height);
							break;
						case -223:
							InitFB(width, height);
							RequestUpdate(0, 0, width, height, false);
							break;
						default:
							Debug.WriteLine(string.Format("Unknown encoding type from server: {0}", enctype));
							Bail("Unknown rectangle encoding from server");
							break;
					}
				}
			}
		}

		void DecodeRaw(int x, int y, int width, int height) {
			var pixels = br.ReadBytes(width * height * 4);
			CopyToImage(pixels, x, y, width, height, 4);
		}

		void DecodeCopyRect(int x, int y, int width, int height) {
			int srx = U16(), sry = U16();
			Debug.WriteLine("Copyrect");

			var rect = new byte[width * height * 4];
			for(var i = 0; i < height; ++i) {
				var soff = (sry + i) * fbw * 4 + srx * 4;
				Array.Copy(fb, soff, rect, i * width * 4, width * 4);
			}
			for(var i = 0; i < height; ++i) {
				var doff = (y + i) * fbw * 4 + x * 4;
				Array.Copy(rect, i * width * 4, fb, doff, width * 4);
			}
		}

		void DecodeHextile(int x, int y, int width, int height) {
			uint bg = 0, fg = 0;
			for(var cy = 0; cy < height; cy += 16) {
				var th = (cy + 16) <= height ? 16 : height - cy;
				for(var cx = 0; cx < width; cx += 16) {
					var tw = (cx + 16) <= width ? 16 : width - cx;

					var flags = U8();
					if((flags & 1) == 1) { // Raw
						var tilepixels = br.ReadBytes(tw * th * 4);
						CopyToImage(tilepixels, x + cx, y + cy, tw, th, 4);
						continue;
					}

					var hascolor = (flags & 16) == 16;
					if((flags & 2) == 2) // Background specified
						bg = U32();
					if((flags & 4) == 4) { // Foreground specified
						fg = U32();
						Debug.Assert(!hascolor); // No SubrectsColoured
					}
					var subrects = 0;
					if((flags & 8) == 8) // Any subrects
						subrects = U8();
					var tile = new uint[16*16];
					for(var fill = 0; fill < 16*16; ++fill)
						tile[fill] = bg;
					for(var j = 0; j < subrects; ++j) {
						var color = hascolor ? U32() : fg;
						var sp = U8();
						var ss = U8();
						int sx = sp >> 4, sy = sp & 0xF;
						int sw = (ss >> 4) + 1, sh = (ss & 0xF) + 1;

						for(var sj = 0; sj < sh; ++sj) {
							for(var si = 0; si < sw; ++si) {
								tile[(sy + sj) * 16 + (sx + si)] = color;
							}
						}
					}

					CopyToImage(TileToPixels(tile, tw, th), x + cx, y + cy, tw, th, 4);
				}
			}
		}

		void DecodeZRLE(int x, int y, int width, int height) {
			var zdata = br.ReadBytes(I32());
			if(zr == null) {
				lms = new LazyMemoryStream();
				lms.Load(zdata);
				var dump = new byte[2];
				lms.Read(dump, 0, 2);
				inflater = new DeflateStream(lms, CompressionMode.Decompress);
				zr = new BufReader().Init(inflater);
			} else {
				lms.Load(zdata);
			}

			for(var cy = 0; cy < height; cy += 64) {
				var th = (cy + 64) <= height ? 64 : height - cy;
				for(var cx = 0; cx < width; cx += 64) {
					var tw = (cx + 64) <= width ? 64 : width - cx;

					var subenc = zr.U8();
					var pixels = new byte[tw * th * 3];
					if(subenc == 0) { // Raw
						pixels = zr.br.ReadBytes(tw * th * 3);
					} else if(subenc == 1) { // Solid
						byte b = zr.U8(), g = zr.U8(), r = zr.U8();
						for(var i = 0; i < tw * th * 3; i += 3) {
							pixels[i] = b;
							pixels[i+1] = g;
							pixels[i+2] = r;
						}
					} else if(subenc == 128) // Plain RLE
						HandlePlainRLE(pixels, tw, th);
					else if(subenc >= 130)
						HandlePaletteRLE(pixels, subenc - 128, tw, th);
					else if(subenc >= 2 && subenc <= 16)
						HandlePalette(pixels, subenc, tw, th);
					CopyToImage(pixels, x + cx, y + cy, tw, th, 3);
				}
			}
		}

		void HandlePlainRLE(byte[] pixels, int tw, int th) {
			var off = 0;
			while(off < tw * th * 3) {
				byte b = zr.U8(), g = zr.U8(), r = zr.U8();
				uint len = 1;
				byte last;
				do {
					len += (last = zr.U8());
				} while(last == 255);
				for(var i = 0; i < len; ++i) {
					pixels[off++] = b;
					pixels[off++] = g;
					pixels[off++] = r;
				}
			}
		}

		void HandlePalette(byte[] pixels, int paletteSize, int tw, int th) {
			var palette = new uint[paletteSize];
			for(var i = 0; i < paletteSize; ++i)
				palette[i] = zr.U24();

			var bitsize = paletteSize == 2 ? 1 : (paletteSize <= 4 ? 2 : 4);
			int datasize;
			if(bitsize == 1)
				datasize = (tw + 7) / 8 * th;
			else if(bitsize == 2)
				datasize = (tw + 3) / 4 * th;
			else
				datasize = (tw + 1) / 2 * th;
			var data = zr.br.ReadBytes(datasize);
			var bs = new BitReader(data, bitsize);
			for(var y = 0; y < th; ++y) {
				for(var x = 0; x < tw; ++x) {
					var off = ((y * tw) + x) * 3;
					var color = palette[bs.Next()];
					byte r = (byte)(color & 0xFF), g = (byte)((color >> 8) & 0xFF), b = (byte)((color >> 16) & 0xFF);
					pixels[off++] = b;
					pixels[off++] = g;
					pixels[off++] = r;
				}
				bs.PadByte();
			}
		}

		void HandlePaletteRLE(byte[] pixels, int paletteSize, int tw, int th) {
			var palette = new uint[paletteSize];
			for(var i = 0; i < paletteSize; ++i)
				palette[i] = zr.U24();

			var off = 0;
			while(off < tw * th * 3) {
				var index = zr.U8();
				if((index & 0x80) == 0x80) {
					var color = palette[index - 128];
					uint len = 1;
					byte last;
					do {
						len += (last = zr.U8());
					} while(last == 255);
					for(var i = 0; i < len; ++i) {
						pixels[off++] = (byte)((color >> 16) & 0xFF);
						pixels[off++] = (byte)((color >> 8) & 0xFF);
						pixels[off++] = (byte)(color & 0xFF);
					}
				} else {
					var color = palette[index];
					pixels[off++] = (byte)((color >> 16) & 0xFF);
					pixels[off++] = (byte)((color >> 8) & 0xFF);
					pixels[off++] = (byte)(color & 0xFF);
				}
			}
		}

		byte[] TileToPixels(uint[] tile, int tw, int th) {
			var pixels = new byte[tw * th * 4];
			var off = 0;
			for(var y = 0; y < th; ++y) {
				for(var x = 0; x < tw; ++x) {
					var val = tile[y * 16 + x];
					pixels[off++] = (byte)((val >> 24) & 0xFF);
					pixels[off++] = (byte)((val >> 16) & 0xFF);
					pixels[off++] = (byte)((val >> 8) & 0xFF);
					pixels[off++] = (byte)(val & 0xFF);
				}
			}
			return pixels;
		}

		void CopyToImage(byte[] pixels, int x, int y, int w, int h, int psize) {
			for(var i = 0; i < h; ++i) {
				var offset = fbw * 4 * (i + y);
				var poff = i * w * psize;
				for(var j = 0; j < w; ++j) {
					fb[offset + (j+x) * 4 + 0] = pixels[poff + j * psize + 0];
					fb[offset + (j+x) * 4 + 1] = pixels[poff + j * psize + 1];
					fb[offset + (j+x) * 4 + 2] = pixels[poff + j * psize + 2];
					fb[offset + (j+x) * 4 + 3] = 0xFF;
				}
			}
			if(w != 0 && h != 0)
				dirty = true;
		}

		void U8(byte x) {
			try {
				bw.Write(x);
				bw.Flush();
			} catch(Exception) {
				throw new DisconnectException();
			}
		}

		void U16(ushort x) {
			try {
				var b = BitConverter.GetBytes(x);
				Array.Reverse(b);
				bw.Write(b);
				bw.Flush();
			} catch(Exception) {
				throw new DisconnectException();
			}
		}

		void U32(uint x) {
			try {
				var b = BitConverter.GetBytes(x);
				Array.Reverse(b);
				bw.Write(b);
				bw.Flush();
			} catch(Exception) {
				throw new DisconnectException();
			}
		}

		void I32(int x) {
			try {
				var b = BitConverter.GetBytes(x);
				Array.Reverse(b);
				bw.Write(b);
				bw.Flush();
			} catch(Exception) {
				throw new DisconnectException();
			}
		}
	}
}
