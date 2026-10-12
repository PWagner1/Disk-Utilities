namespace HandleFinder.Services;

/// <summary>Decodes FILE_* / generic access bits into short human-readable labels.</summary>
public static class AccessRightsFormatter
{
    // Win32 FILE_* and GENERIC_* (subset commonly seen on file handles)
    private const uint FileReadData = 0x0001;
    private const uint FileWriteData = 0x0002;
    private const uint FileAppendData = 0x0004;
    private const uint FileReadEa = 0x0008;
    private const uint FileWriteEa = 0x0010;
    private const uint FileExecute = 0x0020;
    private const uint FileDeleteChild = 0x0040;
    private const uint FileReadAttributes = 0x0080;
    private const uint FileWriteAttributes = 0x0100;
    private const uint Delete = 0x00010000;
    private const uint ReadControl = 0x00020000;
    private const uint WriteDac = 0x00040000;
    private const uint WriteOwner = 0x00080000;
    private const uint Synchronize = 0x00100000;
    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;
    private const uint GenericExecute = 0x20000000;
    private const uint GenericAll = 0x10000000;

    /// <summary>Formats access mask as "0x… — Read, Write, …" (or hex only if unknown).</summary>
    public static string Format(uint access)
    {
        var parts = new List<string>();
        void Add(uint bit, string label)
        {
            if ((access & bit) != 0)
            {
                parts.Add(label);
            }
        }

        Add(GenericAll, "All");
        Add(GenericRead, "GenericRead");
        Add(GenericWrite, "GenericWrite");
        Add(GenericExecute, "GenericExecute");
        Add(FileReadData, "Read");
        Add(FileWriteData, "Write");
        Add(FileAppendData, "Append");
        Add(FileExecute, "Execute");
        Add(FileReadAttributes, "ReadAttr");
        Add(FileWriteAttributes, "WriteAttr");
        Add(FileReadEa, "ReadEA");
        Add(FileWriteEa, "WriteEA");
        Add(FileDeleteChild, "DeleteChild");
        Add(Delete, "Delete");
        Add(ReadControl, "ReadControl");
        Add(WriteDac, "WriteDAC");
        Add(WriteOwner, "WriteOwner");
        Add(Synchronize, "Sync");

        string hex = $"0x{access:X8}";
        return parts.Count == 0 ? hex : $"{hex} — {string.Join(", ", parts)}";
    }
}
