using System;
using System.Collections.Generic;
using System.IO;

namespace VNCaster {
	class LazyMemoryStream : Stream {
		private Queue<byte> queue;
		public LazyMemoryStream() {
			queue = new Queue<byte>();
		}

		public void Load(byte[] buffer) {
			for(var i = 0; i < buffer.Length; ++i)
				queue.Enqueue(buffer[i]);
		}

		public override void SetLength(long value) {
			throw new NotImplementedException();
		}

		public override void Flush() {
			throw new NotImplementedException();
		}

		public override int Read(byte[] buffer, int offset, int count) {
			var i = 0;
			for(; i < count; ++i) {
				if(queue.Count != 0)
					buffer[offset++] = queue.Dequeue();
				else
					break;
			}
			return i;
		}

		public override long Seek(long offset, SeekOrigin origin) {
			throw new NotImplementedException();
		}

		public override void Write(byte[] buffer, int offset, int count) {
			throw new NotImplementedException();
		}

		public override bool CanRead {
			get {
				return true;
			}
		}
		public override bool CanSeek {
			get {
				return false;
			}
		}
		public override bool CanWrite {
			get {
				return false;
			}
		}

		public override long Length {
			get {
				return queue.Count;
			}
		}

		public override long Position {
			get {
				throw new NotImplementedException();
			}

			set {
				throw new NotImplementedException();
			}
		}
	}
}
