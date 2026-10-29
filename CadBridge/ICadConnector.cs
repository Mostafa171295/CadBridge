using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace CadBridge
{
    public enum LogLevel { Info, Ok, Error }

    // Thrown when the user's script does not compile (used by connectors that compile themselves)
    public class ScriptCompileException : Exception
    {
        public ScriptCompileException(string message) : base(message) { }
    }

    // One connector per CAD program (SolidWorks, AutoCAD, ...)
    public interface ICadConnector
    {
        string Name { get; }
        bool IsConnected { get; }
        bool Connect(out string message);
        Task RunScriptAsync(string code, string outDir, Action<string> log);
        void BuildAssembly(string outDir, string assemblyName, Action<string> log);
    }

    // Implemented by connectors that need something installed inside the CAD program (Revit)
    public interface IAddinInstaller
    {
        // returns a success message; throws an Exception with a readable message on failure
        string InstallAddin();
    }

    // Implemented by connectors that can read lists from the CAD program (Revit): levels, materials, types...
    // Used for the drop-down boxes made by  {{Name=@levels}}.  Keys of the result = normalized source names.
    public interface IChoiceProvider
    {
        Task<Dictionary<string, List<string>>> GetChoicesAsync(IList<string> sources);
    }
    // Implemented by connectors that read family files (.rfa) from folders on this PC (Revit).
    // Every file list (@columnFiles, @beamFiles, @footingFiles) can point to a folder chosen by the user
    // (the "Folder..." button next to the list). The choice is remembered for each kind of object.
    public interface IFamilyFolderProvider
    {
        bool IsFileSource(string source);                      // e.g. "@columnFiles" or "columnfiles"
        string GetCustomFolder(string source);                 // "" = the default library folders are used
        void SetCustomFolder(string source, string folder);    // null or "" = back to the default folders
    }
}
