/** Legacy-to-SDK-Style .csproj Converter for Unity
 ** (c) 2024 https://github.com/sator-imaging
 ** Licensed under the MIT License
 */

using System;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

#nullable enable

namespace SatorImaging.Csproj.Sdk
{
    partial class UnityCsProjectConverter
    {
        internal static class EditorExtension
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


            [InitializeOnLoadMethod]
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

    }
}
