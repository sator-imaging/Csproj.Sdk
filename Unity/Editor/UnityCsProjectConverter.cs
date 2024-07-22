/** Legacy-to-SDK-Style .csproj Converter for Unity
 ** (c) 2024 https://github.com/sator-imaging
 ** Licensed under the MIT License
 */

using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Unity.CodeEditor;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.Compilation;
using UnityEngine;

#nullable enable

namespace SatorImaging.Csproj.Sdk
{
    /// <summary>
    /// Migrate legacy style `.csproj` files to newer sdk style format.
    /// </summary>
    public sealed class UnityCsProjectConverter : AssetPostprocessor
    {
        readonly static string EXT_SHARED = ".UnityShared";
        readonly static string EXT_EDITOR = ".UnityEditor";
        readonly static string EXT_PROPS = ".props";
        readonly static string EXT_TARGETS = ".targets";
        readonly static string UNITY_PROJ_DIR_NAME = Directory.GetParent(Application.dataPath).Name;
        readonly static string XML_NS = @"http://schemas.microsoft.com/developer/msbuild/2003";

        /// <summary>
        /// NOTE: version must be specified. ex) Sdk.PackageName.On.Nuget.Org/1.0.0
        /// </summary>
        public static string CustomSdkNameWithVersion { get; set; }
            = nameof(UnityCsProjectConverter) + "." + nameof(CustomSdkNameWithVersion) + " is not set";

        /// <inheritdoc cref="CustomSdkNameWithVersion"/>
        public readonly static string SDK_VOID_NAME_SLASH = "Csproj.Sdk.Void/";
        public readonly static string SDK_VOID_DEFAULT_VERSION = "1.1.0";

        private static string? _latestVoidSdkNameWithVersion;
        public static string LatestVoidSdkNameWithVersion
            => _latestVoidSdkNameWithVersion ??= SDK_VOID_NAME_SLASH + (GetVoidSdkVersionFromNugetOrgThread() ?? SDK_VOID_DEFAULT_VERSION);

        // NOTE: this and internal pre/post build processors could change .csproj file content and it may affect Unity or other scripts.
        //       so callbacks must be called earlier as possible than others, and leave room to insert something before.
        //       * don't use actual minimum value, it could be casted to wrong float inside unity. e.g. MenuItem priority
        /// <summary>Set callback order for this class and internal pre/post build processors.</summary>
        public static int CallbackOrder { get; set; } = int.MinValue + 310;

        public override int GetPostprocessOrder() => CallbackOrder;


        /*  .sln  ================================================================ */

        /*
        static string OnGeneratedSlnSolution(string path, string content)
        {
        }
        */


        /*  .csproj  ================================================================ */

        static bool _generateForBuild = false;

