// Ported from Git Credential Manager, and deliberately kept close to it: the only way to take a
// later upstream fix is to diff against it, which reformatting to this repo's house style would
// make impossible. The .editorconfig beside this project turns the rules that would rewrite it off.
//
//     https://github.com/git-ecosystem/git-credential-manager
//     src/Core/Interop/Linux/Native/Glib.cs  @ 838e3496
//
// Copyright (c) GitHub, Inc. and contributors. Licensed under the MIT License.
// See THIRD-PARTY-NOTICES.md and licenses/MIT-git-credential-manager.txt.
//
// Changed from upstream: nullable disabled (upstream is unannotated), namespace.

#nullable disable

using System;
using System.Runtime.InteropServices;

namespace KHost.Secrets.Interop.Linux.Native
{
    public static class Glib
    {
        private const string LibraryName = "libglib-2.0.so.0";

        public struct GHashTable { /* transparent */ }

        [StructLayout(LayoutKind.Sequential)]
        public struct GList
        {
            public IntPtr data;
            public IntPtr next;
            public IntPtr prev;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct GError
        {
            public int domain;
            public int code;
            public IntPtr message;
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate uint GHashFunc(IntPtr key);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate bool GEqualFunc(IntPtr a, IntPtr b);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate void GDestroyNotify(IntPtr data);

        [DllImport(LibraryName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
        public static extern uint g_str_hash(IntPtr key);

        [DllImport(LibraryName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
        public static extern bool g_str_equal(IntPtr a, IntPtr b);

        [DllImport(LibraryName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
        public static extern unsafe GHashTable* g_hash_table_new(GHashFunc hash_func, GEqualFunc key_equal_func);

        [DllImport(LibraryName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
        public static extern unsafe GHashTable* g_hash_table_new_full(
            GHashFunc hash_func,
            GEqualFunc key_equal_func,
            GDestroyNotify key_destroy_func,
            GDestroyNotify value_destroy_func);

        [DllImport(LibraryName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
        public static extern unsafe void g_hash_table_destroy(GHashTable* hash_table);

        [DllImport(LibraryName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
        public static extern unsafe bool g_hash_table_insert(GHashTable* hash_table, IntPtr key, IntPtr value);

        [DllImport(LibraryName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
        public static extern unsafe IntPtr g_hash_table_lookup(GHashTable* hash_table, IntPtr key);

        [DllImport(LibraryName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
        public static extern unsafe void g_list_free_full(GList* list, GDestroyNotify free_func);

        [DllImport(LibraryName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
        public static extern unsafe void g_hash_table_unref(GHashTable* hash_table);

        [DllImport(LibraryName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
        public static extern unsafe void g_error_free(GError* error);

        [DllImport(LibraryName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
        public static extern void g_free(IntPtr mem);
    }
}
