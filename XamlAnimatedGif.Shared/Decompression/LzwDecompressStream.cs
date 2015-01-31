﻿using System;
using System.IO;
using Buffer = System.Buffer;
#if !NET40
using System.Runtime.CompilerServices;
#endif
using System.Diagnostics;

namespace XamlAnimatedGif.Decompression
{
    class LzwDecompressStream : Stream
    {
        private const int MaxCodeLength = 12;
        private readonly BitReader _reader;
        private readonly CodeTable _codeTable;
        private int _prevCode;
        private byte[] _remainingBytes;
        private bool _endOfStream;

        public LzwDecompressStream(byte[] compressedBuffer, int minimumCodeLength)
        {
            _reader = new BitReader(compressedBuffer);
            _codeTable = new CodeTable(minimumCodeLength);
        }
        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            ValidateReadArgs(buffer, offset, count);

            if (_endOfStream)
                return 0;

            int read = 0;

            FlushRemainingBytes(buffer, offset, count, ref read);

            while (read < count)
            {
                int code = _reader.ReadBits(_codeTable.CodeLength);
                
                if (!ProcessCode(code, buffer, offset, count, ref read))
                {
                    _endOfStream = true;
                    break;
                }
            }
            return read;
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        public override bool CanRead
        {
            get { return true; }
        }

        public override bool CanSeek
        {
            get { return false; }
        }

        public override bool CanWrite
        {
            get { return true; }
        }

        public override long Length
        {
            get { throw new NotSupportedException(); }
        }

        public override long Position
        {
            get { throw new NotSupportedException(); }
            set { throw new NotSupportedException(); }
        }

        private void InitCodeTable()
        {
            _codeTable.Reset();
            _prevCode = -1;
        }

        private static byte[] CopySequenceToBuffer(byte[] sequence, byte[] buffer, int offset, int count, ref int read)
        {
            int bytesToRead = Math.Min(sequence.Length, count - read);
            Buffer.BlockCopy(sequence, 0, buffer, offset + read, bytesToRead);
            read += bytesToRead;
            byte[] remainingBytes = null;
            if (bytesToRead < sequence.Length)
            {
                int remainingBytesCount = sequence.Length - bytesToRead;
                remainingBytes = new byte[remainingBytesCount];
                Buffer.BlockCopy(sequence, bytesToRead, remainingBytes, 0, remainingBytesCount);
            }
            return remainingBytes;
        }

        private void FlushRemainingBytes(byte[] buffer, int offset, int count, ref int read)
        {
            // If we read too many bytes last time, copy them first;
            if (_remainingBytes != null)
                _remainingBytes = CopySequenceToBuffer(_remainingBytes, buffer, offset, count, ref read);
        }

        [Conditional("DISABLED")]
        private static void ValidateReadArgs(byte[] buffer, int offset, int count)
        {
            if (buffer == null) throw new ArgumentNullException("buffer");
            if (offset < 0)
                throw new ArgumentOutOfRangeException("offset", "Offset can't be negative");
            if (count < 0)
                throw new ArgumentOutOfRangeException("count", "Count can't be negative");
            if (offset + count > buffer.Length)
                throw new ArgumentException("Buffer is to small to receive the requested data");
        }

        private bool ProcessCode(int code, byte[] buffer, int offset, int count, ref int read)
        {
            if (code < _codeTable.Count)
            {
                var sequence = _codeTable[code];
                if (sequence.IsStopCode)
                {
                    return false;
                }
                if (sequence.IsClearCode)
                {
                    InitCodeTable();
                    return true;
                }
                _remainingBytes = CopySequenceToBuffer(sequence.Bytes, buffer, offset, count, ref read);
                if (_prevCode >= 0)
                {
                    var prev = _codeTable[_prevCode];
                    var newSequence = prev.Append(sequence.Bytes[0]);
                    _codeTable.Add(newSequence);
                }
            }
            else
            {
                var prev = _codeTable[_prevCode];
                var newSequence = prev.Append(prev.Bytes[0]);
                _codeTable.Add(newSequence);
                _remainingBytes = CopySequenceToBuffer(newSequence.Bytes, buffer, offset, count, ref read);
            }
            _prevCode = code;
            return true;
        }

        struct Sequence
        {
            private readonly byte[] _bytes;
            private readonly bool _isClearCode;
            private readonly bool _isStopCode;

            public Sequence(byte[] bytes)
                : this()
            {
                _bytes = bytes;
            }

            private Sequence(bool isClearCode, bool isStopCode)
                : this()
            {
                _isClearCode = isClearCode;
                _isStopCode = isStopCode;
            }

            public byte[] Bytes
            {
                get { return _bytes; }
            }

            public bool IsClearCode
            {
                get { return _isClearCode; }
            }

            public bool IsStopCode
            {
                get { return _isStopCode; }
            }

            private static readonly Sequence _clearCode = new Sequence(true, false);
            public static Sequence ClearCode
            {
                get { return _clearCode; }
            }

            private static readonly Sequence _stopCode = new Sequence(false, true);
            public static Sequence StopCode
            {
                get { return _stopCode; }
            }

            public Sequence Append(byte b)
            {
                var bytes = new byte[_bytes.Length + 1];
                _bytes.CopyTo(bytes, 0);
                bytes[_bytes.Length] = b;
                return new Sequence(bytes);
            }
        }

        class CodeTable
        {
            private readonly int _minimumCodeLength;
            private readonly Sequence[] _table;
            private int _count;
            private int _codeLength;

            public CodeTable(int minimumCodeLength)
            {
                _minimumCodeLength = minimumCodeLength;
                _codeLength = _minimumCodeLength + 1;
                int initialEntries = 1 << minimumCodeLength;
                _table = new Sequence[1 << MaxCodeLength];
                for (int i = 0; i < initialEntries; i++)
                {
                    _table[_count++] = new Sequence(new[] {(byte) i});
                }
                Add(Sequence.ClearCode);
                Add(Sequence.StopCode);
            }
#if !NET40
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
#endif
            public void Reset()
            {
                _count = (1 << _minimumCodeLength) + 2;
                _codeLength = _minimumCodeLength + 1;
            }

#if !NET40
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
#endif
            public void Add(Sequence sequence)
            {
                _table[_count++] = sequence;
                if ((_count & (_count - 1)) == 0 && _codeLength < MaxCodeLength)
                    _codeLength++;
            }

            public Sequence this[int index]
            {
#if !NET40
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
#endif
                get
                {
                    return _table[index];
                }
            }

            public int Count
            {
#if !NET40
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
#endif
                get { return _count; }
            }

            public int CodeLength
            {
#if !NET40
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
#endif
                get { return _codeLength; }
            }
        }
    }
}
