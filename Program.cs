using Cyan.Manifest;
using Cyan.Utility;
using Microsoft.Data.Sqlite;
using System.Text;

internal class Program
{
    const string ASSET_MANIFEST = "manifest";
    const string PLATFORM_MANIFEST = "manifest2";
    const string ROOT_MANIFEST = "manifest3";

    const string MANIFEST_NAME_PREFIX = "//";
    
    static readonly byte[] MANIFEST_NAME_PREFIX_BYTES = Encoding.UTF8.GetBytes(MANIFEST_NAME_PREFIX);

    static async Task<List<ManifestEntry>> LoadManifest(string url, bool isManifestCategory, bool isCompressed = true)
    {
        List<ManifestEntry> entries = [];
        using var httpClient = new HttpClient();
        try
        {
            var stream = await httpClient.GetStreamAsync(url);

            using BSVReader.IBSVReader reader = BSVReader.Init(stream, BSVReader.ReadMode.Memory, isCompressed);
            BSVReader.ILineParser<ManifestEntry>? bsvParser = ManifestEntry.GetBsvParser(isManifestCategory ? ManifestEntry.Format.Simplified : ManifestEntry.Format.Full, reader);
            if (bsvParser == null)
            {
                return [];
            }
            ManifestEntry item = default;
            while (reader.ReadLine(bsvParser, ref item))
            {
                item.hname = ManifestEntry.CalcHNameString(item.checksum, item.size, item.name);
                if (isManifestCategory)
                {
                    item.name = [.. MANIFEST_NAME_PREFIX_BYTES, .. item.name];
                }
                entries.Add(item);
            }
        }
        catch (Exception e)
        {
            Console.WriteLine($"Failed to fetch data: {e.Message}");
        }

        return entries;
    }

    static SqliteConnection InitDatabase()
    {
        var connection = new SqliteConnection($"Data Source=meta");
        connection.Open();
        return connection;
    }

    static void CreateTables(SqliteConnection connection)
    {
        var command = connection.CreateCommand();
        command.CommandText = """DROP TABLE IF EXISTS a""";
        command.ExecuteNonQuery();
        command.CommandText = """
        CREATE TABLE IF NOT EXISTS `a` (
                         `i` INTEGER PRIMARY KEY,
                         `n` TEXT NOT NULL,
                         `d` TEXT, 
                         `g` INTEGER(4) NOT NULL,
                         `l` INTEGER(8) NOT NULL,
                         `c` INTEGER(8) NOT NULL,
                         `h` TEXT NOT NULL,
                         `m` TEXT NOT NULL,
                         `k` INTEGER(1) NOT NULL,
                         `s` INTEGER(1) NOT NULL,
                         `p` INTEGER(4) NOT NULL DEFAULT 0,
                         `e` INTEGER(8) NOT NULL DEFAULT 0)
        """;
        command.ExecuteNonQuery();
        command.CommandText = """CREATE INDEX IF NOT EXISTS `a1` ON `a`(`s`)""";
        command.ExecuteNonQuery();
        command.CommandText = """CREATE INDEX IF NOT EXISTS `a0` ON `a`(`n`, `s`)""";
        command.ExecuteNonQuery();
        command.CommandText = """CREATE INDEX IF NOT EXISTS `a2` ON `a`(`m`, `s`)""";
        command.ExecuteNonQuery();
        command.CommandText = """CREATE INDEX IF NOT EXISTS `a3` ON `a`(`g`, 's')""";
        command.ExecuteNonQuery();
        command.CommandText = """CREATE INDEX IF NOT EXISTS `a4` ON `a`(`p`)""";
        command.ExecuteNonQuery();
        command.CommandText = """CREATE INDEX IF NOT EXISTS `a5` ON `a`(`n`, `i`)""";
        command.ExecuteNonQuery();
    }

    static void InsertRow(SqliteConnection connection, byte[] category, int kind, in ManifestEntry entry, int state)
    {
        var command = connection.CreateCommand();
        command.CommandText = """INSERT INTO `a` (`n`,`d`,`g`,`l`,`c`,`h`,`m`,`k`,`s`,`p`,`e`) VALUES($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11)""";
        command.Parameters.AddWithValue("$1", entry.name);
        command.Parameters.AddWithValue("$2", entry.deps ?? []);
        command.Parameters.AddWithValue("$3", (int)entry.group);
        command.Parameters.AddWithValue("$4", (long)entry.size);
        command.Parameters.AddWithValue("$5", (long)entry.checksum);
        command.Parameters.AddWithValue("$6", entry.hname);
        command.Parameters.AddWithValue("$7", category);
        command.Parameters.AddWithValue("$8", kind);
        command.Parameters.AddWithValue("$9", state);
        command.Parameters.AddWithValue("$10", (int)entry.priority);
        command.Parameters.AddWithValue("$11", entry.key);
        command.ExecuteNonQuery();
    }

