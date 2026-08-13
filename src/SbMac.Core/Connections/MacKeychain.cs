using System.Runtime.InteropServices;
using System.Text;

namespace SbMac.Core.Connections;

/// <summary>
/// Thin managed wrapper over the macOS Security framework's generic-password items.
/// </summary>
/// <remarks>
/// This talks to <c>SecItemAdd</c> / <c>SecItemCopyMatching</c> / <c>SecItemUpdate</c> /
/// <c>SecItemDelete</c> directly rather than shelling out to <c>/usr/bin/security</c>.
/// <para>
/// The CLI is not usable here. <c>security add-generic-password -w</c> with no value
/// prompts on the terminal rather than reading stdin, so a GUI app writes an item with an
/// empty password — and <c>security</c> still exits 0, so the failure is silent. Passing
/// the secret as <c>-w &lt;value&gt;</c> or <c>-X &lt;hex&gt;</c> does work, but puts it in
/// the process arguments where any process running as the same user can read it out of
/// <c>ps</c>. Going through the framework keeps the secret in this process's memory.
/// </para>
/// </remarks>
internal static class MacKeychain
{
    // OSStatus values from <Security/SecBase.h>.
    const int ErrSecSuccess = 0;
    const int ErrSecDuplicateItem = -25299;
    const int ErrSecItemNotFound = -25300;
    const int ErrSecAuthFailed = -25293;
    const int ErrSecUserCanceled = -128;

    /// <summary>Stores or replaces the secret for a service/account pair.</summary>
    /// <exception cref="SecretStoreException">The keychain rejected the write.</exception>
    public static void Set(string service, string account, string secret)
    {
        var secretBytes = Encoding.UTF8.GetBytes(secret);
        var scope = new CoreFoundation.Scope();

        try
        {
            var query = BuildBaseQuery(service, account, scope);

            // Try to update an existing item first. SecItemAdd on a duplicate fails with
            // errSecDuplicateItem, and there is no "upsert" primitive.
            var update = CoreFoundation.CreateMutableDictionary(scope);
            CoreFoundation.DictionarySet(update, SecurityFramework.SecValueData,
                CoreFoundation.CreateData(secretBytes, scope));

            var status = SecurityFramework.SecItemUpdate(query, update);

            if (status == ErrSecItemNotFound)
            {
                // Nothing to update, so create it. The attributes dictionary for an add is
                // the query plus the data and an accessibility class.
                var attributes = BuildBaseQuery(service, account, scope);
                CoreFoundation.DictionarySet(attributes, SecurityFramework.SecValueData,
                    CoreFoundation.CreateData(secretBytes, scope));

                // Readable only while the keychain is unlocked, and never synced to iCloud
                // or included in a backup that could move it to another machine.
                CoreFoundation.DictionarySet(attributes, SecurityFramework.SecAttrAccessible,
                    SecurityFramework.SecAttrAccessibleWhenUnlockedThisDeviceOnly);

                status = SecurityFramework.SecItemAdd(attributes, IntPtr.Zero);
            }

            if (status != ErrSecSuccess)
            {
                throw new SecretStoreException(DescribeStatus(status, "save the secret to"));
            }
        }
        finally
        {
            scope.Dispose();
            Array.Clear(secretBytes);
        }
    }

