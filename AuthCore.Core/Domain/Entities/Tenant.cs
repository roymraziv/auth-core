namespace AuthCore.Core.Domain.Entities;

public class Tenant
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public required string ApiKeyHash { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}