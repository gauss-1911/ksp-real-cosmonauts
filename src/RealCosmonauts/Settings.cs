namespace RealCosmonauts
{
    /// <summary>
    /// Values read from the REAL_COSMONAUTS_SETTINGS node
    /// (GameData/RealCosmonauts/Settings.cfg). Missing keys keep their defaults.
    /// </summary>
    internal static class Settings
    {
        public const string NodeName = "REAL_COSMONAUTS_SETTINGS";

        /// <summary>Write a report of every Kerbal model to PluginData when the main menu loads.</summary>
        public static bool DumpKerbalModels;

        /// <summary>Also export each Kerbal mesh as an .obj file next to the report.</summary>
        public static bool ExportMeshes;

        /// <summary>Master switch for replacing Kerbal meshes with the mod's models.</summary>
        public static bool ReplaceModels = true;

        public static void Load()
        {
            ConfigNode[] nodes = GameDatabase.Instance.GetConfigNodes(NodeName);
            if (nodes == null || nodes.Length == 0)
            {
                Log.Warning($"No {NodeName} node found, using defaults.");
                return;
            }

            // Later nodes (e.g. from a user's own patch) override earlier ones.
            foreach (ConfigNode node in nodes)
            {
                node.TryGetValue("dumpKerbalModels", ref DumpKerbalModels);
                node.TryGetValue("exportMeshes", ref ExportMeshes);
                node.TryGetValue("replaceModels", ref ReplaceModels);
            }

            Log.Info($"Settings: dumpKerbalModels={DumpKerbalModels}, exportMeshes={ExportMeshes}, replaceModels={ReplaceModels}");
        }
    }
}
