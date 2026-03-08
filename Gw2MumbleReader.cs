using System;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;

namespace LiveSplit.GW2
{
    public class Gw2MumbleReader : IDisposable
    {
        private const string MumbleName = "MumbleLink";
        private MemoryMappedFile _mmf;
        private MemoryMappedViewAccessor _accessor;
        private bool _isOpen;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct Link
        {
            public uint uiVersion;
            public uint uiTick;

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
            public float[] fAvatarPosition;

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
            public float[] fAvatarFront;

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
            public float[] fAvatarTop;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string name;

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
            public float[] fCameraPosition;

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
            public float[] fCameraFront;

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
            public float[] fCameraTop;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string identity;

            public uint context_len;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct Context
        {
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 28)]
            public byte[] serverAddress;

            public uint mapId;
            public uint mapType;
            public uint shardId;
            public uint instance;
            public uint buildId;
            public uint uiState;
            public ushort compassWidth;
            public ushort compassHeight;
            public float compassRotation;
            public float playerX;
            public float playerY;
            public float mapCenterX;
            public float mapCenterY;
            public float mapScale;
            public uint processId;
            public byte mountIndex;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct MumbleData
        {
            public Link link;
            public Context context;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 2048)]
            public string description;
        }

        public bool TryOpen()
        {
            if (_isOpen) return true;

            try
            {
                int size = Marshal.SizeOf(typeof(MumbleData));
                _mmf = MemoryMappedFile.OpenExisting(MumbleName, MemoryMappedFileRights.Read);
                _accessor = _mmf.CreateViewAccessor(0, size, MemoryMappedFileAccess.Read);
                _isOpen = true;
                return true;
            }
            catch
            {
                _isOpen = false;
                return false;
            }
        }

        public bool TryRead(out MumbleData data)
        {
            data = default;

            if (!_isOpen && !TryOpen())
                return false;

            try
            {
                int size = Marshal.SizeOf(typeof(MumbleData));
                byte[] buffer = new byte[size];
                _accessor.ReadArray(0, buffer, 0, size);

                GCHandle handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
                try
                {
                    data = Marshal.PtrToStructure<MumbleData>(handle.AddrOfPinnedObject());
                    return true;
                }
                finally
                {
                    handle.Free();
                }
            }
            catch
            {
                Dispose();
                return false;
            }
        }

        public void Dispose()
        {
            _accessor?.Dispose();
            _mmf?.Dispose();
            _accessor = null;
            _mmf = null;
            _isOpen = false;
        }
    }
}