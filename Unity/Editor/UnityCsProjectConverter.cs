/** Legacy-to-SDK-Style .csproj Converter for Unity
 ** (c) 2024 https://github.com/sator-imaging
 ** Licensed under the MIT License
 */

using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

#nullable enable

namespace SatorImaging.Csproj.Sdk
{
    /// <summary>
    /// Migrate legacy style `.csproj` files to newer sdk style format.
    /// </summary>
    public sealed partial class UnityCsProjectConverter : AssetPostprocessor
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
            => _latestVoidSdkNameWithVersion ??= SDK_VOID_NAME_SLASH + (NugetClient.GetVoidSdkVersionFromNugetOrgThread() ?? SDK_VOID_DEFAULT_VERSION);

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


        readonly static StringBuilder cache_sb = new(capacity: 65536);  // usual .csproj file size is around 60KiB
        readonly static XDocumentWriter cache_writer = new(cache_sb);

    }

}
