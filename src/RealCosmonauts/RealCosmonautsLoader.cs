using System;
using System.Collections;
using System.Reflection;
using UnityEngine;

namespace RealCosmonauts
{
    /// <summary>
    /// Entry point. Runs once, when the main menu first loads: by then the game database
    /// and every part prefab (including the EVA Kerbals) have been loaded.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.MainMenu, true)]
    public class RealCosmonautsLoader : MonoBehaviour
    {
        private void Start()
        {
            Log.Info($"Version {Assembly.GetExecutingAssembly().GetName().Version} starting.");
            Settings.Load();

            if (Settings.DumpKerbalModels)
                SafeDump("MainMenu");

            // Dump first, so the report shows the stock models.
            if (Settings.ReplaceModels)
                HeadSwapper.PatchAllPrefabs();
        }

        internal static void SafeDump(string sceneLabel)
        {
            try
            {
                KerbalModelDumper.DumpAll(sceneLabel, Settings.ExportMeshes);
            }
            catch (Exception e)
            {
                Log.Error($"Kerbal model dump failed: {e}");
            }
        }
    }

    /// <summary>
    /// IVA Kerbals may only exist once a crewed vessel is loaded, so the dump runs again
    /// the first time the flight scene starts.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.Flight, true)]
    public class FlightDumpAddon : MonoBehaviour
    {
        private IEnumerator Start()
        {
            if (!Settings.DumpKerbalModels)
                yield break;

            // Give the vessel time to load and spawn its IVA crew.
            yield return new WaitForSeconds(5f);
            RealCosmonautsLoader.SafeDump("Flight");
        }
    }
}
