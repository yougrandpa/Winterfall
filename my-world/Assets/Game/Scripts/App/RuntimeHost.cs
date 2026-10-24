using System;
using System.IO;

namespace MyAiWorld.App
{
    // Only this boundary needs an engine host. Session, persistence and queries also run in plain C#.
    internal static class RuntimeHost
    {
        internal static string DataPath
        {
            get
            {
#if UNITY_5_3_OR_NEWER
                return UnityEngine.Application.persistentDataPath;
#else
                return Environment.GetEnvironmentVariable("MY_AI_WORLD_DATA_PATH") ??
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MyAiWorld");
#endif
            }
        }

        internal static void Warn(string message)
        {
#if UNITY_5_3_OR_NEWER
            UnityEngine.Debug.LogWarning(message);
#else
            Console.Error.WriteLine(message);
#endif
        }
    }
}
