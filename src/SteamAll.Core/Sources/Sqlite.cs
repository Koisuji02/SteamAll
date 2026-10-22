// =====================================================================================
//  Sqlite.cs
//  Lettura di un database SQLite (GOG GALAXY, Amazon Games, itch.io) senza librerie in più:
//  usiamo la copia di SQLite che ogni sistema ha già:
//    Windows  winsqlite3.dll (fa parte di Windows 10 e 11)
//    macOS    /usr/lib/libsqlite3.dylib
//    Linux    libsqlite3.so.0 (c'è praticamente sempre)
//
//  SQLite è scritto in C, non in .NET. Per chiamarlo si usa "P/Invoke": dichiariamo le sue
//  funzioni con [DllImport] e .NET si occupa di chiamarle. Il nome "sqlite3" lo traduce un
//  "resolver" (vedi il costruttore statico) nella libreria giusta per il sistema.
// =====================================================================================

using System.Reflection;
using System.Runtime.InteropServices;   // DllImport, Marshal, NativeLibrary
using System.Text;

namespace SteamAll.Core.Sources;

internal static class Sqlite
{
    private const string Dll = "sqlite3";

    // Un costruttore "static" parte una volta sola, prima del primo uso della classe.
    static Sqlite()
    {
        NativeLibrary.SetDllImportResolver(Assembly.GetExecutingAssembly(), (name, assembly, path) =>
        {
            if (name != Dll) return IntPtr.Zero;   // IntPtr.Zero = "fai tu, come al solito"
            string library = OperatingSystem.IsWindows() ? "winsqlite3.dll"
                           : OperatingSystem.IsMacOS() ? "/usr/lib/libsqlite3.dylib"
                           : "libsqlite3.so.0";
            return NativeLibrary.Load(library, assembly, path);
        });
    }

    // ---------------------------------------------------------------------------------
    //  Le funzioni di SQLite che ci servono. "extern" = il codice è nella libreria; EntryPoint =
    //  il nome vero della funzione. IntPtr = un "puntatore", un indirizzo di memoria con cui SQLite
    //  ci fa riconoscere il database aperto (db) o la query in corso (statement).
    // ---------------------------------------------------------------------------------

    [DllImport(Dll, EntryPoint = "sqlite3_open_v2")]
    private static extern int Open(byte[] fileName, out IntPtr db, int flags, IntPtr vfs);

    [DllImport(Dll, EntryPoint = "sqlite3_prepare_v2")]
    private static extern int Prepare(IntPtr db, byte[] sql, int length, out IntPtr statement, IntPtr tail);

    [DllImport(Dll, EntryPoint = "sqlite3_step")]
    private static extern int Step(IntPtr statement);

    [DllImport(Dll, EntryPoint = "sqlite3_column_count")]
    private static extern int ColumnCount(IntPtr statement);

    [DllImport(Dll, EntryPoint = "sqlite3_column_type")]
    private static extern int ColumnType(IntPtr statement, int column);

    [DllImport(Dll, EntryPoint = "sqlite3_column_text")]
    private static extern IntPtr ColumnText(IntPtr statement, int column);

    [DllImport(Dll, EntryPoint = "sqlite3_column_bytes")]
    private static extern int ColumnBytes(IntPtr statement, int column);

    [DllImport(Dll, EntryPoint = "sqlite3_finalize")]
    private static extern int FinalizeStatement(IntPtr statement);   // non "Finalize": in C# è un nome speciale

    [DllImport(Dll, EntryPoint = "sqlite3_close")]
    private static extern int Close(IntPtr db);

    [DllImport(Dll, EntryPoint = "sqlite3_errmsg")]
    private static extern IntPtr ErrorMessage(IntPtr db);

    // Codici di SQLite (dalla sua documentazione).
    private const int SqliteOk = 0, SqliteRow = 100, SqliteDone = 101, SqliteNull = 5;
    private const int OpenReadWrite = 0x02;

    /// <summary>
    /// Esegue una query e restituisce le righe: un array di testi per riga, uno per colonna
    /// (null dove il valore è NULL). Il database si apre in lettura e scrittura perché così SQLite
    /// applica il file "-wal" (le ultime modifiche): per questo si lavora SEMPRE su una copia.
    /// </summary>
    public static List<string?[]> Query(string file, string sql)
    {
        var rows = new List<string?[]>();
        int rc = Open(Utf8WithZero(file), out IntPtr db, OpenReadWrite, IntPtr.Zero);
        // try/finally: "finally" viene eseguito SEMPRE, anche dopo un errore: chiude il database.
        try
        {
            if (rc != SqliteOk) throw new IOException($"SQLite: can't open the database ({rc})");
            byte[] query = Utf8WithZero(sql);
            if (Prepare(db, query, query.Length, out IntPtr statement, IntPtr.Zero) != SqliteOk)
                throw new IOException("SQLite: " + LastError(db));
            try
            {
                int columns = ColumnCount(statement);
                // Step avanza di una riga: SqliteRow finché ce ne sono, poi SqliteDone.
                while ((rc = Step(statement)) == SqliteRow)
                {
                    var row = new string?[columns];
                    for (int c = 0; c < columns; c++)
                    {
                        if (ColumnType(statement, c) == SqliteNull) continue;   // resta null
                        IntPtr text = ColumnText(statement, c);
                        var bytes = new byte[ColumnBytes(statement, c)];
                        if (bytes.Length > 0) Marshal.Copy(text, bytes, 0, bytes.Length);
                        row[c] = Encoding.UTF8.GetString(bytes);
                    }
                    rows.Add(row);
                }
                if (rc != SqliteDone) throw new IOException("SQLite: " + LastError(db));
            }
            finally
            {
                FinalizeStatement(statement);
            }
        }
        finally
        {
            if (db != IntPtr.Zero) Close(db);
        }
        return rows;
    }

    /// <summary>
    /// I nomi delle colonne di una tabella (vuoto se la tabella non c'è). Serve quando programmi
    /// diversi, o versioni diverse, chiamano le colonne in modo diverso (game_id o gameId).
    /// PRAGMA table_info dà una riga per colonna; il nome è nella seconda colonna.
    /// </summary>
    public static HashSet<string> Columns(string file, string table) =>
        Query(file, $"PRAGMA table_info({table})").Select(r => r[1] ?? "").Where(n => n.Length > 0).ToHashSet(StringComparer.OrdinalIgnoreCase);

    // Il C vuole testi UTF-8 che finiscono con un byte 0.
    private static byte[] Utf8WithZero(string s) => Encoding.UTF8.GetBytes(s + "\0");

    private static string LastError(IntPtr db)
    {
        IntPtr message = ErrorMessage(db);
        return message == IntPtr.Zero ? "unknown error" : Marshal.PtrToStringUTF8(message) ?? "unknown error";
    }
}
