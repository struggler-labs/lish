using System;
using System.IO;
using System.Linq;
using System.Text;
using Lish.Core;
using Xunit;

namespace Lish.Tests.Core
{
    [Collection(DataDirCollection.Name)]
    public class ProfileStoreTests : IDisposable
    {
        private readonly TestDataDir _data = new TestDataDir();

        public void Dispose() => _data.Dispose();

        private static void WriteHosts(string json) =>
            File.WriteAllText(Paths.HostsFile, json, new UTF8Encoding(false));

        [Fact]
        public void Paths_UseTheConfiguredDataFolder()
        {
            Assert.Equal(_data.Dir, Paths.DataDir);
            Assert.Equal(_data.File("hosts.json"), Paths.HostsFile);
            Assert.Equal(_data.File("known_hosts"), Paths.KnownHostsFile);
        }

        [Fact]
        public void Load_WithoutFiles_IsEmpty()
        {
            var store = ProfileStore.Load();
            Assert.Empty(store.All);
            Assert.Empty(store.Folders);
        }

        [Fact]
        public void SaveAndLoad_RoundTripsProfiles()
        {
            var store = ProfileStore.Empty();
            store.Upsert(new HostProfile
            {
                Name = "web", Group = " prod / web ", Host = "10.0.0.1", Port = 2222, User = "deploy",
                KeyFile = @"C:\keys\id_ed25519", Encoding = "cp949", Term = "vt100", KeepAlive = 0, Timeout = 30, Note = "메모",
            });
            store.Upsert(new HostProfile { Name = "Alpha", Host = "alpha" });
            store.Save();

            var loaded = ProfileStore.Load();
            Assert.Equal(new[] { "Alpha", "web" }, loaded.All.Select(p => p.Name).ToArray());   // sorted by name
            var web = loaded.Find("WEB");
            Assert.Equal("prod/web", web.Group);
            Assert.Equal("10.0.0.1", web.Host);
            Assert.Equal(2222, web.Port);
            Assert.Equal("deploy", web.User);
            Assert.Equal(@"C:\keys\id_ed25519", web.KeyFile);
            Assert.Equal("cp949", web.Encoding);
            Assert.Equal("vt100", web.Term);
            Assert.Equal(0, web.KeepAlive);
            Assert.Equal(30, web.Timeout);
            Assert.Equal("메모", web.Note);
            Assert.Equal(new[] { "prod", "prod/web" }, loaded.Folders.ToArray());
            Assert.Null(loaded.Find("Alpha").Group);
        }

        [Fact]
        public void Load_FillsDefaultsForMissingFields()
        {
            WriteHosts("[{\"Name\":\"x\",\"Host\":\"h\"}]");
            var p = ProfileStore.Load().Find("x");
            Assert.Equal(22, p.Port);
            Assert.Equal(30, p.KeepAlive);
            Assert.Equal(15, p.Timeout);
        }

        [Fact]
        public void Load_AcceptsUtf8Bom_AndIgnoresUnknownAndNamelessEntries()
        {
            var json = "[{\"Name\":\"ok\",\"Host\":\"h\",\"JumpHost\":\"removed-feature\"},{\"Host\":\"nameless\"},null]";
            File.WriteAllText(Paths.HostsFile, json, new UTF8Encoding(true));
            var store = ProfileStore.Load();
            Assert.Equal(new[] { "ok" }, store.All.Select(p => p.Name).ToArray());
        }

        [Fact]
        public void Load_WipesLegacySavedPasswordFromDisk()
        {
            WriteHosts("[{\"Name\":\"old\",\"Host\":\"h\",\"SavedPassword\":\"AQAAANCMnd8BFdERjHoAwE\"}," +
                       "{\"Host\":\"nameless\",\"SavedPassword\":\"secret2\"}]");

            var store = ProfileStore.Load();

            Assert.Equal("h", store.Find("old").Host);
            var onDisk = File.ReadAllText(Paths.HostsFile);
            Assert.DoesNotContain("SavedPassword", onDisk);
            Assert.DoesNotContain("AQAAANCMnd8BFdERjHoAwE", onDisk);
            Assert.DoesNotContain("secret2", onDisk);
            Assert.Contains("\"old\"", onDisk);
            // Nothing left to wipe on a second load
            Assert.False(ProfileStore.Load().Find("old").ClearLegacyPassword());
        }

        [Fact]
        public void Load_WithoutLegacyPassword_DoesNotRewriteFile()
        {
            const string json = "[{\"Name\":\"x\",\"Host\":\"h\"}]";
            WriteHosts(json);
            ProfileStore.Load();
            Assert.Equal(json, File.ReadAllText(Paths.HostsFile));
        }