    /// <summary>Reads a secret. Returns null when no item exists for that service/account.</summary>
    /// <exception cref="SecretStoreException">The item exists but could not be read.</exception>
    public static string? Get(string service, string account)
    {
        var scope = new CoreFoundation.Scope();

        try
        {
            var query = BuildBaseQuery(service, account, scope);
            CoreFoundation.DictionarySet(query, SecurityFramework.SecReturnData, CoreFoundation.True);
            CoreFoundation.DictionarySet(query, SecurityFramework.SecMatchLimit, SecurityFramework.SecMatchLimitOne);

            var status = SecurityFramework.SecItemCopyMatching(query, out var result);

            if (status == ErrSecItemNotFound)
            {
                return null;
            }

            if (status != ErrSecSuccess)
            {
                throw new SecretStoreException(DescribeStatus(status, "read the secret from"));
            }

            if (result == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                var length = (int)CoreFoundation.CFDataGetLength(result);
                if (length == 0)
                {
                    // An empty item is what the old CLI-based implementation wrote. Report
                    // it as absent so the caller prompts for the value again.
                    return null;
                }

                var bytes = new byte[length];
                Marshal.Copy(CoreFoundation.CFDataGetBytePtr(result), bytes, 0, length);
                return Encoding.UTF8.GetString(bytes);
            }
            finally
            {
                CoreFoundation.CFRelease(result);
            }
        }
        finally
        {
            scope.Dispose();
        }
    }

    /// <summary>Removes a secret. Succeeds whether or not the item existed.</summary>
    public static void Delete(string service, string account)
    {
        var scope = new CoreFoundation.Scope();

        try
        {
            var status = SecurityFramework.SecItemDelete(BuildBaseQuery(service, account, scope));

            // A missing item is the desired end state, not a failure.
            if (status is not (ErrSecSuccess or ErrSecItemNotFound))
            {
                throw new SecretStoreException(DescribeStatus(status, "remove the secret from"));
            }
        }
        finally
        {
            scope.Dispose();
        }
    }

    /// <summary>The class/service/account triple that identifies one item.</summary>
    static IntPtr BuildBaseQuery(string service, string account, CoreFoundation.Scope scope)
    {
        var query = CoreFoundation.CreateMutableDictionary(scope);

        CoreFoundation.DictionarySet(query, SecurityFramework.SecClass, SecurityFramework.SecClassGenericPassword);
        CoreFoundation.DictionarySet(query, SecurityFramework.SecAttrService, CoreFoundation.CreateString(service, scope));
        CoreFoundation.DictionarySet(query, SecurityFramework.SecAttrAccount, CoreFoundation.CreateString(account, scope));

        return query;
    }

    static string DescribeStatus(int status, string action) => status switch
    {
        ErrSecAuthFailed =>
            $"macOS refused access to the keychain when trying to {action} it. " +
            "Unlock your login keychain and try again.",

        ErrSecUserCanceled =>
            $"The keychain prompt was dismissed, so SB-Mac could not {action} the keychain.",

        _ => $"Could not {action} the macOS keychain (OSStatus {status})."
    };

    // ───────────────────────────── interop ─────────────────────────────

    static class CoreFoundation
    {
        const string Library = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

        /// <summary>UTF-8, from CFStringBuiltInEncodings.</summary>
        const uint Utf8 = 0x08000100;

        static readonly IntPtr Handle =
            NativeLibrary.Load("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation");

        /// <summary>kCFBooleanTrue, used for the boolean-valued query keys.</summary>
        public static readonly IntPtr True = ReadGlobal("kCFBooleanTrue");

        static readonly IntPtr TypeDictionaryKeyCallBacks = GetSymbol("kCFTypeDictionaryKeyCallBacks");
        static readonly IntPtr TypeDictionaryValueCallBacks = GetSymbol("kCFTypeDictionaryValueCallBacks");

        /// <summary>
        /// Tracks every CF object created for one operation so they are all released
        /// together, even if a call throws part-way through.
        /// </summary>
        public sealed class Scope : IDisposable
        {
            readonly List<IntPtr> objects = [];

            public IntPtr Track(IntPtr value)
            {
                if (value != IntPtr.Zero)
                {
                    objects.Add(value);
                }

                return value;
            }

            public void Dispose()
            {
                foreach (var value in objects)
                {
                    CFRelease(value);
                }

                objects.Clear();
            }
        }

        public static IntPtr CreateString(string value, Scope scope)
        {
            var bytes = Encoding.UTF8.GetBytes(value);
            return scope.Track(CFStringCreateWithBytes(IntPtr.Zero, bytes, bytes.Length, Utf8, false));
        }

