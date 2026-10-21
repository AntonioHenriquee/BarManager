using Microsoft.EntityFrameworkCore;
namespace MeuBar;

public abstract class Record { public Guid Id { get; set; } = Guid.NewGuid(); public long Version { get; set; } }
public class User : Record {
 public string Name {get;set;}=""; public string Login {get;set;}=""; public string PasswordHash {get;set;}="";
 public string Role {get;set;}="Employee"; public bool CanReceive {get;set;} public bool CanCloseCash {get;set;}
 public bool Active {get;set;}=true;
}
public class Session : Record { public Guid UserId {get;set;} public string TokenHash {get;set;}=""; public DateTimeOffset ExpiresAt {get;set;} }
public class Customer : Record {
 public string Name {get;set;}=""; public string Nickname {get;set;}=""; public string Phone {get;set;}="";
 public string WhatsApp {get;set;}=""; public string Address {get;set;}=""; public string Notes {get;set;}="";
 public decimal CreditLimit {get;set;} public bool CreditAuthorized {get;set;} public bool Active {get;set;}=true;
 public DateTimeOffset CreatedAt {get;set;}=DateTimeOffset.UtcNow;
}
public class Product : Record {
 public string Code {get;set;}=""; public string Name {get;set;}=""; public string Category {get;set;}="";
 public string Unit {get;set;}="un"; public string Barcode {get;set;}=""; public string Supplier {get;set;}="";
 public decimal Cost {get;set;} public decimal Price {get;set;} public decimal Stock {get;set;} public decimal MinimumStock {get;set;}
 public bool Active {get;set;}=true;
}
public class ServiceTable : Record { public string Name {get;set;}=""; public string Description {get;set;}=""; public bool Active {get;set;}=true; }
public class Tab : Record {
 public long Number {get;set;} public Guid? TableId {get;set;} public string Consumer {get;set;}="Consumidor"; public string Notes {get;set;}="";
 public string Status {get;set;}="Open"; public Guid OpenedBy {get;set;} public DateTimeOffset OpenedAt {get;set;}=DateTimeOffset.UtcNow;
 public DateTimeOffset? ClosedAt {get;set;}
}
public class TabItem : Record {
 public Guid TabId {get;set;} public Guid ProductId {get;set;} public string ProductName {get;set;}="";
 public decimal Quantity {get;set;} public decimal UnitPrice {get;set;} public decimal UnitCost {get;set;}
 public bool Cancelled {get;set;} public string CancelReason {get;set;}=""; public Guid AddedBy {get;set;} public DateTimeOffset AddedAt {get;set;}=DateTimeOffset.UtcNow;
}
public class Sale : Record {
 public Guid TabId {get;set;} public Guid? CustomerId {get;set;} public Guid UserId {get;set;}
 public decimal Total {get;set;} public decimal Discount {get;set;} public decimal Cost {get;set;}
 public DateTimeOffset CreatedAt {get;set;}=DateTimeOffset.UtcNow;
}
public class Receivable : Record {
 public Guid SaleId {get;set;} public Guid CustomerId {get;set;} public decimal Original {get;set;} public decimal Balance {get;set;}
 public DateTimeOffset? DueAt {get;set;}
}
public class Payment : Record {
 public Guid? SaleId {get;set;} public Guid? ReceivableId {get;set;} public Guid UserId {get;set;} public Guid CashSessionId {get;set;}
 public decimal Amount {get;set;} public string Method {get;set;}=""; public DateTimeOffset CreatedAt {get;set;}=DateTimeOffset.UtcNow;
 public bool Reversed {get;set;} public string Reason {get;set;}="";
}
public class StockMovement : Record {
 public Guid ProductId {get;set;} public Guid? TabItemId {get;set;} public decimal Quantity {get;set;} public string Reason {get;set;}="";
 public Guid UserId {get;set;} public DateTimeOffset CreatedAt {get;set;}=DateTimeOffset.UtcNow;
}
public class CashSession : Record {
 public Guid OpenedBy {get;set;} public DateTimeOffset OpenedAt {get;set;}=DateTimeOffset.UtcNow; public decimal Initial {get;set;}
 public DateTimeOffset? ClosedAt {get;set;} public Guid? ClosedBy {get;set;} public decimal? Counted {get;set;} public decimal? Expected {get;set;}
 public bool IsOpen {get;set;}=true;
}
public class CashMovement : Record {
 public Guid CashSessionId {get;set;} public Guid UserId {get;set;} public Guid? PaymentId {get;set;}
 public string Kind {get;set;}=""; public string Method {get;set;}="Cash"; public decimal Amount {get;set;}
 public string Description {get;set;}=""; public DateTimeOffset CreatedAt {get;set;}=DateTimeOffset.UtcNow;
}
public class Audit : Record {
 public Guid UserId {get;set;} public string Action {get;set;}=""; public string Details {get;set;}=""; public DateTimeOffset CreatedAt {get;set;}=DateTimeOffset.UtcNow;
}
public class Operation : Record { public Guid UserId {get;set;} public string Kind {get;set;}=""; public string RequestHash {get;set;}=""; public string Result {get;set;}=""; }
public class Outbox {
 public long Sequence {get;set;} public Guid Id {get;set;}=Guid.NewGuid(); public string DeviceId {get;set;}="";
 public string Payload {get;set;}=""; public DateTimeOffset CreatedAt {get;set;}=DateTimeOffset.UtcNow; public DateTimeOffset? SentAt {get;set;}
}
public class ReplicaState { public string DeviceId {get;set;}=""; public long Sequence {get;set;} public DateTimeOffset AppliedAt {get;set;} }
public class SyncHealth { public int Id {get;set;}=1; public DateTimeOffset? LastSuccess {get;set;} public string Error {get;set;}=""; }
public class BackupRun { public Guid Id {get;set;}=Guid.NewGuid(); public DateTimeOffset At {get;set;}=DateTimeOffset.UtcNow; public bool Success {get;set;} public string Message {get;set;}=""; }

