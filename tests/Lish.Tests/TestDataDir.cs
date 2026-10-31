using System;
using System.IO;
using System.Reflection;
using Lish.Core;
using Xunit;

namespace Lish.Tests
{
    /// <summary>
    /// Points Lish.Core.Paths.DataDir at a fresh temporary folder for the duration of a test,
    /// so tests never read or write the real %APPDATA%\Lish (or LISH_HOME) data.
    /// Paths caches the folder in a private static field, so it is swapped through reflection.
    /// </summary>
    internal sealed class TestDataDir : IDisposable
    {
        private static readonly FieldInfo DataDirField =
            typeof(Paths).GetField("_dataDir", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Paths._dataDir not found");

        private readonly object _previous;

        public string Dir { get; }

        public TestDataDir()
        {
            Dir = Path.Combine(Path.GetTempPath(), "lish-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Dir);
            _previous = DataDirField.GetValue(null);
            DataDirField.SetValue(null, Dir);
        }

        public string File(string name) => Path.Combine(Dir, name);

        public void Dispose()
        {
            DataDirField.SetValue(null, _previous);
            try { Directory.Delete(Dir, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    /// <summary>Tests that swap the shared static data folder must not run in parallel with each other.</summary>
    [CollectionDefinition(Name, DisableParallelization = true)]
    public sealed class DataDirCollection
    {
        public const string Name = "DataDir";
    }
}
