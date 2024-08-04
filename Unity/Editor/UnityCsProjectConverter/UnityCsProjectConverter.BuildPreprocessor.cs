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
        // need to regenerate before building app to apply generator settings
        public sealed class BuildPreprocessor : IPreprocessBuildWithReport
        {
            public int callbackOrder => CallbackOrder;

            public void OnPreprocessBuild(BuildReport report)
            {
                if (!Prefs.Instance.EnableGenerator)
                    return;

                // for commandline build
                Console.WriteLine(nameof(UnityCsProjectConverter) + ": " + nameof(OnPreprocessBuild));

                // NOTE: it seems that IPostprocessBuild won't run in batch mode when error
                //       register on app quit action for workaround
                RegisterRegenerateActionOnlyOnBatchBuild();


                // NOTE: DisableOnBuild doesn't exit here
                //       it needs to run to revert .csproj files back to original state!!
                bool restore_EnableGenerator = Prefs.Instance.EnableGenerator;
                if (Prefs.Instance.DisableOnBuild)
                {
                    Prefs.Instance.EnableGenerator = false;  // this will force generation method early return
                }


                try
                {
                    _generateForBuild = true;
                    RegenerateProjectFiles();
                }
                finally
                {
                    _generateForBuild = false;
                    Prefs.Instance.EnableGenerator = restore_EnableGenerator;
                }
            }
        }

    }
}
