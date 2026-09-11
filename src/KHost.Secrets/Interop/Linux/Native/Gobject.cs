// Ported from Git Credential Manager, and deliberately kept close to it: the only way to take a
// later upstream fix is to diff against it, which reformatting to this repo's house style would
// make impossible. The .editorconfig beside this project turns the rules that would rewrite it off.
//
//     https://github.com/git-ecosystem/git-credential-manager
//     src/Core/Interop/Linux/Native/Gobject.cs  @ 838e3496
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
    public static class Gobject
    {
        private const string LibraryName = "libgobject-2.0.so.0";

        [DllImport(LibraryName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
        public static extern void g_object_ref(IntPtr @object);

        [DllImport(LibraryName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
        public static extern void g_object_unref(IntPtr @object);
    }
}
