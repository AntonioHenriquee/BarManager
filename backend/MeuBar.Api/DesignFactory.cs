using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
namespace MeuBar;
public class DesignFactory : IDesignTimeDbContextFactory<BarDb> {
 public BarDb CreateDbContext(string[] args)=>new(new DbContextOptionsBuilder<BarDb>().UseNpgsql("Host=localhost;Database=meubar;Username=meubar;Password=design-only").Options);
}
