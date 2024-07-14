/** Legacy-to-SDK-Style .csproj Converter for Unity
 ** (c) 2024 https://github.com/sator-imaging
 ** Licensed under the MIT License
 */

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
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
        public static string CustomSdkNameAndVersion { get; set; }
            = nameof(UnityCsProjectConverter) + "." + nameof(CustomSdkNameAndVersion) + " is not set";

        /// <inheritdoc cref="CustomSdkNameAndVersion"/>
        public readonly static string SDK_VOID_NAME_SLASH = "Csproj.Sdk.Void/";

        private static string? _latestVoidSdkNameAndVersion;
        public static string LatestVoidSdkNameAndVersion
            => _latestVoidSdkNameAndVersion ??= SDK_VOID_NAME_SLASH + (GetVoidSdkVersionFromNugetOrgThread() ?? "1.0.1");

        public override int GetPostprocessOrder() => int.MinValue + 310;  // must be called first!!
                                                                          // don't use actual minimum value, it could be casted to wrong float inside unity!!

        /*  nuget api  ================================================================ */

        readonly static string NUGET_EP = @"https://api.nuget.org/v3-flatcontainer/csproj.sdk.void/index.json";
        readonly static string MIME_JSON = @"application/json";
        readonly static string PREF_LAST_FETCH_TIME = nameof(SatorImaging) + nameof(Csproj) + nameof(UnityCsProjectConverter) + nameof(PREF_LAST_FETCH_TIME);
        readonly static DateTime FETCH_TIME_EPOCH = new(2024, 1, 1);  // overflows after 68 years!

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

                foundLatestVersion = prefs.latestVoidSdkVersion;
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
                            int timeoutMillisecs = Prefs.Instance.nugetOrgTimeoutMillis;

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
                prefs.latestVoidSdkVersion = foundLatestVersion;

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
                prefs.latestVoidSdkVersion = null;
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
            public string? latestVoidSdkVersion;
            public int nugetOrgTimeoutMillis = 5000;

            // menus
            [SerializeField] bool enableGenerator = true;
            [SerializeField] bool disableOnBuild = false;
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

            public bool EnableGenerator
            {
                get => enableGenerator;
                set
                {
                    if (enableGenerator == value)
                        return;

                    enableGenerator = value;

                    Save();
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

                    Save();
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

                    Save();
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

                    Save();
                }
            }

        }


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
            if (!Prefs.Instance.EnableGenerator)
                return content;


            //// TODO: double clicking .cs file in Unity always call this method for all assemblies (.asmdef)
            ////       need to make it more efficient and faster
            //UnityEngine.Debug.Log(nameof(UnityCsProjectConverter) + ": " + nameof(OnGeneratedCSProject) + ": " + path);


            CreateFileIfNotExists(UNITY_PROJ_DIR_NAME + EXT_SHARED + EXT_PROPS);
            CreateFileIfNotExists(UNITY_PROJ_DIR_NAME + EXT_EDITOR + EXT_PROPS);
            CreateFileIfNotExists(UNITY_PROJ_DIR_NAME + EXT_SHARED + EXT_TARGETS);
            CreateFileIfNotExists(UNITY_PROJ_DIR_NAME + EXT_EDITOR + EXT_TARGETS);

            var xml = new XmlDocument();
            xml.LoadXml(content);


            const string TAG_PROJECT = "Project";

            XmlNode? root = null;
            foreach (var candidate in xml.GetElementsByTagName(TAG_PROJECT).Cast<XmlNode>())
            {
                root = candidate;
                break;
            }
            if (root == null)
            {
                UnityEngine.Debug.LogError("unable to take .csproj node: " + TAG_PROJECT);
                return content;
            }


            //sdk!!
            const string ATTR_SDK = "Sdk";
            //const string ATTR_TOOLS_VER = "ToolsVersion";
            //const string VALUE_CURRENT = "Current";

            if (Prefs.Instance.EnableSdkStyle)
            {
                if (!root.Attributes.Cast<XmlAttribute>().Any(static x =>
                {
                    return x.Name.Equals(ATTR_SDK, StringComparison.OrdinalIgnoreCase)
                        //|| (x.Name.Equals(ATTR_TOOLS_VER, StringComparison.OrdinalIgnoreCase) && x.Value.Equals(VALUE_CURRENT, StringComparison.OrdinalIgnoreCase))
                        ;
                }))
                {
                    while (root.Attributes.Count > 0)
                    {
                        root.Attributes.RemoveAt(0);
                    }

                    var sdkAttr = xml.CreateAttribute(ATTR_SDK);
                    sdkAttr.Value = Prefs.Instance.UseVoidSdk ? LatestVoidSdkNameAndVersion : CustomSdkNameAndVersion;
                    root.Attributes.Append(sdkAttr);
                }
            }


            const string TAG_PROPERTY_GROUP = "PropertyGroup";

            XmlNode? propGroup = null;
            foreach (var candidate in root.ChildNodes.Cast<XmlNode>())
            {
                if (candidate.Name != TAG_PROPERTY_GROUP)
                    continue;

                propGroup = candidate;
                break;
            }
            if (propGroup == null)
            {
                UnityEngine.Debug.LogError("unable to take .csproj node: " + TAG_PROPERTY_GROUP);
                return content;
            }


            // Custom .props/.targets!!
            const string TAG_IMPORT = "Import";
            var fileExts = new string[] { EXT_SHARED, EXT_EDITOR };

            /* =      .props      = */
            // reversed order!! later appears earlier in xml
            root.PrependChild(CreateCommentNode(xml, root, string.Empty));

            foreach (var ext in fileExts.Reverse())  // reversed order!!
            {
                if (_generateForBuild && ext == EXT_EDITOR)
                    continue;

                var node = xml.CreateNode(XmlNodeType.Element, TAG_IMPORT, root.NamespaceURI);
                var attr = xml.CreateAttribute(TAG_PROJECT, string.Empty);
                attr.Value = UNITY_PROJ_DIR_NAME + ext + EXT_PROPS;
                node.Attributes.Append(attr);

                root.PrependChild(node);
            }

            root.PrependChild(CreateCommentNode(xml, root, nameof(UnityCsProjectConverter)));
            root.PrependChild(CreateCommentNode(xml, root, string.Empty));


            /* =      .targets      = */
            root.AppendChild(CreateCommentNode(xml, root, string.Empty));
            root.AppendChild(CreateCommentNode(xml, root, nameof(UnityCsProjectConverter)));

            foreach (var ext in fileExts)
            {
                if (_generateForBuild && ext == EXT_EDITOR)
                    continue;

                var node = xml.CreateNode(XmlNodeType.Element, TAG_IMPORT, root.NamespaceURI);
                var attr = xml.CreateAttribute(TAG_PROJECT, string.Empty);
                attr.Value = UNITY_PROJ_DIR_NAME + ext + EXT_TARGETS;
                node.Attributes.Append(attr);

                root.AppendChild(node);
            }

            root.AppendChild(CreateCommentNode(xml, root, string.Empty));


            //version!?
            const string TAG_GENERATOR = "UnityProjectGenerator";
            var generatorElem = xml.GetElementsByTagName(TAG_GENERATOR);
            if (generatorElem.Count == 1)
            {
                generatorElem[0].InnerText += "-" + nameof(UnityCsProjectConverter);
            }


            //write!!
            using var stream = new StringWriter();
            using var writer = XmlWriter.Create(stream, new XmlWriterSettings()
            {
                Encoding = Encoding.UTF8,
                Indent = true,
            });

            xml.WriteContentTo(writer);
            writer.Flush();


            // remove namespaces!! no way to achieve by using XmlDocument interface!!
            if (Prefs.Instance.EnableSdkStyle)
            {
                var sb = stream.GetStringBuilder();
                sb.Replace(" xmlns=\"" + XML_NS + "\"", string.Empty);
            }


            content = stream.ToString();
            return content;
        }


        /*  helper  ================================================================ */

        static void CreateFileIfNotExists(string fileName)
        {
            if (File.Exists(fileName))
                return;

            File.WriteAllText(fileName,
$@"<Project xmlns=""{XML_NS}"">
    <PropertyGroup>
    </PropertyGroup>
</Project>
",
                Encoding.UTF8);
        }


        static XmlNode CreateCommentNode(XmlDocument xml, XmlNode root, string comment)
        {
            var result = xml.CreateNode(XmlNodeType.Comment, nameof(UnityCsProjectConverter), root.NamespaceURI);
            result.InnerText = comment;
            return result;
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
            var generatorList = new List<object>();
            foreach (var t in TypeCache.GetTypesDerivedFrom<object>())
            {
                if (!t.GetInterfaces().Any(x => x.Name == "IGenerator"))  // vs and vscode plugin use same interface name
                    continue;

                var generator = Activator.CreateInstance(t)
                    ?? throw new NullReferenceException(t.ToString());

                generatorList.Add(generator);
            }

            if (generatorList.Count == 0)
            {
                return;
            }
            else if (generatorList.Count > 1)
            {
                UnityEngine.Debug.LogError("multiple .csproj generators found: " + string.Join(", ", generatorList));
                return;
            }

            MethodInfo Sync;
            Sync = generatorList[0].GetType().GetMethod(nameof(Sync))
                ?? throw new NullReferenceException(nameof(Sync) + " method not found: " + generatorList[0]);

            Sync.Invoke(generatorList[0], Array.Empty<object>());

            // for commandline build
            Console.WriteLine(nameof(UnityCsProjectConverter) + ": " + nameof(RegenerateProjectFiles));
            UnityEngine.Debug.Log(nameof(UnityCsProjectConverter) + ": " + nameof(RegenerateProjectFiles));
        }


        // need to regenerate before building app to apply generator settings
        public sealed class BuildPreprocessor : IPreprocessBuildWithReport
        {
            public int callbackOrder => int.MinValue + 310;  // must be called first!!
                                                             // don't use actual minimum value, it could be casted to wrong float inside unity!!

            public void OnPreprocessBuild(BuildReport report)
            {
                if (!Prefs.Instance.EnableGenerator)
                    return;

                // for commandline build
                Console.WriteLine(nameof(UnityCsProjectConverter) + ": " + nameof(OnPreprocessBuild));

                // NOTE: DisableOnBuild doesn't exit here
                //       it needs to run to revert .csproj files back to original state!!
                bool restoreEnableGenerator = Prefs.Instance.EnableGenerator;
                if (Prefs.Instance.DisableOnBuild)
                {
                    Prefs.Instance.EnableGenerator = false;
                }


                // NOTE: it seems that IPostprocessBuild won't run in batch mode when error
                //       register on app quit action for workaround
                RegisterRegenerateActionOnlyOnBatchBuild();


                _generateForBuild = true;
                try
                {
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
            public int callbackOrder => int.MaxValue - 310;  // must be called at last!!
                                                             // don't use actual max value, it could be casted to wrong float inside unity!!

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
                // need to save to .json before recompile in Unity editor
                CompilationPipeline.compilationStarted += _ => Prefs.Instance.Save();

                // must delay to wait menu item creation
                EditorApplication.delayCall += () =>
                {
                    var prefs = Prefs.Instance;
                    Menu.SetChecked(MENU_ENABLE_GENERATOR, prefs.EnableGenerator);
                    Menu.SetChecked(MENU_DISABLE_ON_BUILD, prefs.DisableOnBuild);
                    Menu.SetChecked(MENU_ENABLE_SDK_STYLE, prefs.EnableSdkStyle);
                    Menu.SetChecked(MENU_USE_VOID_SDK, prefs.UseVoidSdk);
                };
            }


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

    }
}
