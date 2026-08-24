using Postgrest.Attributes;
using Postgrest.Models;

namespace AxisApp.Models;

[Table("categories")]
public class Category : BaseModel
{
    [PrimaryKey("id")]
    public Guid Id { get; set; }

    [Column("name")]
    public string Name { get; set; } = "";

    [Column("created_by")]
    public Guid CreatedBy { get; set; }
}
