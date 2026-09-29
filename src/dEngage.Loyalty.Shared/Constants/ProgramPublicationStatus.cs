namespace dEngage.Loyalty.Shared;

// 1.3.CL item 8: a program's release lifecycle, orthogonal to ProgramStatus (active/inactive).
// A new program is Draft (and inactive — it cannot be activated); Publish makes it Published and
// writes a ProgramPublication ConfigVersion snapshot. There is no way back to Draft (§5 h).
public static class ProgramPublicationStatus
{
    public const string Draft = "draft";
    public const string Published = "published";
}