        [Fact]
        public void Load_CorruptFile_IsBackedUpAndStartsEmpty()
        {
            WriteHosts("{ this is not json");
            var store = ProfileStore.Load();

            Assert.Empty(store.All);
            Assert.NotNull(ProfileStore.LoadError);
            var backups = Directory.GetFiles(_data.Dir, "hosts.json.*.bak");
            Assert.Single(backups);
            Assert.Equal("{ this is not json", File.ReadAllText(backups[0]));
        }

        [Fact]
        public void EmptyFolder_SurvivesSaveAndLoad()
        {
            var store = ProfileStore.Empty();
            Assert.Equal("empty/child", store.CreateFolder(store.CreateFolder(null, "empty"), " child "));
            store.Save();
            Assert.False(File.Exists(Paths.HostsFile + ".tmp"));

            var loaded = ProfileStore.Load();
            Assert.True(loaded.FolderExists("EMPTY/child"));
            Assert.Equal(new[] { "empty", "empty/child" }, loaded.Folders.ToArray());
        }

        [Fact]
        public void FolderOperations_MoveSessionsWithTheFolder()
        {
            var store = ProfileStore.Empty();
            store.Upsert(new HostProfile { Name = "api", Host = "h1", Group = "prod/web" });
            store.Upsert(new HostProfile { Name = "db", Host = "h2", Group = "prod" });
            store.Upsert(new HostProfile { Name = "other", Host = "h3", Group = "dev" });

            Assert.Throws<ArgumentException>(() => store.CreateFolder(null, "prod"));
            Assert.Throws<ArgumentException>(() => store.CreateFolder(null, "a/b"));
            Assert.Throws<ArgumentException>(() => store.MoveFolder("prod", "prod/web"));

            Assert.Equal("live", store.RenameFolder("prod", "live"));
            Assert.Equal("live/web", store.Find("api").Group);
            Assert.Equal("live", store.Find("db").Group);
            Assert.False(store.FolderExists("prod"));

            Assert.Equal("dev/web", store.MoveFolder("live/web", "dev"));
            Assert.Equal("dev/web", store.Find("api").Group);
            Assert.Equal(new[] { "api", "other" }, store.SessionsUnder("dev").Select(p => p.Name).OrderBy(n => n).ToArray());

            store.DeleteFolder("dev");
            Assert.Equal(new[] { "db" }, store.All.Select(p => p.Name).ToArray());
            Assert.False(store.FolderExists("dev/web"));
        }

        [Fact]
        public void Upsert_ReplacesByNameCaseInsensitively_AndUniqueNameAvoidsClashes()
        {
            var store = ProfileStore.Empty();
            store.Upsert(new HostProfile { Name = "srv", Host = "a" });
            store.Upsert(new HostProfile { Name = "SRV", Host = "b" });
            Assert.Single(store.All);
            Assert.Equal("b", store.Find("srv").Host);

            Assert.Equal("srv (2)", store.UniqueName("srv"));
            store.Upsert(new HostProfile { Name = "srv (2)", Host = "c" });
            Assert.Equal("srv (3)", store.UniqueName("srv"));
            Assert.Equal("new", store.UniqueName("new"));

            Assert.True(store.Remove("Srv"));
            Assert.False(store.Remove("srv"));
        }

        [Fact]
        public void AppSettings_DefaultsAndRoundTrip()
        {
            var missing = AppSettings.Load();
            Assert.Equal(10.5f, missing.FontSize);
            Assert.True(missing.SftpFollowTerminal);

            File.WriteAllText(Paths.SettingsFile, "{}");
            var empty = AppSettings.Load();
            Assert.Equal(10.5f, empty.FontSize);
            Assert.Equal(5000, empty.Scrollback);
            Assert.True(empty.CopyOnSelect);
            Assert.True(empty.ConfirmMultilinePaste);
            Assert.Equal(-1, empty.WindowX);
            Assert.Equal(1280, empty.WindowWidth);
            Assert.True(empty.SftpFollowTerminal);

            empty.FontSize = 13f;
            empty.Scrollback = 100;
            empty.SftpFollowTerminal = false;
            empty.FontName = "D2Coding";
            empty.Save();
            var loaded = AppSettings.Load();
            Assert.Equal(13f, loaded.FontSize);
            Assert.Equal(100, loaded.Scrollback);
            Assert.False(loaded.SftpFollowTerminal);
            Assert.Equal("D2Coding", loaded.FontName);
        }

        [Fact]
        public void AppSettings_CorruptFile_FallsBackToDefaults()
        {
            File.WriteAllText(Paths.SettingsFile, "not json");
            Assert.Equal(5000, AppSettings.Load().Scrollback);
        }
    }
}
