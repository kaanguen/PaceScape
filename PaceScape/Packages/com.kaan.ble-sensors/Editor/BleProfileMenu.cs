using UnityEditor;
using UnityEngine;

namespace Kaan.BleSensors.Editor
{
    public static class BleProfileMenu
    {
        [MenuItem("Assets/Create/BLE/Laufband FTMS")]
        static void CreateTreadmillProfile()
        {
            var path = EditorUtility.SaveFilePanelInProject("FTMS-Profil speichern", "TreadmillFTMS", "asset", "Profil im Assets-Ordner speichern.");
            if (string.IsNullOrEmpty(path)) return;
            AssetDatabase.CreateAsset(TreadmillProfileDefaults.Create(), path);
            AssetDatabase.SaveAssets();
            Selection.activeObject = AssetDatabase.LoadAssetAtPath<BleDeviceProfile>(path);
        }
        [MenuItem("Assets/Create/BLE/Cosinuss-Standardprofil")]
        static void CreateCosinussProfile()
        {
            var path = EditorUtility.SaveFilePanelInProject("BLE-Geräteprofil speichern", "Cosinuss", "asset", "Profil im Assets-Ordner speichern.");
            if (string.IsNullOrEmpty(path)) return;
            var profile = CosinussProfileDefaults.Create();
            AssetDatabase.CreateAsset(profile, path);
            AssetDatabase.SaveAssets();
            Selection.activeObject = profile;
        }
    }
}
