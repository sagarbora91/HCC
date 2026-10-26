using Hcc.Infrastructure.SqlServer.Migrations;

namespace Hcc.Operations.Tests;

/// <summary>A09-04: the schema-version comparison behind the server's refusal to serve (no database; the HTTP side is in
/// Hcc.Integration.Tests SchemaGuardTests).</summary>
public sealed class SchemaStatusTests
{
    private static MigrationScript S(int version) => new(version, "m" + version, "SELECT 1;");

    private static readonly MigrationScript[] Build = [S(1), S(100), S(2702)];

    [Fact]
    public void All_build_migrations_applied_is_current()
    {
        var s = SchemaStatus.Compare(new HashSet<int> { 1, 100, 2702 }, Build);
        Assert.False(s.IsBehind);
        Assert.Equal((3, 3, 2702, 2702), (s.AppliedCount, s.BuildCount, s.DatabaseVersion, s.BuildVersion));
    }

    [Fact]
    public void A_missing_build_migration_is_behind_with_the_migrate_message()
    {
        var s = SchemaStatus.Compare(new HashSet<int> { 1, 100 }, Build);
        Assert.True(s.IsBehind);
        Assert.Equal([2702], s.Missing);
        Assert.Equal("Database schema is behind this build: run `Hcc.Api migrate` (applied 2 of 3).", s.Message);
    }

    [Fact]
    public void An_empty_database_is_behind_and_a_newer_database_is_not()
    {
        var empty = SchemaStatus.Compare(new HashSet<int>(), Build);
        Assert.True(empty.IsBehind);
        Assert.Equal((0, 3, 0), (empty.AppliedCount, empty.Missing.Count, empty.DatabaseVersion));
        var newer = SchemaStatus.Compare(new HashSet<int> { 1, 100, 2702, 2800 }, Build);   // migrate refuses that case itself
        Assert.False(newer.IsBehind);
        Assert.Equal(2800, newer.DatabaseVersion);
    }

    [Fact]
    public void The_embedded_migrations_of_this_build_compare_as_current_with_themselves()
    {
        var scripts = new EmbeddedMigrationSource().Load();
        var s = SchemaStatus.Compare(scripts.Select(x => x.Version).ToHashSet(), scripts);
        Assert.False(s.IsBehind);
        Assert.Equal(scripts.Count, s.AppliedCount);
    }
}
