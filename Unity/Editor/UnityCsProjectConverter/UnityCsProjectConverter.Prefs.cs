/** Legacy-to-SDK-Style .csproj Converter for Unity
 ** (c) 2024 https://github.com/sator-imaging
 ** Licensed under the MIT License
 */

using System;
using System.IO;
using System.Text;
using System.Threading;
using UnityEditor;
using UnityEngine;

#nullable enable

namespace SatorImaging.Csproj.Sdk
{
    partial class UnityCsProjectConverter
    {
        /// <summary>
        /// [Thread-Safe]
        /// Preference singleton saved as json in ProjectSettings.
        /// </summary>
        [Serializable]
        public sealed class Prefs
        {
            readonly static string OUTPUT_PATH = Application.dataPath
                + "/../ProjectSettings/" + nameof(UnityCsProjectConverter) + ".json";

            // nuget.org
            [SerializeField] string? latestVoidSdkVersion;
            [SerializeField] int nugetOrgTimeoutMillis = 5000;

            // menus
            [SerializeField] bool enableGenerator = true;
            [SerializeField] bool disableOnBuild = false;
            [SerializeField] bool disableInDebugMode = false;
            [SerializeField] bool enableSdkStyle = true;
            [SerializeField] bool useVoidSdk = true;

            private Prefs()
            {
                EditorApplication.quitting += Save;

                if (File.Exists(OUTPUT_PATH))
                    JsonUtility.FromJsonOverwrite(File.ReadAllText(OUTPUT_PATH, Encoding.UTF8), this);
            }

            volatile static Prefs? _instance;
            public static Prefs Instance => _instance ?? Interlocked.CompareExchange(ref _instance, new(), null) ?? _instance;

            public void Save() => File.WriteAllText(OUTPUT_PATH, JsonUtility.ToJson(this, true), Encoding.UTF8);


            /* =      property      = */

            public string? LatestVoidSdkVersion
            {
                get { return latestVoidSdkVersion; }
                set { latestVoidSdkVersion = value; }
            }

            public int NugetOrgTimeoutMillis
            {
                get { return nugetOrgTimeoutMillis; }
                set { nugetOrgTimeoutMillis = value; }
            }

            public bool EnableGenerator
            {
                get => enableGenerator;
                set
                {
                    if (enableGenerator == value)
                        return;

                    enableGenerator = value;

                    //Save();
                }
            }

            public bool DisableOnBuild
            {
                get => disableOnBuild;
                set
                {
                    if (disableOnBuild == value)
                        return;

                    disableOnBuild = value;

                    //Save();
                }
            }

            public bool EnableSdkStyle
            {
                get => enableSdkStyle;
                set
                {
                    if (enableSdkStyle == value)
                        return;

                    enableSdkStyle = value;

                    //Save();
                }
            }

            public bool UseVoidSdk
            {
                get => useVoidSdk;
                set
                {
                    if (useVoidSdk == value)
                        return;

                    useVoidSdk = value;

                    //Save();
                }
            }

            public bool DisableInDebugMode
            {
                get => disableInDebugMode;
                set => disableInDebugMode = value;
            }

        }

    }
}
