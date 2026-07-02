using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Localization;
using UnityEngine;

namespace Vapor
{
    public static class LocalizationValidator
    {
        [MenuItem("Vapor/Validate Localization Entries")]
        public static void ValidateLocalizationEntries()
        {
            var entries = new List<(string tableName, string entryKey)>();
            var files = FindAllCSharpFiles();

            foreach (var file in files)
            {
                var extractedEntries = ExtractLocalizationCalls(file);
                entries.AddRange(extractedEntries);
            }

            int created = 0;
            int validated = 0;

            foreach (var (tableName, entryKey) in entries.Distinct())
            {
                if (ValidateOrCreateEntry(tableName, entryKey))
                {
                    created++;
                }

                validated++;
            }

            Debug.Log($"Localization validation complete. Validated {validated} entries, created {created} missing entries.");
            AssetDatabase.SaveAssets();
        }

        private static string[] FindAllCSharpFiles()
        {
            return Directory.GetFiles("Assets", "*.cs", SearchOption.AllDirectories);
        }

        private static List<(string tableName, string entryKey)> ExtractLocalizationCalls(string filePath)
        {
            var results = new List<(string, string)>();
            var content = File.ReadAllText(filePath);

            // Match WithLocalization calls with tuple parameters
            var pattern = @"\.WithLocalization\s*\(\s*\(\s*""([^""]+)""\s*,\s*""([^""]+)""\s*\)\s*,\s*\(\s*""([^""]+)""\s*,\s*""([^""]+)""\s*\)\s*\)";
            var matches = Regex.Matches(content, pattern);



            foreach (Match match in matches)
            {
                // Extract name table and entry
                results.Add((match.Groups[1].Value, match.Groups[2].Value));
                // Extract description table and entry
                results.Add((match.Groups[3].Value, match.Groups[4].Value));
            }

            // Match new LocalizedString(table, key) constructor calls
            var localizedStringPattern = @"new\s+LocalizedString\s*\(\s*""([^""]+)""\s*,\s*""([^""]+)""\s*\)";
            var localizedStringMatches = Regex.Matches(content, localizedStringPattern);

            foreach (Match match in localizedStringMatches)
            {
                results.Add((match.Groups[1].Value, match.Groups[2].Value));
            }

            return results;
        }

        private static bool ValidateOrCreateEntry(string tableName, string entryKey)
        {

            var collection = LocalizationEditorSettings.GetStringTableCollection(tableName);
            if (!collection)
            {
                // Create the Settings folder if it doesn't exist
                var settingsPath = "Assets/Settings";
                if (!Directory.Exists(settingsPath))
                {
                    Directory.CreateDirectory(settingsPath);
                    AssetDatabase.Refresh();
                }

                // Create a new string table collection
                collection = LocalizationEditorSettings.CreateStringTableCollection(tableName, settingsPath);
                EditorUtility.SetDirty(collection);
                AssetDatabase.SaveAssets();
            }

            var sharedTableData = collection.SharedData;
            var entry = sharedTableData.GetEntry(entryKey);

            if (entry == null)
            {
                sharedTableData.AddKey(entryKey);
                EditorUtility.SetDirty(sharedTableData);

                // Find English table and set default value
                var englishTable = collection.StringTables.FirstOrDefault(t => t.LocaleIdentifier.Code == "en");
                if (englishTable)
                {
                    var tableEntry = englishTable.GetEntry(entryKey);
                    if (tableEntry != null)
                    {
                        tableEntry.Value = entryKey;
                        EditorUtility.SetDirty(englishTable);
                    }
                    else
                    {
                        englishTable.AddEntry(entryKey, entryKey);
                        EditorUtility.SetDirty(englishTable);
                    }
                }

                Debug.Log($"Created missing localization entry: Table='{tableName}', Key='{entryKey}'");
                return true;
            }
            else
            {
                // Find English table and set default value
                var englishTable = collection.StringTables.FirstOrDefault(t => t.LocaleIdentifier.Code == "en");
                if (englishTable)
                {
                    var tableEntry = englishTable.GetEntry(entryKey);
                    if (tableEntry != null)
                    {
                        if(tableEntry.Value.EmptyOrNull())
                        {
                            tableEntry.Value = entryKey;
                            EditorUtility.SetDirty(englishTable);
                            Debug.Log($"Updated english localization entry: Table='{tableName}', Key='{entryKey}', Entry='{tableEntry.Value}'");
                        }
                    }
                    else
                    {
                        englishTable.AddEntry(entryKey, entryKey);
                        EditorUtility.SetDirty(englishTable);
                        Debug.Log($"Created english localization entry: Table='{tableName}', Key='{entryKey}', Entry='{entryKey}");
                    }
                }
            }

            return false;
        }
    }
}