        static string OnGeneratedCSProject(string path, string content)
        {
            // return as-is
            if (!Prefs.Instance.EnableGenerator)
                return content;

            if (CompilationPipeline.codeOptimization == CodeOptimization.Debug)
            {
                if (Prefs.Instance.DisableInDebugMode)
                    return content;
            }

            const string MSBUILD_AUTO_IMPORT = "Directory.Build";
            CreateFileIfNotExists(MSBUILD_AUTO_IMPORT + EXT_PROPS, "<Nullable>enable</Nullable>");
            CreateFileIfNotExists(MSBUILD_AUTO_IMPORT + EXT_TARGETS, null);

            //// TODO: double clicking .cs file in Unity always call this method for all assemblies (.asmdef)
            ////       need to make it more efficient and faster
            //UnityEngine.Debug.Log(nameof(UnityCsProjectConverter) + ": " + nameof(OnGeneratedCSProject) + ": " + path);

            var xdoc = XDocument.Parse(content);
            var ns = XNamespace.Get(XML_NS);
            var root = xdoc.Root;

            //sdk!!
            const string ATTR_SDK = "Sdk";

            if (Prefs.Instance.EnableSdkStyle)
            {
                if (!root.Attributes().Any(x => x.Name.LocalName.Equals(ATTR_SDK, StringComparison.OrdinalIgnoreCase)))
                {
                    root.RemoveAttributes();
                    root.SetAttributeValue(ATTR_SDK, Prefs.Instance.UseVoidSdk ? LatestVoidSdkNameWithVersion : CustomSdkNameWithVersion);
                }
            }


            // NOTE: these update will show "Attach to Unity" button in VS toolbar.
            //       but it is not work correctly just slows down VS launch time.
            //https://github.com/Unity-Technologies/com.unity.ide.visualstudio/blob/master/Packages/com.unity.ide.visualstudio/Editor/ProjectGeneration/SdkStyleProjectGeneration.cs
            /*
            const string TAG_CAPABILITY = "ProjectCapability";
            const string ATTR_INCLUDE = "Include";
            const string ATTR_REMOVE = "Remove";
            const string TAG_ITEMGROUP = "ItemGroup";
            if (Prefs.Instance.EnableSdkStyle)
            {
                //header
                var headerItemGroup = new XElement(ns.GetName(TAG_ITEMGROUP));
                headerItemGroup.Add(new XComment(nameof(UnityCsProjectConverter)));

                foreach (var cap in new string[]
                {
                    "Unity",
                })
                {
                    headerItemGroup.Add(new XElement(ns.GetName(TAG_CAPABILITY), new XAttribute(ATTR_INCLUDE, cap)));
                }
                root.AddFirst(headerItemGroup);

                //footer
                var footerItemGroup = new XElement(ns.GetName(TAG_ITEMGROUP));
                footerItemGroup.Add(new XComment(nameof(UnityCsProjectConverter)));

                foreach (var cap in new string[]
                {
                    "LaunchProfiles",
                    "SharedProjectReferences",
                    "ReferenceManagerSharedProjects",
                    "ProjectReferences",
                    "ReferenceManagerProjects",
                    "COMReferences",
                    "ReferenceManagerCOM",
                    "AssemblyReferences",
                    "ReferenceManagerAssemblies",
                })
                {
                    footerItemGroup.Add(new XElement(ns.GetName(TAG_CAPABILITY), new XAttribute(ATTR_REMOVE, cap)));
                }
                root.Add(footerItemGroup);
            }
            */


            //version!?
            const string TAG_GENERATOR = "UnityProjectGenerator";

            var generatorNode = xdoc.Descendants(ns.GetName(TAG_GENERATOR)).FirstOrDefault();
            if (generatorNode != null)
            {
                generatorNode.Value += "-" + nameof(UnityCsProjectConverter);
            }


            // this converter doesn't write file, reusing writer!!
            xdoc.Save(cache_writer, SaveOptions.None);
            cache_writer.Flush();

            // remove namespace!! no way to achieve by using XDocument interface!!
            if (Prefs.Instance.EnableSdkStyle)
            {
                cache_sb.Replace(" xmlns=\"" + XML_NS + "\"", string.Empty, 0, 512);  // 256 is enough, 512 for safe
            }

            content = cache_sb.ToString();
            cache_sb.Length = 0;  // don't clear! buffer will be gone!!

            return content;
        }


        // NOTE: resulting .csproj file is written by Unity, not by this converter, and file encoding is utf-8.
        //       this writer class is required due to XDocument automatically update <?xml encoding="..."?> to writer's encoding. (ex: utf-16)
        sealed class XDocumentWriter : StringWriter
        {
            public XDocumentWriter(StringBuilder sb) : base(sb, CultureInfo.InvariantCulture) { }
            public override Encoding Encoding => Encoding.UTF8;
        }

        readonly static StringBuilder cache_sb = new(capacity: 65536);  // usual .csproj file size is around 60KiB
        readonly static XDocumentWriter cache_writer = new(cache_sb);


        /*  helper  ================================================================ */

        static void CreateFileIfNotExists(string fileName, string? propertyGroupContent)
        {
            if (File.Exists(fileName))
                return;

            string content =
$@"<Project xmlns=""{XML_NS}"">
    <PropertyGroup>
";

            if (propertyGroupContent != null)
            {
                content += "        " + propertyGroupContent.Replace("\n", "\n        ").TrimEnd() + Environment.NewLine;
            }

            content +=
$@"    </PropertyGroup>
</Project>
";

            File.WriteAllText(fileName, content, Encoding.UTF8);
        }


