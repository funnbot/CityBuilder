#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEditor.PackageManager;
using UnityEngine;
using UnityEditor.Callbacks;
using System.Runtime.InteropServices;

public static class GenerateXMLDocumentation
{
    static readonly string[] filtered = new[] { "Assembly-CSharp", "Bee.BeeDriver", "PPv2URPConverters", "Unity.Internal" };

    [InitializeOnLoadMethod]
    static void Init()
    {
        var outputDir = Path.Combine(Application.dataPath, "../Library/ScriptAssemblies"); // dataPath is Assets folder in editor
        outputDir = Path.GetFullPath(outputDir);

        var docDir = Path.Combine(Application.dataPath, "../PackageDocs"); // dataPath is Assets folder in editor
        docDir = Path.GetFullPath(docDir);

        foreach (var file in Directory.EnumerateFiles(docDir))
        {
            var outFile = Path.Combine(outputDir, $"{Path.GetFileName(file)}");
            File.Copy(file, outFile, overwrite: true);
        }
    }
}
#endif
