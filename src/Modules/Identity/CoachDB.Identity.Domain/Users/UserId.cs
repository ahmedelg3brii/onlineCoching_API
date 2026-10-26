namespace CoachDB.Identity.Domain.Users;

/// <summary>Strongly typed id, so a <see cref="UserId"/> can't be passed where another id is expected.</summary>
public readonly record struct UserId(Guid Value)
{
    public static UserId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}