        public static IntPtr CreateData(byte[] value, Scope scope) =>
            scope.Track(CFDataCreate(IntPtr.Zero, value, value.Length));

        public static IntPtr CreateMutableDictionary(Scope scope) =>
            scope.Track(CFDictionaryCreateMutable(
                IntPtr.Zero, 0, TypeDictionaryKeyCallBacks, TypeDictionaryValueCallBacks));

        /// <summary>
        /// The dictionary retains whatever is added, so entries stay valid for as long as
        /// it does — the scope releases the caller's own reference.
        /// </summary>
        public static void DictionarySet(IntPtr dictionary, IntPtr key, IntPtr value) =>
            CFDictionarySetValue(dictionary, key, value);

        /// <summary>Reads a CFTypeRef global (the symbol holds a pointer to the object).</summary>
        static IntPtr ReadGlobal(string symbol) => Marshal.ReadIntPtr(GetSymbol(symbol));

        /// <summary>Reads the address of a global, for symbols that are structs rather than pointers.</summary>
        static IntPtr GetSymbol(string symbol) => NativeLibrary.GetExport(Handle, symbol);

        [DllImport(Library)]
        public static extern void CFRelease(IntPtr value);

        [DllImport(Library)]
        public static extern IntPtr CFDataGetBytePtr(IntPtr data);

        [DllImport(Library)]
        public static extern nint CFDataGetLength(IntPtr data);

        [DllImport(Library)]
        static extern IntPtr CFStringCreateWithBytes(
            IntPtr allocator, byte[] bytes, nint length, uint encoding, [MarshalAs(UnmanagedType.I1)] bool isExternalRepresentation);

        [DllImport(Library)]
        static extern IntPtr CFDataCreate(IntPtr allocator, byte[] bytes, nint length);

        [DllImport(Library)]
        static extern IntPtr CFDictionaryCreateMutable(
            IntPtr allocator, nint capacity, IntPtr keyCallBacks, IntPtr valueCallBacks);

        [DllImport(Library)]
        static extern void CFDictionarySetValue(IntPtr dictionary, IntPtr key, IntPtr value);
    }

    static class SecurityFramework
    {
        const string Library = "/System/Library/Frameworks/Security.framework/Security";

        static readonly IntPtr Handle =
            NativeLibrary.Load("/System/Library/Frameworks/Security.framework/Security");

        public static readonly IntPtr SecClass = ReadGlobal("kSecClass");
        public static readonly IntPtr SecClassGenericPassword = ReadGlobal("kSecClassGenericPassword");
        public static readonly IntPtr SecAttrService = ReadGlobal("kSecAttrService");
        public static readonly IntPtr SecAttrAccount = ReadGlobal("kSecAttrAccount");
        public static readonly IntPtr SecValueData = ReadGlobal("kSecValueData");
        public static readonly IntPtr SecReturnData = ReadGlobal("kSecReturnData");
        public static readonly IntPtr SecMatchLimit = ReadGlobal("kSecMatchLimit");
        public static readonly IntPtr SecMatchLimitOne = ReadGlobal("kSecMatchLimitOne");
        public static readonly IntPtr SecAttrAccessible = ReadGlobal("kSecAttrAccessible");

        public static readonly IntPtr SecAttrAccessibleWhenUnlockedThisDeviceOnly =
            ReadGlobal("kSecAttrAccessibleWhenUnlockedThisDeviceOnly");

        static IntPtr ReadGlobal(string symbol) => Marshal.ReadIntPtr(NativeLibrary.GetExport(Handle, symbol));

        [DllImport(Library)]
        public static extern int SecItemAdd(IntPtr attributes, IntPtr result);

        [DllImport(Library)]
        public static extern int SecItemCopyMatching(IntPtr query, out IntPtr result);

        [DllImport(Library)]
        public static extern int SecItemUpdate(IntPtr query, IntPtr attributesToUpdate);

        [DllImport(Library)]
        public static extern int SecItemDelete(IntPtr query);
    }
}
