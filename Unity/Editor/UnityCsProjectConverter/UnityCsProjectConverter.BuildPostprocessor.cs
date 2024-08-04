/** Legacy-to-SDK-Style .csproj Converter for Unity
 ** (c) 2024 https://github.com/sator-imaging
 ** Licensed under the MIT License
 */

using System;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

#nullable enable

namespace SatorImaging.Csproj.Sdk
{
    partial class UnityCsProjectConverter
    {
        // rebuild is required to revert .csproj content back to editor state
        public sealed class BuildPostprocessor : IPostprocessBuildWithReport
        {
            public int callbackOrder => CallbackOrder;

            public void OnPostprocessBuild(BuildReport report)
            {
                if (!Prefs.Instance.EnableGenerator)
                    return;

                // for commandline build
                Console.WriteLine(nameof(UnityCsProjectConverter) + ": " + nameof(OnPostprocessBuild));

                _generateForBuild = false;
                RegenerateProjectFiles();
            }
        }

    }
}
