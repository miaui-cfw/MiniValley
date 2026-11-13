namespace SdvAot;

/// <summary>Single-player sync layer (patcher mode netslim edits Netcode inside the game assembly):
/// - NetFields.AddField no longer builds "Owner: field" debug names (one string per net field, ~12 MB on PC);
/// - NetFields.GetNameForInstance returns null (one "Base (Derived)" string per object);
/// - AbstractNetSerializable.SetDirtySooner no longer copies the clock's version vector into ChangeVersion
///   (a List per changed field; only network conflict resolution reads it).
/// The names only ever appear in error messages, which handle null.</summary>
public static class NetSlim
{
    public static string NoName(string a, string b, string c) => null;
}
