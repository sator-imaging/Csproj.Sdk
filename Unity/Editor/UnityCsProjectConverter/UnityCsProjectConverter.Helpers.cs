/** Legacy-to-SDK-Style .csproj Converter for Unity
 ** (c) 2024 https://github.com/sator-imaging
 ** Licensed under the MIT License
 */

using System;
using System.IO;
using System.Text;
using Unity.CodeEditor;
using UnityEditor;
using UnityEngine;

#nullable enable

namespace SatorImaging.Csproj.Sdk
{
    partial class UnityCsProjectConverter
    {
        /*  file  ================================================================ */

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

    }
}