        /*  build events  ================================================================ */

        static void RegisterRegenerateActionOnlyOnBatchBuild()
        {
            if (Application.isBatchMode && BuildPipeline.isBuildingPlayer)
            {
                // NOTE: Applicatoin.quitting won't run as expected.
                //       it's invoked on quitting play mode in editor.
                EditorApplication.quitting += () =>
                {
                    _generateForBuild = false;
                    RegenerateProjectFiles();
                };
            }
        }

        static void RegenerateProjectFiles()
        {
            var current = CodeEditor.Editor.CurrentCodeEditor;
            current.SyncAll();
        }


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
                bool restoreEnableGenerator = Prefs.Instance.EnableGenerator;
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
                    Prefs.Instance.EnableGenerator = restoreEnableGenerator;
                }
            }
        }


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


        /*  editor  ================================================================ */

        static class EditorExtension
        {
            // update EditorInitializeOnLoad when checked menu item added
            const string MENU_ROOT = "File/C# Project (.csproj)/";
            const string MENU_ENABLE_GENERATOR = MENU_ROOT + "Enable Override";
            const string MENU_DISABLE_ON_BUILD = MENU_ROOT + "Disable Override on Build";
            const string MENU_DISABLE_IN_DEBUG_MODE = MENU_ROOT + "Disable Override in Debug Mode \t (experimental)";
            const string MENU_ENABLE_SDK_STYLE = MENU_ROOT + "Enable SDK Style \t (latest .csproj format)";
            const string MENU_USE_VOID_SDK = MENU_ROOT + "Use \"Void\" SDK";
            const string MENU_REVIEW_CSPROJ = MENU_ROOT + "Review Resulting .csproj...";
            const int PRIORITY_MENU = 209;  // 200: separator disappears 210: sandwiched by `Build` items 220: right after `Build` 230: right before `Exit`


            // NOTE: InitializeOnLoad cannot be used in UPM package script.
            //       Unity loads packages before Editor assembly ready so editor callbacks won't run.
            //[InitializeOnLoadMethod]
            [UnityEditor.Callbacks.DidReloadScripts]
            static void UnityEditor_Initialize()
            {
                // must delay to wait menu item creation
                EditorApplication.delayCall += () =>
                {
                    var prefs = Prefs.Instance;
                    Menu.SetChecked(MENU_ENABLE_GENERATOR, prefs.EnableGenerator);
                    Menu.SetChecked(MENU_DISABLE_ON_BUILD, prefs.DisableOnBuild);
                    Menu.SetChecked(MENU_DISABLE_IN_DEBUG_MODE, prefs.DisableInDebugMode);
                    Menu.SetChecked(MENU_ENABLE_SDK_STYLE, prefs.EnableSdkStyle);
                    Menu.SetChecked(MENU_USE_VOID_SDK, prefs.UseVoidSdk);

                    // need to save to .json before recompile in Unity editor
                    CompilationPipeline.compilationStarted -= OnCompilationStarted;
                    CompilationPipeline.compilationStarted += OnCompilationStarted;

                    CompilationPipeline.codeOptimizationChanged -= OnCodeOptimizationChanged;
                    CompilationPipeline.codeOptimizationChanged += OnCodeOptimizationChanged;
                };
            }

            readonly static Action<object> OnCompilationStarted = _ => Prefs.Instance.Save();

            readonly static Action<CodeOptimization> OnCodeOptimizationChanged = _ => RegenerateProjectFiles();


            /* =      enable generator      = */

            [MenuItem(MENU_ENABLE_GENERATOR, priority = PRIORITY_MENU)]
            static void Menu_EnableGenerator_Toggle()
            {
                var toggled = !Prefs.Instance.EnableGenerator;
                Prefs.Instance.EnableGenerator = toggled;
                Menu.SetChecked(MENU_ENABLE_GENERATOR, toggled);
            }


            /* =      disable on build      = */

            [MenuItem(MENU_DISABLE_ON_BUILD, validate = true)]
            static bool Menu_DisableOnBuild_Validate() => Prefs.Instance.EnableGenerator;

            [MenuItem(MENU_DISABLE_ON_BUILD, priority = PRIORITY_MENU)]
            static void Menu_DisableOnBuild_Toggle()
            {
                var toggled = !Prefs.Instance.DisableOnBuild;
                Prefs.Instance.DisableOnBuild = toggled;
                Menu.SetChecked(MENU_DISABLE_ON_BUILD, toggled);
            }


            /* =      disable in debug mode      = */

            [MenuItem(MENU_DISABLE_IN_DEBUG_MODE, validate = true)]
            static bool Menu_DisableInDebugMode_Validate() => Prefs.Instance.EnableGenerator;

            [MenuItem(MENU_DISABLE_IN_DEBUG_MODE, priority = PRIORITY_MENU)]
            static void Menu_DisableInDebugMode_Toggle()
            {
                if (Prefs.Instance.DisableInDebugMode == false)
                {
                    if (!EditorUtility.DisplayDialog(nameof(UnityCsProjectConverter),
                        "Strongly recommend that turn off \"Auto Reload Project\" option in Tools for Unity preference found in Visual Studio.\n\n"
                        + "When auto-reloading is enabled and Visual Studio is open while changing to debug mode, it could cause VS indefinitely reloading.",
                        "Confirm", "cancel"))
                    {
                        return;
                    }
                }

                var toggled = !Prefs.Instance.DisableInDebugMode;
                Prefs.Instance.DisableInDebugMode = toggled;
                Menu.SetChecked(MENU_DISABLE_IN_DEBUG_MODE, toggled);
            }


            /* =      enable sdk style      = */

            [MenuItem(MENU_ENABLE_SDK_STYLE, validate = true)]
            static bool Menu_EnableSdkStyle_Validate() => Prefs.Instance.EnableGenerator;

            [MenuItem(MENU_ENABLE_SDK_STYLE, priority = PRIORITY_MENU + 50)]  //+50
            static void Menu_EnableSdkStyle_Toggle()
            {
                var toggled = !Prefs.Instance.EnableSdkStyle;
                Prefs.Instance.EnableSdkStyle = toggled;
                Menu.SetChecked(MENU_ENABLE_SDK_STYLE, toggled);
            }


            /* =      use void sdk      = */

            [MenuItem(MENU_USE_VOID_SDK, validate = true)]
            static bool Menu_UseVoidSdk_Validate() => Prefs.Instance.EnableGenerator && Prefs.Instance.EnableSdkStyle;

            [MenuItem(MENU_USE_VOID_SDK, priority = PRIORITY_MENU + 50)]  //+50
            static void Menu_UseVoidSdk_Toggle()
            {
                var toggled = !Prefs.Instance.UseVoidSdk;
                Prefs.Instance.UseVoidSdk = toggled;
                Menu.SetChecked(MENU_USE_VOID_SDK, toggled);
            }


            /* =      review resulting .csproj      = */
#if UNITY_EDITOR_WIN
            [MenuItem(MENU_REVIEW_CSPROJ, priority = PRIORITY_MENU + 100)]  //+100
#endif
            static void ReviewResultingCsproj()
            {
                var targetPath = EditorUtility.OpenFilePanelWithFilters(nameof(UnityCsProjectConverter),
                    Application.dataPath + "/../.", new string[] { "C# Project", "csproj" });

                if (!File.Exists(targetPath))
                    throw new FileNotFoundException(targetPath);


                var tempDirPath = Path.GetTempPath();
                var tempFilePath = Path.Combine(tempDirPath, UNITY_PROJ_DIR_NAME + ".msbuild.preprocess.xml");

                // don't exit automatically. need to see result when error occurred
                var cmdString = $"/k dotnet msbuild -preprocess:\"{tempFilePath}\" \"{targetPath}\" & echo. & echo    Close this window to continue...";
                UnityEngine.Debug.Log($"[CMD]: cmd {cmdString}");

                // CMD> dotnet msbuild -preprocess:<fileName>.xml
                var dotnetProc = new ProcessStartInfo("cmd", cmdString)
                {
                    CreateNoWindow = false,
                    ErrorDialog = true,
                };
                var proc = Process.Start(dotnetProc);
                proc.WaitForExit();

                Process.Start("notepad", tempFilePath);
            }
        }


        /*  nuget api  ================================================================ */

        readonly static string NUGET_EP = @"https://api.nuget.org/v3-flatcontainer/csproj.sdk.void/index.json";
        readonly static string MIME_JSON = @"application/json";
        readonly static string PREF_LAST_FETCH_TIME = nameof(SatorImaging) + nameof(Csproj) + nameof(UnityCsProjectConverter) + nameof(PREF_LAST_FETCH_TIME);
        readonly static DateTime FETCH_TIME_EPOCH = new(2024, 1, 1);  // TODO: overflows 68 years later!

        [Serializable] sealed class NugetPayload { public string[]? versions; }

        static string? GetVoidSdkVersionFromNugetOrgThread()
        {
            // NOTE: don't save last fetch time in prefs!! no worth to share each user fetch time on git!!
            var lastFetchTimeSecsFromCustomEpoch = EditorPrefs.GetInt(PREF_LAST_FETCH_TIME, 0);

            string? foundLatestVersion = null;

            // don't fetch nuget.org repeatedly
            var prefs = Prefs.Instance;
            var elapsedTimeFromLastFetch = DateTime.UtcNow - FETCH_TIME_EPOCH.AddSeconds(lastFetchTimeSecsFromCustomEpoch);
            if (elapsedTimeFromLastFetch.TotalDays < 1)
            {
                //UnityEngine.Debug.Log("[NuGet] elapsed time from last fetch < 1 day: " + elapsedTimeFromLastFetch);

                foundLatestVersion = prefs.LatestVoidSdkVersion;
                goto VALIDATE_AND_RETURN;
            }

            EditorPrefs.SetInt(PREF_LAST_FETCH_TIME, Convert.ToInt32((DateTime.UtcNow - FETCH_TIME_EPOCH).TotalSeconds));


            UnityEngine.Debug.Log("[NuGet] fetching nuget.org for package information...: " + SDK_VOID_NAME_SLASH);

            var tcs = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
            try
            {
                ThreadPool.QueueUserWorkItem(async static (tcs) =>
                {
                    //threadPool
                    {
                        {
                            int timeoutMillisecs = Prefs.Instance.NugetOrgTimeoutMillis;

                            using var cts = new CancellationTokenSource(timeoutMillisecs);

                            using var client = new HttpClient();
                            client.DefaultRequestHeaders.Accept.Clear();
                            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue(MIME_JSON));

                            try
                            {
                                var response = await client.GetAsync(NUGET_EP, cts.Token);
                                if (response.IsSuccessStatusCode)
                                {
                                    var json = await response.Content.ReadAsStringAsync();
                                    tcs.SetResult(json);
                                }
                            }
                            catch (Exception exc)
                            {
                                tcs.SetException(exc);
                            }

                            tcs.SetException(new Exception("unhandled error"));
                        }
                    }
                },
                tcs, false);


                // this will throw when thread task is failed
                var json = tcs.Task.ConfigureAwait(false).GetAwaiter().GetResult();
                var result = JsonUtility.FromJson<NugetPayload>(json);

                foundLatestVersion = result.versions?[result.versions.Length - 1];
                prefs.LatestVoidSdkVersion = foundLatestVersion;

                UnityEngine.Debug.Log("[NuGet] latest version found: " + SDK_VOID_NAME_SLASH + foundLatestVersion);

                goto VALIDATE_AND_RETURN;
            }
            catch (Exception exc)
            {
                UnityEngine.Debug.LogError("[NuGet] operation timed out or api v3 is not available: " + exc);
            }

        VALIDATE_AND_RETURN:
            if (string.IsNullOrWhiteSpace(foundLatestVersion))
            {
                prefs.LatestVoidSdkVersion = null;
                return null;
            }
            return foundLatestVersion;
        }


        /*  prefs  ================================================================ */

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
