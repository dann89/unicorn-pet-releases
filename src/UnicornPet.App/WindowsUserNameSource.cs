using System;
using System.Runtime.InteropServices;
using System.Text;
using UnicornPet.Core;

namespace UnicornPet.App
{
    /// <summary>Reads the signed-in user's names from Windows. Values stay in memory; nothing here logs or stores them.</summary>
    public class WindowsUserNameSource : IUserNameSource
    {
        const int NameDisplay = 3;

        [DllImport("secur32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern byte GetUserNameEx(int nameFormat, StringBuilder buffer, ref uint size);

#if !WORKSAFE
        [DllImport("netapi32.dll", CharSet = CharSet.Unicode)]
        static extern int NetUserGetInfo(string server, string user, int level, out IntPtr buf);

        [DllImport("netapi32.dll")]
        static extern int NetApiBufferFree(IntPtr buf);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct USER_INFO_10
        {
            public string name;
            public string comment;
            public string usr_comment;
            public string full_name;
        }
#endif

        public string AccountName
        {
            get { try { return Environment.UserName ?? ""; } catch { return ""; } }
        }

        public string DisplayName
        {
            get
            {
                try
                {
                    var sb = new StringBuilder(256);
                    uint size = (uint)sb.Capacity;
                    if (GetUserNameEx(NameDisplay, sb, ref size) != 0 && sb.Length > 0) return sb.ToString();
                }
                catch { }
#if !WORKSAFE
                try
                {
                    IntPtr buf;
                    if (NetUserGetInfo(null, AccountName, 10, out buf) == 0 && buf != IntPtr.Zero)
                    {
                        try { return Marshal.PtrToStructure<USER_INFO_10>(buf).full_name ?? ""; }
                        finally { NetApiBufferFree(buf); }
                    }
                }
                catch { }
#endif
                return "";
            }
        }
    }
}
