using System;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace RemoteNET.Access
{
    /// <summary>
    /// Configuration for establishing a connection to a remote application
    /// </summary>
    public class ConnectionConfig
    {
        public ConnectionStrategy Strategy { get; set; } = ConnectionStrategy.Unknown;
        public string TargetDllToProxy { get; set; } = null;

        public static ConnectionConfig Default => new ConnectionConfig();
        public static ConnectionConfig DllInjection => new ConnectionConfig { Strategy = ConnectionStrategy.DllInjection };
        public static ConnectionConfig DllHijack(string targetDll = null) => new ConnectionConfig 
        { 
            Strategy = ConnectionStrategy.DllHijack, 
            TargetDllToProxy = targetDll 
        };

        /// <summary>
        /// Creates a DLL hijacking configuration using a specific module from the target process
        /// </summary>
        /// <param name="targetProcess">The process to scan for modules</param>
        /// <param name="moduleName">Name of the module to hijack (e.g. "kernel32.dll")</param>
        /// <returns>Configuration for DLL hijacking with the specified module</returns>
        public static ConnectionConfig DllHijackByModuleName(Process targetProcess, string moduleName)
        {
            try
            {
                var modules = SharpDllProxy.VictimModuleFinder.Search(targetProcess);
                var targetModule = modules.FirstOrDefault(m => 
                    Path.GetFileName(m.OriginalFilePath).Equals(moduleName, StringComparison.OrdinalIgnoreCase));
                
                if (targetModule == null)
                    throw new ArgumentException($"Module '{moduleName}' not found in target process.");
                
                return DllHijack(targetModule.OriginalFilePath);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to configure DLL hijacking for module '{moduleName}': {ex.Message}", ex);
            }
        }
    }
}