using System.Runtime.InteropServices;

namespace GamePadT9.TestEditor;

// Isolated builds redirect COM through the executable's embedded xiaobai.manifest.
// Profile activation affects this test process only, never the desktop session.
internal static class Isolation
{
    internal static object ReadProfile()
    {
        var manager = (IProfiles)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("33C53A50-F456-4884-B049-85FD643ECFED"), true)!)!;
        var category = new Guid("34745C63-B2F0-4784-8B67-5E12C8701A31");
        try
        {
            Marshal.ThrowExceptionForHR(manager.GetActiveProfile(ref category, out var value));
            return new { type = value.Type, language = value.Language, clsid = value.Clsid, profile = value.Profile, keyboard = value.Keyboard.ToString("X") };
        }
        finally { Marshal.ReleaseComObject(manager); }
    }
    internal static void ActivateProfile(bool standalone = false)
    {
        var type = Type.GetTypeFromCLSID(new Guid("33C53A50-F456-4884-B049-85FD643ECFED"), true)!;
        var manager = (IProfiles)Activator.CreateInstance(type)!;
        var service = new Guid(standalone ? "595B67E9-48A3-4C82-B7B1-64E4A35C9D92" : "A3F4CDED-B1E9-41EE-9CA6-7B4D0DE6CB0A");
        var profile = new Guid(standalone ? "79C457D1-690A-4F83-A3DE-C95C98E01D4D" : "3D02CAB6-2B8E-4781-BA20-1C9267529467");
        const uint forThisProcess = 0x10000000;
        try { Marshal.ThrowExceptionForHR(manager.ActivateProfile(1, 0x0804, ref service, ref profile, 0, forThisProcess)); }
        finally { Marshal.ReleaseComObject(manager); }
    }
    [ComImport, Guid("71C6E74C-0F28-11D8-A82A-00065B84435C"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IProfiles
    {
        [PreserveSig] int ActivateProfile(uint type, ushort lang, ref Guid clsid, ref Guid profile, nint hkl, uint flags);
        [PreserveSig] int DeactivateProfile(uint type, ushort lang, ref Guid clsid, ref Guid profile, nint hkl, uint flags);
        [PreserveSig] int GetProfile(uint type, ushort lang, ref Guid clsid, ref Guid profile, nint hkl, out ProfileData data);
        [PreserveSig] int EnumProfiles(ushort lang, out nint profiles);
        [PreserveSig] int ReleaseInputProcessor(ref Guid clsid, uint flags);
        [PreserveSig] int RegisterProfile(ref Guid clsid, ushort lang, ref Guid profile, nint description, uint count, nint icon, uint iconCount, uint index, nint substitute, uint layout, int enabled, uint flags);
        [PreserveSig] int UnregisterProfile(ref Guid clsid, ushort lang, ref Guid profile, uint flags);
        [PreserveSig] int GetActiveProfile(ref Guid category, out ProfileData data);
    }
    [StructLayout(LayoutKind.Sequential)] private struct ProfileData
    {
        public uint Type;
        public ushort Language;
        public Guid Clsid, Profile, Category;
        public nint Substitute;
        public uint Capabilities;
        public nint Keyboard;
        public uint Flags;
    }
}
