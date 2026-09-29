namespace dEngage.Loyalty.Engine.Framework.Abstractions;

public interface IIdGenerator
{
    Guid NewId();
}

public sealed class SystemIdGenerator : IIdGenerator
{
    public Guid NewId() => UUIDNext.Uuid.NewDatabaseFriendly(UUIDNext.Database.PostgreSql);
}
