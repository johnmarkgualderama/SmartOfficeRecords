using Microsoft.EntityFrameworkCore;
using SmartOfficeRecords.Models;

namespace SmartOfficeRecords.Data
{
    public class ApplicationDbContext : DbContext
    {
        // This constructor receives the connection string settings
        // that we configure in Program.cs
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        // This represents the ApplicantRegister table in SQL Server.
        // EF Core uses this to run INSERT/SELECT/UPDATE/DELETE commands.
        public DbSet<ApplicantRegister> ApplicantRegisters { get; set; }
        public DbSet<Admin> Admins { get; set; }
        public DbSet<Appointment> Appointments { get; set; }
    }
}