namespace dEngage.Loyalty.Shared;

public static class ConfigChangeType
{
    public const string Created = "created";
    public const string Updated = "updated";
    public const string Deleted = "deleted";
    // 1.3.CL item 9: an aggregate snapshot of a program and everything nested in it, written by
    // Publish under EntityType "ProgramPublication" (own version sequence = the publish number).
    public const string Published = "published";
}