public class BarDb(DbContextOptions<BarDb> options) : DbContext(options) {
 public DbSet<User> Users => Set<User>(); public DbSet<Session> Sessions => Set<Session>();
 public DbSet<Customer> Customers => Set<Customer>(); public DbSet<Product> Products => Set<Product>(); public DbSet<ServiceTable> Tables => Set<ServiceTable>();
 public DbSet<Tab> Tabs => Set<Tab>(); public DbSet<TabItem> Items => Set<TabItem>(); public DbSet<Sale> Sales => Set<Sale>();
 public DbSet<Receivable> Receivables => Set<Receivable>(); public DbSet<Payment> Payments => Set<Payment>();
 public DbSet<StockMovement> StockMovements => Set<StockMovement>(); public DbSet<CashSession> CashSessions => Set<CashSession>();
 public DbSet<CashMovement> CashMovements => Set<CashMovement>(); public DbSet<Audit> Audits => Set<Audit>();
 public DbSet<Operation> Operations => Set<Operation>(); public DbSet<Outbox> Outbox => Set<Outbox>();
 public DbSet<ReplicaState> ReplicaStates => Set<ReplicaState>(); public DbSet<SyncHealth> SyncHealth => Set<SyncHealth>(); public DbSet<BackupRun> BackupRuns => Set<BackupRun>();
 protected override void OnModelCreating(ModelBuilder b) {
  foreach(var type in new[]{typeof(User),typeof(Session),typeof(Customer),typeof(Product),typeof(ServiceTable),typeof(Tab),typeof(TabItem),typeof(Sale),typeof(Receivable),typeof(Payment),typeof(StockMovement),typeof(CashSession),typeof(CashMovement),typeof(Audit),typeof(Operation)}) {
   b.Entity(type).HasKey("Id"); b.Entity(type).Property("Version").IsConcurrencyToken();
  }
  foreach(var p in b.Model.GetEntityTypes().SelectMany(t=>t.GetProperties()).Where(p=>p.ClrType==typeof(decimal)||p.ClrType==typeof(decimal?))) { p.SetPrecision(18); p.SetScale(p.Name is "Quantity" or "Stock" or "MinimumStock" ? 3 : 2); }
  b.Entity<User>().HasIndex(x=>x.Login).IsUnique(); b.Entity<Session>().HasIndex(x=>x.TokenHash).IsUnique();
  b.Entity<Session>().HasOne<User>().WithMany().HasForeignKey(x=>x.UserId).OnDelete(DeleteBehavior.Restrict);
  b.Entity<Product>().HasIndex(x=>x.Code).IsUnique(); b.Entity<ServiceTable>().HasIndex(x=>x.Name).IsUnique();
  b.Entity<Tab>().Property(x=>x.Number).UseIdentityByDefaultColumn(); b.Entity<Tab>().HasIndex(x=>x.Number).IsUnique();
  b.Entity<Tab>().HasIndex(x=>x.TableId).IsUnique().HasFilter("\"Status\" IN ('Open', 'Closing') AND \"TableId\" IS NOT NULL");
  b.Entity<Tab>().HasOne<ServiceTable>().WithMany().HasForeignKey(x=>x.TableId).OnDelete(DeleteBehavior.Restrict);
  b.Entity<Tab>().HasOne<User>().WithMany().HasForeignKey(x=>x.OpenedBy).OnDelete(DeleteBehavior.Restrict);
  b.Entity<TabItem>().HasOne<Tab>().WithMany().HasForeignKey(x=>x.TabId).OnDelete(DeleteBehavior.Restrict);
  b.Entity<TabItem>().HasOne<Product>().WithMany().HasForeignKey(x=>x.ProductId).OnDelete(DeleteBehavior.Restrict);
  b.Entity<Sale>().HasIndex(x=>x.TabId).IsUnique(); b.Entity<Sale>().HasOne<Tab>().WithMany().HasForeignKey(x=>x.TabId).OnDelete(DeleteBehavior.Restrict);
  b.Entity<Sale>().HasOne<Customer>().WithMany().HasForeignKey(x=>x.CustomerId).OnDelete(DeleteBehavior.Restrict);
  b.Entity<Receivable>().HasOne<Sale>().WithMany().HasForeignKey(x=>x.SaleId).OnDelete(DeleteBehavior.Restrict);
  b.Entity<Receivable>().HasOne<Customer>().WithMany().HasForeignKey(x=>x.CustomerId).OnDelete(DeleteBehavior.Restrict);
  b.Entity<Payment>().HasOne<Sale>().WithMany().HasForeignKey(x=>x.SaleId).OnDelete(DeleteBehavior.Restrict);
  b.Entity<Payment>().HasOne<Receivable>().WithMany().HasForeignKey(x=>x.ReceivableId).OnDelete(DeleteBehavior.Restrict);
  b.Entity<Payment>().HasOne<CashSession>().WithMany().HasForeignKey(x=>x.CashSessionId).OnDelete(DeleteBehavior.Restrict);
  b.Entity<CashSession>().HasIndex(x=>x.IsOpen).IsUnique().HasFilter("\"IsOpen\" = TRUE");
  b.Entity<CashMovement>().HasOne<CashSession>().WithMany().HasForeignKey(x=>x.CashSessionId).OnDelete(DeleteBehavior.Restrict);
  b.Entity<CashMovement>().HasOne<Payment>().WithMany().HasForeignKey(x=>x.PaymentId).OnDelete(DeleteBehavior.Restrict);
  b.Entity<StockMovement>().HasOne<Product>().WithMany().HasForeignKey(x=>x.ProductId).OnDelete(DeleteBehavior.Restrict);
  b.Entity<StockMovement>().HasOne<TabItem>().WithMany().HasForeignKey(x=>x.TabItemId).OnDelete(DeleteBehavior.Restrict);
  foreach(var type in new[]{typeof(Sale),typeof(Payment),typeof(StockMovement),typeof(CashMovement),typeof(Audit),typeof(Operation)}) b.Entity(type).HasOne(typeof(User)).WithMany().HasForeignKey("UserId").OnDelete(DeleteBehavior.Restrict);
  b.Entity<TabItem>().HasOne<User>().WithMany().HasForeignKey(x=>x.AddedBy).OnDelete(DeleteBehavior.Restrict);
  b.Entity<CashSession>().HasOne<User>().WithMany().HasForeignKey(x=>x.OpenedBy).OnDelete(DeleteBehavior.Restrict);
  b.Entity<Outbox>().HasKey(x=>x.Sequence); b.Entity<Outbox>().Property(x=>x.Sequence).ValueGeneratedNever(); b.Entity<Outbox>().HasIndex(x=>x.Id).IsUnique();
  b.Entity<ReplicaState>().HasKey(x=>x.DeviceId); b.Entity<SyncHealth>().HasKey(x=>x.Id); b.Entity<BackupRun>().HasKey(x=>x.Id);
  b.Entity<Product>().ToTable(t=>{t.HasCheckConstraint("product_values","\"Price\" >= 0 AND \"Cost\" >= 0 AND \"Stock\" >= 0");});
  b.Entity<Receivable>().ToTable(t=>t.HasCheckConstraint("receivable_balance","\"Balance\" >= 0 AND \"Balance\" <= \"Original\""));
  b.Entity<Payment>().ToTable(t=>t.HasCheckConstraint("payment_positive","\"Amount\" > 0"));
 }
}
