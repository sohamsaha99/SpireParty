using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;

namespace SpireParty.Core;

/// <summary>
/// The party's run save and history, written with the game's own serializer but to SpireParty's
/// folder, never through the game's save store or Steam Cloud (hard rule 1).
/// </summary>
public static class PartyRunSave
{
    public static bool Exists(string path) => File.Exists(path) || File.Exists(path + ".backup");

    public static ReadSaveResult<SerializableRun> Read(string path)
    {
        string? source = File.Exists(path) ? path : File.Exists(path + ".backup") ? path + ".backup" : null;
        if (source == null) return new ReadSaveResult<SerializableRun>(ReadSaveStatus.FileNotFound);
        try
        {
            return JsonSerializationUtility.FromJson<SerializableRun>(File.ReadAllText(source));
        }
        catch (Exception e)
        {
            return new ReadSaveResult<SerializableRun>(ReadSaveStatus.Unrecoverable, $"{source}: {e.Message}");
        }
    }

    /// <summary>Writes atomically and keeps the previous save as run.save.backup.</summary>
    public static Task WriteAsync(string path, SerializableRun save)
    {
        using var stream = new MemoryStream();
        JsonSerializer.Serialize(stream, save, JsonSerializationUtility.GetTypeInfo<SerializableRun>());
        byte[] bytes = stream.ToArray();
        return Task.Run(() => WriteAtomic(path, bytes));
    }

    public static void Delete(string path)
    {
        File.Delete(path);
        File.Delete(path + ".backup");
    }

    public static void WriteHistory(string historyDir, RunHistory history)
    {
        Directory.CreateDirectory(historyDir);
        string json = JsonSerializationUtility.ToJson(history);
        File.WriteAllText(Path.Combine(historyDir, $"{history.StartTime}.run"), json);
    }

    static readonly object WriteLock = new();

    static void WriteAtomic(string path, byte[] bytes)
    {
        lock (WriteLock)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string tmp = path + ".tmp";
            File.WriteAllBytes(tmp, bytes);
            if (File.Exists(path)) File.Copy(path, path + ".backup", overwrite: true);
            File.Move(tmp, path, overwrite: true);
        }
    }
}