    static void ParseQualifiedVersion(string qualifiedResVer, out string resVer, out ulong size, out ulong checksum)
    {
        string[] array = qualifiedResVer.Split([':']);
        resVer = qualifiedResVer;
        size = 0UL;
        checksum = 0UL;
        if (array.Length > 1)
        {
            resVer = array[0];
            byte[] array2 = Convert.FromBase64String(array[1]);
            int num = 0;
            size = BSVReader.ReadVLQ(array2, ref num, 2);
            checksum = BSVReader.ReadUNum(array2, ref num, 8);
        }
    }

    static async Task<int> Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("Usage: ManifestReader.exe <URL> <Resource version> <Platform>");
            return 1;
        }

        if (!Uri.TryCreate(args[0], new UriCreationOptions(), out _))
        {
            Console.Error.WriteLine("Invaild URL");
            return 1;
        }

        var baseUrl = args[0];
        var resourceVersion = args[1];
        ParseQualifiedVersion(resourceVersion, out string resVer, out ulong size, out ulong checksum);

        var manifestUrl = $"{baseUrl}/resources/Manifest";

        var platform = "Windows";

        if (args.Length > 2)
        {
            platform = args[2];
        }

        var rootUrl = $"{baseUrl}/{Uri.EscapeDataString(resVer)}/manifests/manifestdat/root.manifest.bsv.lz4";

        var rootEntry = new ManifestEntry
        {
            name = Encoding.UTF8.GetBytes(MANIFEST_NAME_PREFIX + "root"),
            hname = ManifestEntry.CalcHNameString(checksum, size, Encoding.UTF8.GetBytes("root")),
            checksum = checksum,
            size = size
        };

        if (checksum > 0 || size > 0)
        {
            rootUrl = $"{manifestUrl}/{rootEntry.hname[..2]}/{rootEntry.hname}";
        }

        Console.WriteLine("Initializing meta DB");

        using SqliteConnection connection = InitDatabase();
        CreateTables(connection);
        var transaction = connection.BeginTransaction();

        try
        {
            Console.WriteLine("Fetch root manifest");
            var rootEntries = await LoadManifest(rootUrl, true);
            InsertRow(connection, Encoding.UTF8.GetBytes(ROOT_MANIFEST), 3, rootEntry, 0);

            Console.WriteLine($"Available platforms: {string.Join(", ", rootEntries.Select((e) => Encoding.UTF8.GetString(e.name[2..])).ToArray())}");

            foreach (var item in rootEntries)
            {
                if (Encoding.UTF8.GetString(item.name).Equals($"{MANIFEST_NAME_PREFIX}{platform}"))
                {
                    Console.WriteLine($"Fetch platform manifest: {platform}");
                    InsertRow(connection, Encoding.UTF8.GetBytes(PLATFORM_MANIFEST), 2, item, 0);
                    var manifestEntries = await LoadManifest($"{manifestUrl}/{item.hname[..2]}/{item.hname}", true);
                    foreach (var item1 in manifestEntries)
                    {
                        InsertRow(connection, Encoding.UTF8.GetBytes(ASSET_MANIFEST), 1, item1, 0);
                    }
                    foreach (var item1 in manifestEntries)
                    {
                        var categoryName = Encoding.UTF8.GetString(item1.name[2..]);
                        Console.WriteLine($"Fetch asset manifest: {categoryName}");
                        var entries = await LoadManifest($"{manifestUrl}/{item1.hname[..2]}/{item1.hname}", false);

                        foreach (var item2 in entries)
                        {
                            InsertRow(connection, item1.name[2..], 0, item2, 0);
                        }
                    }
                    break;
                }
            }
        }
        catch (Exception e)
        {
            Console.WriteLine($"Failed to fetch manifest: {e.ToString()}");
        }
        finally
        {
            transaction.Commit();
        }

        return 0;
    }
}
