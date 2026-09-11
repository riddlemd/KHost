// Ported from Git Credential Manager, and deliberately kept close to it: the only way to take a
// later upstream fix is to diff against it, which reformatting to this repo's house style would
// make impossible. The .editorconfig beside this project turns the rules that would rewrite it off.
//
//     https://github.com/git-ecosystem/git-credential-manager
//     src/Core/Interop/Windows/WindowsCredential.cs  @ 838e3496
//
// Copyright (c) GitHub, Inc. and contributors. Licensed under the MIT License.
// See THIRD-PARTY-NOTICES.md and licenses/MIT-git-credential-manager.txt.
//
// Changed from upstream: nullable disabled (upstream is unannotated), namespace.

#nullable disable


namespace KHost.Secrets.Interop.Windows
{
    public class WindowsCredential : ICredential
    {
        public WindowsCredential(string service, string userName, string password, string targetName)
        {
            Service = service;
            UserName = userName;
            Password = password;
            TargetName = targetName;
        }

        public string Service { get; }

        public string UserName { get; }

        public string Password { get; }

        public string TargetName { get; }

        string ICredential.Account => UserName;
    }
}
